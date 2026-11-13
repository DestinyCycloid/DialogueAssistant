namespace DialogueAssistant.Core;

/// <summary>由平台宿主实现的目标窗口帧来源。</summary>
public interface IGameFrameSource
{
    /// <summary>获取目标窗口当前客户区画面；窗口不可用时返回 null。</summary>
    CaptureFrame? Capture(GameWindowDescriptor window);
}
