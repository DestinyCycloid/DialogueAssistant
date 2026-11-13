using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DialogueAssistant.Core;

namespace DialogueAssistant.Windows;

/// <summary>使用 Win32 API 枚举可见窗口，独立于 BetterGI 的窗口管理代码。</summary>
public static class WindowCatalog
{
    /// <summary>获取当前可见且有标题的顶层窗口列表。</summary>
    public static IReadOnlyList<GameWindowDescriptor> GetVisibleWindows()
    {
        var windows = new List<GameWindowDescriptor>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;

            var title = new StringBuilder(512);
            if (GetWindowText(handle, title, title.Capacity) <= 0) return true;

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0) return true;

            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new GameWindowDescriptor(handle, (int)processId, title.ToString(), process.ProcessName));
            }
            catch (ArgumentException)
            {
                // 窗口所属进程可能在枚举期间退出，忽略这一瞬时失效窗口。
            }

            return true;
        }, nint.Zero);

        return windows;
    }

    /// <summary>枚举顶层窗口回调。</summary>
    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint handle, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
}
