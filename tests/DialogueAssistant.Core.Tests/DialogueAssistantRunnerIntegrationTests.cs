using DialogueAssistant.Core;

namespace DialogueAssistant.Core.Tests;

/// <summary>验证捕获、识别决策、输入委托与取消信号组成的 Runner 运行链路。</summary>
public sealed class DialogueAssistantRunnerIntegrationTests
{
    /// <summary>验证 Runner 捕获画面、识别普通对话并将快进动作委托给输入端。</summary>
    [Fact]
    public async Task RunAsync_DialogueFrame_CapturesRecognizesAndSendsQuickSkip()
    {
        var window = new GameWindowDescriptor((nint)123, 456, "test", "game");
        var frameSource = new FakeFrameSource(CreateFrame());
        var input = new FakeGameInput();
        var recognizer = new FakeDialogueRecognizer(new DialogueRecognitionResult(
            DialogueUiState.Dialogue, 0.99, Array.Empty<DialogueOptionBounds>()));
        var runner = CreateRunner(frameSource, input, recognizer);
        using var cancellation = new CancellationTokenSource();
        runner.FrameProcessed += _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(window,
            () => CreateOptions(), cancellation.Token));

        Assert.Equal(1, frameSource.CaptureCount);
        Assert.Equal(1, recognizer.RecognitionCount);
        var sentAction = Assert.Single(input.Actions);
        Assert.Equal(DialogueActionType.PressSpace, sentAction.Type);
        Assert.Same(window, input.LastWindow);
    }

    /// <summary>验证选项 OCR 结果经决策后将首项中心坐标交给输入端。</summary>
    [Fact]
    public async Task RunAsync_OptionsFrame_SendsClickAtRecognizedOptionCenter()
    {
        var frameSource = new FakeFrameSource(CreateFrame());
        var input = new FakeGameInput();
        var recognizer = new FakeDialogueRecognizer(new DialogueRecognitionResult(
            DialogueUiState.Options, 0.99, [new DialogueOptionBounds(20, 30, 40, 20, "继续") ]));
        var runner = CreateRunner(frameSource, input, recognizer);
        using var cancellation = new CancellationTokenSource();
        runner.FrameProcessed += _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new GameWindowDescriptor((nint)123, 456, "test", "game"),
            () => CreateOptions() with { AfterChooseOptionDelay = TimeSpan.Zero }, cancellation.Token));

        Assert.Equal(1, frameSource.CaptureCount);
        Assert.Equal(1, recognizer.RecognitionCount);
        Assert.Equal(new DialogueAction(DialogueActionType.Click, 40, 40), Assert.Single(input.Actions));
    }

    /// <summary>验证启动前已取消时不会捕获画面、执行识别或发送输入。</summary>
    [Fact]
    public async Task RunAsync_AlreadyCanceled_DoesNotCaptureOrSendInput()
    {
        var frameSource = new FakeFrameSource(CreateFrame());
        var input = new FakeGameInput();
        var recognizer = new FakeDialogueRecognizer(new DialogueRecognitionResult(
            DialogueUiState.Dialogue, 0.99, Array.Empty<DialogueOptionBounds>()));
        var runner = CreateRunner(frameSource, input, recognizer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            new GameWindowDescriptor((nint)123, 456, "test", "game"), () => CreateOptions(), cancellation.Token));

        Assert.Equal(0, frameSource.CaptureCount);
        Assert.Equal(0, recognizer.RecognitionCount);
        Assert.Empty(input.Actions);
    }

    /// <summary>验证选项点击延迟期间收到取消后，不会调用输入端执行点击。</summary>
    [Fact]
    public async Task RunAsync_CanceledDuringOptionDelay_DoesNotSendClick()
    {
        var frameSource = new FakeFrameSource(CreateFrame());
        var input = new FakeGameInput();
        var recognizer = new FakeDialogueRecognizer(new DialogueRecognitionResult(
            DialogueUiState.Options, 0.99, [new DialogueOptionBounds(20, 30, 40, 20, "继续") ]));
        var runner = CreateRunner(frameSource, input, recognizer);
        using var cancellation = new CancellationTokenSource();

        var runTask = runner.RunAsync(new GameWindowDescriptor((nint)123, 456, "test", "game"),
            () => CreateOptions() with { AfterChooseOptionDelay = TimeSpan.FromSeconds(5) }, cancellation.Token);
        await WaitForRecognitionAsync(recognizer);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

        Assert.Equal(1, frameSource.CaptureCount);
        Assert.Empty(input.Actions);
    }

    /// <summary>创建使用 30 毫秒最小帧间隔的 Runner，避免测试产生忙轮询。</summary>
    private static DialogueAssistantRunner CreateRunner(FakeFrameSource frameSource, FakeGameInput input,
        FakeDialogueRecognizer recognizer) => new(frameSource, input, new DialogueAssistantEngine(recognizer),
            TimeSpan.FromMilliseconds(30));

    /// <summary>创建一帧尺寸合法且刚捕获的测试画面。</summary>
    private static CaptureFrame CreateFrame() => new(new byte[100 * 100 * 4], 100, 100, DateTimeOffset.UtcNow);

    /// <summary>创建启用单帧确认、无动作冷却的测试选项。</summary>
    private static DialogueAssistantOptions CreateOptions() => new()
    {
        RequiredStableFrames = 1,
        MinimumActionInterval = TimeSpan.Zero,
        MaximumFrameAge = TimeSpan.FromSeconds(5)
    };

    /// <summary>等待识别替身被调用，确保取消发生在选项点击延迟阶段。</summary>
    private static async Task WaitForRecognitionAsync(FakeDialogueRecognizer recognizer)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (recognizer.RecognitionCount == 0 && DateTimeOffset.UtcNow < timeoutAt)
            await Task.Delay(10);
        Assert.Equal(1, recognizer.RecognitionCount);
    }

    /// <summary>为 Runner 提供固定捕获帧并统计调用次数。</summary>
    private sealed class FakeFrameSource(CaptureFrame frame) : IGameFrameSource
    {
        /// <summary>捕获方法被调用的次数。</summary>
        public int CaptureCount { get; private set; }

        /// <summary>返回预设帧并增加捕获计数。</summary>
        public CaptureFrame? Capture(GameWindowDescriptor window)
        {
            CaptureCount++;
            return frame;
        }
    }

    /// <summary>返回固定识别结果并统计 OCR/识别管线调用次数。</summary>
    private sealed class FakeDialogueRecognizer(DialogueRecognitionResult result) : IDialogueRecognizer
    {
        /// <summary>识别方法被调用的次数。</summary>
        public int RecognitionCount { get; private set; }

        /// <summary>返回预设识别结果。</summary>
        public DialogueRecognitionResult Recognize(CaptureFrame frame, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecognitionCount++;
            return result;
        }
    }

    /// <summary>记录 Runner 委托执行的动作与目标窗口。</summary>
    private sealed class FakeGameInput : IGameInput
    {
        /// <summary>收到的输入动作列表。</summary>
        public List<DialogueAction> Actions { get; } = [];

        /// <summary>最近一次输入对应的目标窗口。</summary>
        public GameWindowDescriptor? LastWindow { get; private set; }

        /// <summary>记录动作并模拟宿主接受输入。</summary>
        public bool Execute(DialogueAction action, GameWindowDescriptor window, CaptureFrame frame,
            DateTimeOffset now, TimeSpan maximumFrameAge, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add(action);
            LastWindow = window;
            return true;
        }
    }
}
