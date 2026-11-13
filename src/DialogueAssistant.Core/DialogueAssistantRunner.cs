namespace DialogueAssistant.Core;

/// <summary>循环获取游戏帧、运行剧情核心并委托宿主执行输入动作。</summary>
public sealed class DialogueAssistantRunner
{
    private readonly IGameFrameSource _frameSource;
    private readonly IGameInput _gameInput;
    private readonly DialogueAssistantEngine _engine;
    private readonly TimeSpan _frameInterval;

    /// <summary>创建一次运行会话；帧间隔至少保留 30 ms，避免忙轮询。</summary>
    public DialogueAssistantRunner(IGameFrameSource frameSource, IGameInput gameInput,
        DialogueAssistantEngine engine, TimeSpan? frameInterval = null)
    {
        ArgumentNullException.ThrowIfNull(frameSource);
        ArgumentNullException.ThrowIfNull(gameInput);
        ArgumentNullException.ThrowIfNull(engine);
        _frameSource = frameSource;
        _gameInput = gameInput;
        _engine = engine;
        _frameInterval = frameInterval ?? TimeSpan.FromMilliseconds(100);
        if (_frameInterval < TimeSpan.FromMilliseconds(30))
            throw new ArgumentOutOfRangeException(nameof(frameInterval), "自动处理帧间隔不能小于 30 毫秒。");
    }

    /// <summary>每次完成一帧处理后发出状态结果，供 UI 或日志订阅。</summary>
    public event Action<DialogueFrameOutcome>? FrameProcessed;

    /// <summary>在给定目标窗口上持续处理，直到取消、窗口失效或捕获中断。</summary>
    public async Task RunAsync(GameWindowDescriptor window, Func<DialogueAssistantOptions> optionsProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(optionsProvider);

        while (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = _frameSource.Capture(window);
            if (frame is null)
                throw new InvalidOperationException("目标窗口不可用或捕获中断；自动处理已停止。");

            var now = DateTimeOffset.UtcNow;
            var options = optionsProvider();
            var outcome = _engine.ProcessFrame(frame, options, isEnabled: true, now, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (outcome.Action.Type != DialogueActionType.None)
            {
                // 仅普通选项点击采用配置的固定延迟；取消信号可中断等待，不影响空格快进。
                if (outcome.Action.Type == DialogueActionType.Click && options.AfterChooseOptionDelay > TimeSpan.Zero)
                    await Task.Delay(options.AfterChooseOptionDelay, cancellationToken).ConfigureAwait(false);

                // 输入前再次校验取消信号；适配器仍校验窗口尺寸、帧时效及前台状态。
                cancellationToken.ThrowIfCancellationRequested();
                var permittedFrameAge = options.MaximumFrameAge +
                    (outcome.Action.Type == DialogueActionType.Click ? options.AfterChooseOptionDelay : TimeSpan.Zero);
                var executed = _gameInput.Execute(outcome.Action, window, frame, DateTimeOffset.UtcNow,
                    permittedFrameAge, cancellationToken);
                if (!executed)
                    outcome = outcome with { Action = DialogueAction.None, Status = "输入前置条件不满足；未执行动作。" };
            }

            FrameProcessed?.Invoke(outcome);
            await Task.Delay(_frameInterval, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
