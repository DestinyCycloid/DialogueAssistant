namespace DialogueAssistant.Core;

/// <summary>由自动剧情决策引擎提出的一次输入动作。</summary>
public readonly record struct DialogueAction(DialogueActionType Type, int X = 0, int Y = 0)
{
    /// <summary>返回不执行输入的动作。</summary>
    public static DialogueAction None => new(DialogueActionType.None);
}

/// <summary>当前 MVP 支持的输入动作类型。</summary>
public enum DialogueActionType
{
    /// <summary>不执行任何输入。</summary>
    None,
    /// <summary>轻按空格键快进对话。</summary>
    PressSpace,
    /// <summary>点击捕获帧像素坐标上的选项中心。</summary>
    Click
}
