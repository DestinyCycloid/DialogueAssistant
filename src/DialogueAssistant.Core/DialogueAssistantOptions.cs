namespace DialogueAssistant.Core;

/// <summary>与 UI 框架无关的自动剧情运行选项。</summary>
public sealed record DialogueAssistantOptions
{
    /// <summary>是否自动快进无选项的对话。</summary>
    public bool QuickSkipEnabled { get; init; } = true;

    /// <summary>当前对话出现选项时采用的选择策略。</summary>
    public DialogueOptionSelection OptionSelection { get; init; } = DialogueOptionSelection.First;

    /// <summary>是否启用自动剧情处理；宿主仍须用户明确启动运行循环。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>两次自动输入之间的最小间隔。</summary>
    public TimeSpan MinimumActionInterval { get; init; } = TimeSpan.FromMilliseconds(350);

    /// <summary>选择选项前的固定等待时间。</summary>
    public TimeSpan AfterChooseOptionDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>是否启用自定义优先选项关键词。</summary>
    public bool CustomPriorityOptionsEnabled { get; init; }

    /// <summary>自定义优先选项关键词；支持按行或分号分隔。</summary>
    public string CustomPriorityOptions { get; init; } = string.Empty;

    /// <summary>内置优先选择关键词；优先于默认选项策略。</summary>
    public IReadOnlyList<string> BuiltInSelectOptions { get; init; } = Array.Empty<string>();

    /// <summary>用户自定义暂停选择关键词。</summary>
    public IReadOnlyList<string> PauseOptions { get; init; } = Array.Empty<string>();

    /// <summary>随识别资源提供的默认暂停选择关键词。</summary>
    public IReadOnlyList<string> DefaultPauseOptions { get; init; } = Array.Empty<string>();

    /// <summary>是否跳过旧程序内置的优先/暂停关键词规则。</summary>
    public bool SkipBuiltInClickOptions { get; init; }

    /// <summary>执行动作前必须连续识别到相同 UI 状态的帧数。</summary>
    public int RequiredStableFrames { get; init; } = 2;

    /// <summary>低于该置信度的识别结果不允许触发动作。</summary>
    public double MinimumConfidence { get; init; } = 0.85;

    /// <summary>超过该时长的捕获帧视为过期，不允许据此产生动作。</summary>
    public TimeSpan MaximumFrameAge { get; init; } = TimeSpan.FromMilliseconds(500);
}

/// <summary>自动选择对话选项的策略。</summary>
public enum DialogueOptionSelection
{
    /// <summary>选择第一个选项。</summary>
    First = 0,
    /// <summary>选择最后一个选项；保留旧设置文件中的数值 1。</summary>
    Last = 1,
    /// <summary>保留选项，不执行点击；保留旧设置文件中的数值 2。</summary>
    None = 2,
    /// <summary>从识别到的选项中随机选择。</summary>
    Random = 3
}
