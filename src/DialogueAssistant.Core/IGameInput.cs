namespace DialogueAssistant.Core;

/// <summary>由平台宿主实现的受限自动剧情输入接口。</summary>
public interface IGameInput
{
    /// <summary>对指定窗口执行候选动作；拒绝目标无效、帧过期、坐标失效或取消的输入。</summary>
    bool Execute(DialogueAction action, GameWindowDescriptor window, CaptureFrame frame,
        DateTimeOffset now, TimeSpan maximumFrameAge, CancellationToken cancellationToken = default);
}
