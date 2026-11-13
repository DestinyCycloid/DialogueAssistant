namespace DialogueAssistant.Core;

/// <summary>将游戏帧分类为自动剧情所需 UI 状态的接口。</summary>
public interface IDialogueRecognizer
{
    /// <summary>从一帧图像识别对话 UI，无法可靠识别时必须返回 Unknown。</summary>
    DialogueRecognitionResult Recognize(CaptureFrame frame, CancellationToken cancellationToken = default);
}
