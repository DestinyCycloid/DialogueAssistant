namespace DialogueAssistant.Core;

/// <summary>编排帧识别和动作决策，不直接访问 UI、窗口、文件或输入设备。</summary>
public sealed class DialogueAssistantEngine
{
    /// <summary>无法可靠识别或未执行时返回的失败关闭结果。</summary>
    private static readonly DialogueRecognitionResult UnknownResult =
        new(DialogueUiState.Unknown, 0, Array.Empty<DialogueOptionBounds>());

    /// <summary>由宿主注入的剧情识别器。</summary>
    private readonly IDialogueRecognizer _recognizer;

    /// <summary>负责稳定帧、置信度、策略和动作节流的纯逻辑决策器。</summary>
    private readonly DialogueDecisionEngine _decisionEngine;

    /// <summary>创建宿主无关的剧情处理管线。</summary>
    public DialogueAssistantEngine(IDialogueRecognizer recognizer, DialogueDecisionEngine? decisionEngine = null)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        _recognizer = recognizer;
        _decisionEngine = decisionEngine ?? new DialogueDecisionEngine();
    }

    /// <summary>识别一帧并返回建议动作；未启用、过期、异常取消或配置无效时不产生输入动作。</summary>
    public DialogueFrameOutcome ProcessFrame(
        CaptureFrame frame,
        DialogueAssistantOptions options,
        bool isEnabled,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // 验证易导致不安全动作的设置，非法值采用无动作结果而不是修正为猜测值。
        if (options.MinimumActionInterval < TimeSpan.Zero || options.MaximumFrameAge < TimeSpan.Zero
            || options.AfterChooseOptionDelay < TimeSpan.Zero || options.AfterChooseOptionDelay > TimeSpan.FromSeconds(10)
            || options.RequiredStableFrames < 1 || !Enum.IsDefined(options.OptionSelection)
            || !double.IsFinite(options.MinimumConfidence) || options.MinimumConfidence is < 0 or > 1)
        {
            _decisionEngine.ResetStability();
            return new DialogueFrameOutcome(UnknownResult, DialogueAction.None, "配置无效，已安全停止本帧处理。");
        }

        if (!isEnabled || !options.Enabled)
        {
            _decisionEngine.ResetStability();
            return new DialogueFrameOutcome(UnknownResult, DialogueAction.None, "自动剧情已暂停。");
        }

        var frameAge = now - frame.CapturedAt;
        if (frameAge < TimeSpan.Zero || frameAge > options.MaximumFrameAge)
        {
            _decisionEngine.ResetStability();
            return new DialogueFrameOutcome(UnknownResult, DialogueAction.None, "捕获帧时间无效或已过期，未执行动作。");
        }

        // 识别失败或未完成时只返回 Unknown；取消则交还宿主处理，不吞掉取消信号。
        var recognition = _recognizer.Recognize(frame, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidRecognition(recognition, frame))
        {
            _decisionEngine.ResetStability();
            recognition = UnknownResult;
        }
        var action = _decisionEngine.Decide(recognition, options, now);
        var status = action.Type == DialogueActionType.None
            ? "当前帧无可执行动作。"
            : $"已生成候选动作：{action.Type}。";
        return new DialogueFrameOutcome(recognition, action, status);
    }

    /// <summary>拒绝识别器返回的非法状态、置信度或越界选项坐标。</summary>
    private static bool IsValidRecognition(DialogueRecognitionResult? recognition, CaptureFrame frame)
    {
        if (recognition is null || !Enum.IsDefined(recognition.State)
            || !double.IsFinite(recognition.Confidence) || recognition.Confidence is < 0 or > 1
            || recognition.Options is null)
            return false;

        foreach (var option in recognition.Options)
        {
            if (option.X < 0 || option.Y < 0 || option.Width <= 0 || option.Height <= 0
                || (long)option.X + option.Width > frame.Width || (long)option.Y + option.Height > frame.Height)
                return false;
        }

        return true;
    }
}
