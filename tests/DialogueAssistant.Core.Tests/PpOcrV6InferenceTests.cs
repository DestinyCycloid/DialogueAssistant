using System.Runtime.InteropServices;
using DialogueAssistant.Core;
using DialogueAssistant.Recognition;
using OpenCvSharp;

namespace DialogueAssistant.Core.Tests;

/// <summary>使用随模型包提供的中文样例验证 PP-OCRv6 模型加载及端到端推理。</summary>
public sealed class PpOcrV6InferenceTests
{
    /// <summary>加载独立发布目录中的 ONNX 模型，并确认样例中的中文文本被识别。</summary>
    [Fact]
    public void Recognize_BundledChineseSample_ReturnsExpectedText()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Model", "PaddleOCR", "test_pp_ocr.png");
        Assert.True(File.Exists(samplePath), $"OCR 测试样例缺失：{samplePath}");

        using var source = Cv2.ImRead(samplePath, ImreadModes.Color);
        Assert.False(source.Empty());
        using var bgra = new Mat();
        Cv2.CvtColor(source, bgra, ColorConversionCodes.BGR2BGRA);
        var pixels = new byte[checked(bgra.Width * bgra.Height * 4)];
        Marshal.Copy(bgra.Data, pixels, 0, pixels.Length);
        var frame = new CaptureFrame(pixels, bgra.Width, bgra.Height, DateTimeOffset.UtcNow);

        using var recognizer = new PpOcrV6TextRecognizer();
        var regions = recognizer.Recognize(frame,
            new DialogueOptionBounds(0, 0, frame.Width, frame.Height));

        Assert.NotEmpty(regions);
        Assert.Contains(regions, region => region.Text.Contains("派遣", StringComparison.Ordinal)
                                           || region.Text.Contains("凯瑟琳", StringComparison.Ordinal));
    }
}
