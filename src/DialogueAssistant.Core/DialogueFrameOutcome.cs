namespace DialogueAssistant.Core;

/// <summary>描述核心对一帧的识别结果、候选动作及未执行原因。</summary>
/// <param name="Recognition">本帧的 UI 识别结果。</param>
/// <param name="Action">核心建议宿主执行的动作；无动作时为 None。</param>
/// <param name="Status">可显示给用户的简短处理状态。</param>
public sealed record DialogueFrameOutcome(
    DialogueRecognitionResult Recognition,
    DialogueAction Action,
    string Status);
