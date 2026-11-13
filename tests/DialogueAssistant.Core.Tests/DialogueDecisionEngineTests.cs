using DialogueAssistant.Core;

namespace DialogueAssistant.Core.Tests;

/// <summary>验证主线对话动作决策的选项策略、稳定帧和失败关闭行为。</summary>
public sealed class DialogueDecisionEngineTests
{
    /// <summary>确认普通对话在达到稳定帧数后生成空格快进动作。</summary>
    [Fact]
    public void Decide_DialogueWithQuickSkipEnabled_ReturnsPressSpace()
    {
        var engine = new DialogueDecisionEngine();
        var options = CreateOptions() with { QuickSkipEnabled = true };

        var action = engine.Decide(Recognition(DialogueUiState.Dialogue), options, DateTimeOffset.UtcNow);

        Assert.Equal(DialogueActionType.PressSpace, action.Type);
    }

    /// <summary>确认首选策略点击第一个识别选项的中心。</summary>
    [Fact]
    public void Decide_FirstSelection_ClicksFirstOptionCenter()
    {
        var engine = new DialogueDecisionEngine();
        var action = engine.Decide(Recognition(DialogueUiState.Options, Options()), CreateOptions(), DateTimeOffset.UtcNow);

        Assert.Equal(new DialogueAction(DialogueActionType.Click, 30, 40), action);
    }

    /// <summary>确认末选策略点击最后一个识别选项的中心。</summary>
    [Fact]
    public void Decide_LastSelection_ClicksLastOptionCenter()
    {
        var engine = new DialogueDecisionEngine();
        var options = CreateOptions() with { OptionSelection = DialogueOptionSelection.Last };
        var action = engine.Decide(Recognition(DialogueUiState.Options, Options()), options, DateTimeOffset.UtcNow);

        Assert.Equal(new DialogueAction(DialogueActionType.Click, 130, 140), action);
    }

    /// <summary>确认“不选择”策略不会生成点击动作。</summary>
    [Fact]
    public void Decide_NoneSelection_ReturnsNoAction()
    {
        var engine = new DialogueDecisionEngine();
        var options = CreateOptions() with { OptionSelection = DialogueOptionSelection.None };
        var action = engine.Decide(Recognition(DialogueUiState.Options, Options()), options, DateTimeOffset.UtcNow);

        Assert.Equal(DialogueActionType.None, action.Type);
    }

    /// <summary>确认要求多帧稳定时，首帧不会触发输入，第二帧才允许动作。</summary>
    [Fact]
    public void Decide_RequiresStableFrames_BeforeReturningAction()
    {
        var engine = new DialogueDecisionEngine();
        var options = CreateOptions() with { RequiredStableFrames = 2 };
        var recognition = Recognition(DialogueUiState.Dialogue);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(DialogueActionType.None, engine.Decide(recognition, options, now).Type);
        Assert.Equal(DialogueActionType.PressSpace, engine.Decide(recognition, options, now.AddMilliseconds(100)).Type);
    }

    /// <summary>确认置信度不足的识别结果失败关闭，不产生输入。</summary>
    [Fact]
    public void Decide_LowConfidence_ReturnsNoAction()
    {
        var engine = new DialogueDecisionEngine();
        var recognition = Recognition(DialogueUiState.Dialogue) with { Confidence = 0.5 };

        var action = engine.Decide(recognition, CreateOptions(), DateTimeOffset.UtcNow);

        Assert.Equal(DialogueActionType.None, action.Type);
    }

    /// <summary>创建使用单帧确认且无动作冷却的测试选项。</summary>
    private static DialogueAssistantOptions CreateOptions() => new()
    {
        MinimumActionInterval = TimeSpan.Zero,
        RequiredStableFrames = 1
    };

    /// <summary>创建指定状态和候选选项的高置信度识别结果。</summary>
    private static DialogueRecognitionResult Recognition(
        DialogueUiState state,
        IReadOnlyList<DialogueOptionBounds>? options = null) => new(state, 0.99, options ?? Array.Empty<DialogueOptionBounds>());

    /// <summary>创建用于校验首项和末项中心坐标的两个选项。</summary>
    private static IReadOnlyList<DialogueOptionBounds> Options() =>
    [
        new DialogueOptionBounds(10, 20, 40, 40, "first"),
        new DialogueOptionBounds(110, 120, 40, 40, "last")
    ];
}
