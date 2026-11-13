namespace DialogueAssistant.Core;

/// <summary>描述可供独立程序选择的顶层窗口。</summary>
public sealed record GameWindowDescriptor
{
    /// <summary>创建目标窗口描述。</summary>
    public GameWindowDescriptor(nint handle, int processId, string title, string processName)
    {
        Handle = handle;
        ProcessId = processId;
        Title = title;
        ProcessName = processName;
    }

    /// <summary>目标窗口句柄。</summary>
    public nint Handle { get; }

    /// <summary>窗口所属进程 ID。</summary>
    public int ProcessId { get; }

    /// <summary>窗口标题。</summary>
    public string Title { get; }

    /// <summary>窗口所属进程名。</summary>
    public string ProcessName { get; }

    /// <summary>返回便于窗口选择器展示的名称。</summary>
    public override string ToString() => $"{Title} ({ProcessName}, PID {ProcessId})";
}
