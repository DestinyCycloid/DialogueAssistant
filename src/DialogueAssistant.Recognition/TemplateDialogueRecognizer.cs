using System.Security.Cryptography;
using DialogueAssistant.Core;
using OpenCvSharp;

namespace DialogueAssistant.Recognition;

/// <summary>以选项气泡模板定位选项标记，并仅在可选 OCR 提供者返回文字区域后生成点击候选。</summary>
public sealed class TemplateDialogueRecognizer : IDialogueRecognizer, IDisposable
{
    /// <summary>选项模板匹配分数最低门槛。</summary>
    private const double MatchThreshold = 0.88;

    /// <summary>每帧最多检查的选项图标数。</summary>
    private const int MaximumTemplateCount = 4;
    /// <summary>选项气泡模板图像。</summary>
    private readonly Mat _optionTemplate;

    /// <summary>原对话自动播放按钮图像，用于识别无选项对话画面。</summary>
    private readonly Mat _disabledUiTemplate;

    /// <summary>可选的独立 OCR 服务；未提供时禁用选项动作。</summary>
    private readonly ITextRecognizer? _textRecognizer;

    /// <summary>串行保护 OpenCV 模板访问与释放。</summary>
    private readonly object _syncRoot = new();
    /// <summary>是否已释放模板资源。</summary>
    private bool _disposed;

    /// <summary>加载独立目录中的选项模板，并可注入 OCR 服务。</summary>
    public TemplateDialogueRecognizer(string assetDirectory, ITextRecognizer? textRecognizer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetDirectory);
        _textRecognizer = textRecognizer;
        var manifestPath = Path.Combine(assetDirectory, "manifest.sha256");
        var templatePath = Path.Combine(assetDirectory, "icon_option.png");
        var disabledUiPath = Path.Combine(assetDirectory, "1920x1080", "disabled_ui.png");
        ValidateAsset(templatePath, manifestPath, "icon_option.png");
        ValidateAsset(disabledUiPath, manifestPath, "1920x1080/disabled_ui.png");
        _optionTemplate = Cv2.ImRead(templatePath, ImreadModes.Color);
        _disabledUiTemplate = Cv2.ImRead(disabledUiPath, ImreadModes.Color);
        if (_optionTemplate.Empty() || _disabledUiTemplate.Empty())
        {
            _optionTemplate.Dispose();
            _disabledUiTemplate.Dispose();
            throw new InvalidDataException("自动剧情识别模板无法解码。");
        }
    }

    /// <summary>识别选项气泡并尝试与 OCR 文字区域配对；无 OCR 结果时返回 Unknown。</summary>
    public DialogueRecognitionResult Recognize(CaptureFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var source = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, frame.Pixels);
            using var color = new Mat();
            Cv2.CvtColor(source, color, ColorConversionCodes.BGRA2BGR);
            if (Math.Abs(frame.Width / (double)frame.Height - 16d / 9d) > 0.01)
                return Unknown();

            var assetScale = frame.Width / 1920d;
            using var optionTemplate = ScaleTemplate(_optionTemplate, assetScale);
            using var disabledUiTemplate = ScaleTemplate(_disabledUiTemplate, assetScale);

            // 选项气泡只出现在画面右侧；限制搜索范围以减少菜单或场景纹理误报。
            var searchX = frame.Width / 2;
            var searchY = frame.Height / 12;
            var searchWidth = frame.Width - searchX - frame.Width / 6;
            var searchHeight = frame.Height - searchY - frame.Height / 12 - (int)Math.Round(10 * assetScale);
            if (searchWidth < optionTemplate.Width || searchHeight < optionTemplate.Height)
                return Unknown();

            using var search = new Mat(color, new Rect(searchX, searchY, searchWidth, searchHeight));
            using var response = new Mat();
            Cv2.MatchTemplate(search, optionTemplate, response, TemplateMatchModes.CCoeffNormed);
            var matches = new List<(int X, int Y, double Score)>();
            while (matches.Count < MaximumTemplateCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Cv2.MinMaxLoc(response, out _, out var maxValue, out _, out var maxLocation);
                if (!double.IsFinite(maxValue) || maxValue < MatchThreshold) break;

                matches.Add((searchX + maxLocation.X, searchY + maxLocation.Y, maxValue));
                var left = Math.Max(0, maxLocation.X - optionTemplate.Width / 2);
                var top = Math.Max(0, maxLocation.Y - optionTemplate.Height / 2);
                var right = Math.Min(response.Cols, maxLocation.X + optionTemplate.Width / 2 + 1);
                var bottom = Math.Min(response.Rows, maxLocation.Y + optionTemplate.Height / 2 + 1);
                using var suppressed = new Mat(response, new Rect(left, top, right - left, bottom - top));
                suppressed.SetTo(Scalar.All(-1));
            }

            if (matches.Count == 0)
            {
                // 复用原自动剧情“禁用自动播放按钮”模板识别普通对话界面，供快进策略使用。
                var dialogueConfidence = MatchDisabledDialogueUi(color, disabledUiTemplate);
                return dialogueConfidence >= MatchThreshold
                    ? new DialogueRecognitionResult(DialogueUiState.Dialogue, dialogueConfidence,
                        Array.Empty<DialogueOptionBounds>())
                    : Unknown();
            }
            if (_textRecognizer is null) return Unknown();

            // OCR 只在右侧选项区内运行；识别器返回的位置必须是捕获帧坐标。
            cancellationToken.ThrowIfCancellationRequested();
            var searchBounds = new DialogueOptionBounds(searchX, searchY, searchWidth, searchHeight);
            var textRegions = _textRecognizer.Recognize(frame, searchBounds, cancellationToken);
            if (textRegions is null || textRegions.Count == 0)
                return Unknown();

            // 每个图标只配对同一水平行、位于图标右侧的高置信度文字区域。
            var availableTexts = textRegions
                .Select((text, index) => (Text: text, Index: index))
                .Where(item => IsValidTextRegion(item.Text, frame))
                .ToList();
            var optionBounds = new List<DialogueOptionBounds>();
            var confidence = 1d;
            foreach (var marker in matches.OrderBy(match => match.Y))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var markerCenterY = marker.Y + optionTemplate.Height / 2d;
                var maxDistance = Math.Max(optionTemplate.Height * 3, frame.Height / 18d);
                var paired = availableTexts
                    .Select(item => new
                    {
                        item.Index,
                        item.Text,
                        Distance = Math.Abs(item.Text.Bounds.Y + item.Text.Bounds.Height / 2d - markerCenterY)
                    })
                    .Where(item => item.Text.Bounds.X >= marker.X + optionTemplate.Width
                                   && item.Distance <= maxDistance)
                    .OrderBy(item => item.Distance)
                    .FirstOrDefault();
                if (paired is null) continue;

                availableTexts.RemoveAll(item => item.Index == paired.Index);
                optionBounds.Add(paired.Text.Bounds with { Text = paired.Text.Text });
                confidence = Math.Min(confidence, Math.Min(marker.Score, paired.Text.Confidence));
            }

            if (optionBounds.Count == 0) return Unknown();
            return new DialogueRecognitionResult(DialogueUiState.Options, confidence, optionBounds);
        }
    }

    /// <summary>按 1920×1080 参考分辨率缩放模板；不改动原始资源对象。</summary>
    private static Mat ScaleTemplate(Mat template, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(template.Width * scale));
        var height = Math.Max(1, (int)Math.Round(template.Height * scale));
        if (width == template.Width && height == template.Height) return template.Clone();
        return template.Resize(new Size(width, height), 0, 0, InterpolationFlags.Linear);
    }

    /// <summary>在左上角自动播放按钮区域匹配原版禁用 UI 模板。</summary>
    private static double MatchDisabledDialogueUi(Mat frame, Mat template)
    {
        var roiWidth = frame.Width / 3;
        var roiHeight = frame.Height / 8;
        if (template.Width > roiWidth || template.Height > roiHeight) return 0;
        using var search = new Mat(frame, new Rect(0, 0, roiWidth, roiHeight));
        using var response = new Mat();
        Cv2.MatchTemplate(search, template, response, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(response, out _, out var maximum, out _, out _);
        return double.IsFinite(maximum) ? maximum : 0;
    }

    /// <summary>检查 OCR 文字内容、置信度与坐标是否可安全用于选项点击。</summary>
    private static bool IsValidTextRegion(RecognizedTextRegion? text, CaptureFrame frame)
    {
        if (text is null || string.IsNullOrWhiteSpace(text.Text)
            || !double.IsFinite(text.Confidence) || text.Confidence is < 0.85 or > 1)
            return false;

        var bounds = text.Bounds;
        return bounds.X >= 0 && bounds.Y >= 0 && bounds.Width > 0 && bounds.Height > 0
               && (long)bounds.X + bounds.Width <= frame.Width
               && (long)bounds.Y + bounds.Height <= frame.Height;
    }

    /// <summary>校验指定模板在清单中的 SHA-256 值，缺失或被修改时禁止静默识别。</summary>
    private static void ValidateAsset(string templatePath, string manifestPath, string assetName)
    {
        if (!File.Exists(templatePath) || !File.Exists(manifestPath))
            throw new FileNotFoundException($"自动剧情识别资源缺失：{templatePath}");

        var expected = File.ReadAllLines(manifestPath)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].Equals(assetName, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[0])
            .FirstOrDefault();
        if (expected is null)
            throw new InvalidDataException($"识别资源清单未登记模板：{assetName}");

        using var stream = File.OpenRead(templatePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"识别模板完整性校验失败：{assetName}");
    }

    /// <summary>生成明确的未知识别结果，确保识别不充分时不会产生动作。</summary>
    private static DialogueRecognitionResult Unknown() =>
        new(DialogueUiState.Unknown, 0, Array.Empty<DialogueOptionBounds>());

    /// <summary>释放 OpenCV 模板内存。</summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _optionTemplate.Dispose();
            _disabledUiTemplate.Dispose();
            _disposed = true;
        }
    }
}
