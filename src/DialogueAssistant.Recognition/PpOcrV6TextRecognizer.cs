using System.Text;
using Clipper2Lib;
using DialogueAssistant.Core;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using YamlDotNet.RepresentationModel;

namespace DialogueAssistant.Recognition;

/// <summary>使用独立资源目录中的 PP-OCRv6 ONNX 模型识别捕获帧文字。</summary>
public sealed class PpOcrV6TextRecognizer : ITextRecognizer, IDisposable
{
    /// <summary>检测模型相对独立应用资源根目录的路径。</summary>
    private const string DetectionModelRelativePath = "Assets/Model/PaddleOCR/Det/V6/PP-OCRv6_small_det_infer/slim.onnx";
    /// <summary>识别模型相对独立应用资源根目录的路径。</summary>
    private const string RecognitionModelRelativePath = "Assets/Model/PaddleOCR/Rec/V6/PP-OCRv6_small_rec_infer/slim.onnx";
    /// <summary>检测模型使用的归一化均值。</summary>
    private static readonly float[] DetectionMean = [0.485f, 0.456f, 0.406f];
    /// <summary>检测模型使用的归一化标准差。</summary>
    private static readonly float[] DetectionStd = [0.229f, 0.224f, 0.225f];
    /// <summary>串行化两个会话的推理与资源释放。</summary>
    private readonly object _syncRoot = new();
    /// <summary>PP-OCRv6 文本检测 ONNX 会话。</summary>
    private readonly InferenceSession _detectionSession;
    /// <summary>PP-OCRv6 文本识别 ONNX 会话。</summary>
    private readonly InferenceSession _recognitionSession;
    /// <summary>由识别模型同目录 inference.yml 载入的 CTC 标签字典。</summary>
    private readonly IReadOnlyList<string> _labels;
    /// <summary>标记会话是否已释放。</summary>
    private bool _disposed;

    /// <summary>从应用发布目录或指定独立模型目录加载 PP-OCRv6 模型和字典。</summary>
    /// <param name="applicationDirectory">应用资源根目录；省略时使用当前应用发布目录。</param>
    public PpOcrV6TextRecognizer(string? applicationDirectory = null)
    {
        var root = applicationDirectory ?? AppContext.BaseDirectory;
        var detectionPath = Path.Combine(root, DetectionModelRelativePath);
        var recognitionPath = Path.Combine(root, RecognitionModelRelativePath);
        var configPath = Path.Combine(Path.GetDirectoryName(recognitionPath)!, "inference.yml");
        RequireFile(detectionPath, "PP-OCRv6 检测模型");
        RequireFile(recognitionPath, "PP-OCRv6 识别模型");
        RequireFile(configPath, "PP-OCRv6 识别字典配置");
        _labels = LoadCharacterDictionary(configPath);

        using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _detectionSession = new InferenceSession(detectionPath, options);
        try
        {
            _recognitionSession = new InferenceSession(recognitionPath, options);
        }
        catch
        {
            _detectionSession.Dispose();
            throw;
        }
    }

    /// <summary>识别指定帧区域并返回使用整帧坐标的文本框；同步 ONNX 推理前后均响应取消请求。</summary>
    /// <param name="frame">BGRA32 捕获帧。</param>
    /// <param name="searchBounds">帧坐标中的搜索区域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按阅读顺序排列且置信度不低于 0.5 的文本框。</returns>
    public IReadOnlyList<RecognizedTextRegion> Recognize(CaptureFrame frame, DialogueOptionBounds searchBounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = ClipBounds(searchBounds, frame.Width, frame.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return Array.Empty<RecognizedTextRegion>();

        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            using var full = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC4, frame.Pixels);
            using var bgr = new Mat();
            Cv2.CvtColor(full, bgr, ColorConversionCodes.BGRA2BGR);
            using var source = new Mat(bgr, bounds);
            var boxes = RunDetection(source, cancellationToken);
            var result = new List<RecognizedTextRegion>(boxes.Count);
            foreach (var box in boxes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var crop = CropText(source, box);
                var (text, confidence) = RunRecognition(crop, cancellationToken);
                if (string.IsNullOrWhiteSpace(text) || confidence < 0.5f) continue;
                var x1 = Math.Clamp((int)Math.Floor(box.Min(point => point.X)), 0, source.Width - 1);
                var y1 = Math.Clamp((int)Math.Floor(box.Min(point => point.Y)), 0, source.Height - 1);
                var x2 = Math.Clamp((int)Math.Ceiling(box.Max(point => point.X)), x1 + 1, source.Width);
                var y2 = Math.Clamp((int)Math.Ceiling(box.Max(point => point.Y)), y1 + 1, source.Height);
                result.Add(new RecognizedTextRegion(text,
                    new DialogueOptionBounds(bounds.X + x1, bounds.Y + y1, x2 - x1, y2 - y1), confidence));
            }
            return result.OrderBy(region => region.Bounds.Y).ThenBy(region => region.Bounds.X).ToArray();
        }
    }

    /// <summary>将搜索范围裁剪至帧内，拒绝负尺寸或完全越界的区域。</summary>
    private static Rect ClipBounds(DialogueOptionBounds bounds, int width, int height)
    {
        var left = Math.Clamp(bounds.X, 0, width);
        var top = Math.Clamp(bounds.Y, 0, height);
        var right = (int)Math.Clamp((long)bounds.X + bounds.Width, 0, width);
        var bottom = (int)Math.Clamp((long)bounds.Y + bounds.Height, 0, height);
        return bounds.Width > 0 && bounds.Height > 0 && right > left && bottom > top
            ? new Rect(left, top, right - left, bottom - top)
            : new Rect(0, 0, 0, 0);
    }

    /// <summary>执行 PP-OCRv6 检测预处理、ONNX 推理和 DB 后处理。</summary>
    private List<Point2f[]> RunDetection(Mat source, CancellationToken cancellationToken)
    {
        var preWidth = source.Width;
        var preHeight = source.Height;
        using var tiny = preWidth + preHeight < 64
            ? new Mat(Math.Max(32, preHeight), Math.Max(32, preWidth), source.Type(), Scalar.Black)
            : source.Clone();
        if (preWidth + preHeight < 64)
        {
            using var roi = new Mat(tiny, new Rect(0, 0, preWidth, preHeight));
            source.CopyTo(roi);
        }
        var ratio = Math.Max(tiny.Width, tiny.Height) > 960 ? 960d / Math.Max(tiny.Width, tiny.Height) : 1d;
        var resizedWidth = Math.Max(32, (int)Math.Round(tiny.Width * ratio / 32d) * 32);
        var resizedHeight = Math.Max(32, (int)Math.Round(tiny.Height * ratio / 32d) * 32);
        using var resized = tiny.Resize(new Size(resizedWidth, resizedHeight));
        var input = NormalizeDetection(resized);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _detectionSession.Run([
            NamedOnnxValue.CreateFromTensor(_detectionSession.InputNames[0], input)]);
        cancellationToken.ThrowIfCancellationRequested();
        var tensor = outputs[0].AsTensor<float>();
        var dims = tensor.Dimensions.ToArray();
        if (dims.Length != 4 || dims[0] != 1 || dims[1] != 1 || dims[2] < resizedHeight || dims[3] < resizedWidth)
            throw new InvalidDataException($"PP-OCRv6 检测输出形状无效：{string.Join("x", dims)}");
        using var prediction = new Mat(resizedHeight, resizedWidth, MatType.CV_32FC1);
        for (var y = 0; y < resizedHeight; y++)
            for (var x = 0; x < resizedWidth; x++)
                prediction.At<float>(y, x) = tensor[0, 0, y, x];
        var found = PpOcrDbPostProcessor.Process(prediction, source.Size(), cancellationToken);
        return found;
    }

    /// <summary>依照 PP-OCRv6 配置对 BGR 图像归一化并生成 NCHW 张量。</summary>
    private static DenseTensor<float> NormalizeDetection(Mat image)
    {
        var values = new float[checked(3 * image.Height * image.Width)];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.At<Vec3b>(y, x);
                values[y * image.Width + x] = (pixel.Item0 / 255f - DetectionMean[0]) / DetectionStd[0];
                values[image.Height * image.Width + y * image.Width + x] =
                    (pixel.Item1 / 255f - DetectionMean[1]) / DetectionStd[1];
                values[2 * image.Height * image.Width + y * image.Width + x] =
                    (pixel.Item2 / 255f - DetectionMean[2]) / DetectionStd[2];
            }
        }
        return new DenseTensor<float>(values, [1, 3, image.Height, image.Width]);
    }

    /// <summary>对文本框作透视裁切，长宽比极高时按 PaddleOCR 规则逆时针旋转。</summary>
    private static Mat CropText(Mat source, Point2f[] points)
    {
        var width = (int)Math.Max(Distance(points[0], points[1]), Distance(points[2], points[3]));
        var height = (int)Math.Max(Distance(points[0], points[3]), Distance(points[1], points[2]));
        if (width < 1 || height < 1) throw new InvalidDataException("PP-OCRv6 检测框尺寸无效。");
        Point2f[] destination = [new(0, 0), new(width, 0), new(width, height), new(0, height)];
        using var transform = Cv2.GetPerspectiveTransform(points, destination);
        var crop = new Mat();
        Cv2.WarpPerspective(source, crop, transform, new Size(width, height), InterpolationFlags.Cubic, BorderTypes.Replicate);
        if (crop.Height / (double)crop.Width >= 1.5) Cv2.Rotate(crop, crop, RotateFlags.Rotate90Counterclockwise);
        return crop;
    }

    /// <summary>执行 Rec 图像归一化、ONNX 推理和 CTC 贪心解码。</summary>
    private (string Text, float Confidence) RunRecognition(Mat source, CancellationToken cancellationToken)
    {
        var targetHeight = 48;
        var targetWidth = Math.Max(1, (int)Math.Ceiling(source.Width / (double)source.Height * targetHeight));
        using var resized = source.Resize(new Size(targetWidth, targetHeight));
        var width = targetWidth;
        var values = new float[checked(3 * targetHeight * width)];
        for (var y = 0; y < targetHeight; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = resized.At<Vec3b>(y, x);
                values[y * width + x] = (pixel.Item0 - 127.5f) / 127.5f;
                values[targetHeight * width + y * width + x] = (pixel.Item1 - 127.5f) / 127.5f;
                values[2 * targetHeight * width + y * width + x] = (pixel.Item2 - 127.5f) / 127.5f;
            }
        }
        var tensor = new DenseTensor<float>(values, [1, 3, targetHeight, width]);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _recognitionSession.Run([
            NamedOnnxValue.CreateFromTensor(_recognitionSession.InputNames[0], tensor)]);
        cancellationToken.ThrowIfCancellationRequested();
        var output = outputs[0].AsTensor<float>();
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length != 3 || dimensions[0] != 1)
            throw new InvalidDataException($"PP-OCRv6 识别输出形状无效：{string.Join("x", dimensions)}");
        var steps = dimensions[1];
        var labelsCount = dimensions[2];
        var text = new StringBuilder();
        var lastIndex = 0;
        var score = 0f;
        var count = 0;
        for (var step = 0; step < steps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var maxIndex = 0;
            var maxValue = float.MinValue;
            for (var label = 0; label < labelsCount; label++)
            {
                var current = output[0, step, label];
                if (current > maxValue) { maxValue = current; maxIndex = label; }
            }
            if (maxIndex > 0 && maxIndex != lastIndex)
            {
                var labelIndex = maxIndex - 1;
                if (labelIndex < _labels.Count) text.Append(_labels[labelIndex]);
                else if (labelIndex == _labels.Count) text.Append(' ');
                else throw new InvalidDataException($"识别模型输出标签索引 {maxIndex} 超出字符字典范围 {_labels.Count}。");
                score += maxValue;
                count++;
            }
            lastIndex = maxIndex;
        }
        return (text.ToString(), count == 0 ? 0 : score / count);
    }

    /// <summary>读取模型 inference.yml 中 PostProcess.character_dict，并校验非空列表。</summary>
    private static IReadOnlyList<string> LoadCharacterDictionary(string path)
    {
        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root
            || !root.Children.TryGetValue(new YamlScalarNode("PostProcess"), out var postNode)
            || postNode is not YamlMappingNode post
            || !post.Children.TryGetValue(new YamlScalarNode("character_dict"), out var charactersNode)
            || charactersNode is not YamlSequenceNode characters)
            throw new InvalidDataException($"配置中缺少 PostProcess.character_dict：{path}");
        // PaddleOCR 字符字典包含空占位项；保留为空串以维持模型输出索引与 CTC 标签一一对应。
        var labels = characters.Children.OfType<YamlScalarNode>().Select(node => node.Value ?? string.Empty).ToArray();
        if (labels.Length == 0) throw new InvalidDataException($"字符字典为空：{path}");
        return labels;
    }

    /// <summary>确认模型和配置文件存在，缺失时报告具体独立资源路径。</summary>
    private static void RequireFile(string path, string name)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"{name}缺失，请确认模型资源已随 DialogueAssistant 独立发布：{path}", path);
    }

    /// <summary>计算两个点间的欧氏距离。</summary>
    private static double Distance(Point2f left, Point2f right)
    {
        var dx = left.X - right.X;
        var dy = left.Y - right.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>释放两个 ONNX 会话。</summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _recognitionSession.Dispose();
            _detectionSession.Dispose();
            _disposed = true;
        }
    }
}
