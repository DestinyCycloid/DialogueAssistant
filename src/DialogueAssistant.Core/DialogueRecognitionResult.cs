namespace DialogueAssistant.Core;

/// <summary>描述一帧中检测到的对话 UI 状态及选项位置。</summary>
public sealed record DialogueRecognitionResult(
    DialogueUiState State,
    double Confidence,
    IReadOnlyList<DialogueOptionBounds> Options);

/// <summary>识别器支持的对话界面状态。</summary>
public enum DialogueUiState
{
    /// <summary>画面无法可靠分类，必须不执行输入。</summary>
    Unknown,
    /// <summary>当前帧不是自动剧情支持的对话界面。</summary>
    NotDialogue,
    /// <summary>检测到剧情对话但没有可用选项。</summary>
    Dialogue,
    /// <summary>检测到一个或多个可选择的剧情选项。</summary>
    Options
}

/// <summary>以捕获帧像素坐标表示的对话选项矩形。</summary>
public readonly record struct DialogueOptionBounds(int X, int Y, int Width, int Height, string Text = "")
{
    /// <summary>选项对应的 OCR 文本；缺少文本的调用方使用空字符串。</summary>
    public string Text { get; init; } = Text;

    /// <summary>计算选项矩形中心点，供输入适配器执行点击。</summary>
    public (int X, int Y) Center => (X + Width / 2, Y + Height / 2);
}
