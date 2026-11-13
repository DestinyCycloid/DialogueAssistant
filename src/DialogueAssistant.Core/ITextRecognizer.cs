namespace DialogueAssistant.Core;

/// <summary>由独立宿主实现的 OCR 边界，只识别指定帧区域并返回帧坐标结果。</summary>
public interface ITextRecognizer
{
    /// <summary>识别指定区域中的文字；返回区域框必须使用整张捕获帧坐标。</summary>
    IReadOnlyList<RecognizedTextRegion> Recognize(CaptureFrame frame, DialogueOptionBounds searchBounds,
        CancellationToken cancellationToken = default);
}
