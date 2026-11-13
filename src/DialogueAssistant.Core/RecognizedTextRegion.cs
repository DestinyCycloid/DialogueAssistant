namespace DialogueAssistant.Core;

/// <summary>以捕获帧像素坐标表示的一条 OCR 文本及其边界框。</summary>
/// <param name="Text">OCR 识别出的文字。</param>
/// <param name="Bounds">文字在捕获帧中的边界框。</param>
/// <param name="Confidence">OCR 置信度，取值范围为 0 到 1。</param>
public sealed record RecognizedTextRegion(string Text, DialogueOptionBounds Bounds, double Confidence);
