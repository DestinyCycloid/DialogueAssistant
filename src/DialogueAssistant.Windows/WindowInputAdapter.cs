using System.Runtime.InteropServices;
using DialogueAssistant.Core;

namespace DialogueAssistant.Windows;

/// <summary>将自动剧情动作安全地发送给已绑定且位于前台的目标窗口。</summary>
public sealed class WindowInputAdapter : IGameInput
{
    /// <summary>SendInput 键盘输入类型标识。</summary>
    private const uint InputKeyboard = 1;
    /// <summary>SendInput 鼠标输入类型标识。</summary>
    private const uint InputMouse = 0;
    /// <summary>键盘按键释放标记。</summary>
    private const uint KeyUp = 0x0002;
    /// <summary>鼠标左键按下标记。</summary>
    private const uint MouseLeftDown = 0x0002;
    /// <summary>鼠标左键释放标记。</summary>
    private const uint MouseLeftUp = 0x0004;
    /// <summary>鼠标移动标记。</summary>
    private const uint MouseMove = 0x0001;
    /// <summary>鼠标使用绝对坐标标记。</summary>
    private const uint MouseAbsolute = 0x8000;
    /// <summary>绝对坐标使用整个虚拟桌面标记。</summary>
    private const uint MouseVirtualDesk = 0x4000;
    /// <summary>键鼠按下与释放之间保留至少一个稳定输入间隔。</summary>
    private const int MinimumInputPulseMilliseconds = 50;

    /// <summary>执行核心候选动作，并校验帧时效、帧尺寸、窗口状态和取消信号。</summary>
    public bool Execute(DialogueAction action, GameWindowDescriptor window, CaptureFrame frame, DateTimeOffset now,
        TimeSpan maximumFrameAge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (action.Type == DialogueActionType.None) return true;
        if (maximumFrameAge < TimeSpan.Zero) return false;

        var frameAge = now - frame.CapturedAt;
        if (frameAge < TimeSpan.Zero || frameAge > maximumFrameAge || !GetClientRect(window.Handle, out var rect)
            || rect.Right - rect.Left != frame.Width || rect.Bottom - rect.Top != frame.Height)
            return false;

        cancellationToken.ThrowIfCancellationRequested();
        return action.Type switch
        {
            DialogueActionType.PressSpace => PressSpace(window),
            DialogueActionType.Click => ClickClientPoint(window, action.X, action.Y, cancellationToken),
            _ => false
        };
    }

    /// <summary>执行空格键快进；目标无效或不在前台时不发送输入。</summary>
    public bool PressSpace(GameWindowDescriptor window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsTargetForeground(window.Handle)) return false;

        var keyDown = new[]
        {
            new NativeInput { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x20 } } }
        };
        if (SendInput(1, keyDown, Marshal.SizeOf<NativeInput>()) != 1) return false;

        var keyUp = new[]
        {
            new NativeInput { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = 0x20, Flags = KeyUp } } }
        };
        var released = false;
        try
        {
            Thread.Sleep(MinimumInputPulseMilliseconds);
            released = SendInput(1, keyUp, Marshal.SizeOf<NativeInput>()) == 1;
            return released;
        }
        finally
        {
            // 尝试释放按键，避免部分 SendInput 失败后留下持续按下状态。
            if (!released) SendInput(1, keyUp, Marshal.SizeOf<NativeInput>());
        }
    }

    /// <summary>点击捕获帧像素坐标；窗口不在前台或坐标越界时失败关闭。</summary>
    public bool ClickClientPoint(GameWindowDescriptor window, int x, int y, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsTargetForeground(window.Handle) || !GetClientRect(window.Handle, out var clientRect)) return false;
        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (x < 0 || y < 0 || x >= width || y >= height) return false;

        var point = new NativePoint { X = x, Y = y };
        if (!ClientToScreen(window.Handle, ref point) || !IsTargetForeground(window.Handle)) return false;

        var virtualLeft = GetSystemMetrics(76);
        var virtualTop = GetSystemMetrics(77);
        var virtualWidth = GetSystemMetrics(78);
        var virtualHeight = GetSystemMetrics(79);
        if (virtualWidth <= 1 || virtualHeight <= 1) return false;
        var absoluteX = (int)Math.Round((point.X - virtualLeft) * 65535d / (virtualWidth - 1));
        var absoluteY = (int)Math.Round((point.Y - virtualTop) * 65535d / (virtualHeight - 1));

        var size = Marshal.SizeOf<NativeInput>();
        var move = new[]
        {
            new NativeInput { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { X = absoluteX, Y = absoluteY, Flags = MouseMove | MouseAbsolute | MouseVirtualDesk } } }
        };
        if (SendInput(1, move, size) != 1 || !IsTargetForeground(window.Handle)) return false;
        cancellationToken.ThrowIfCancellationRequested();

        var down = new[]
        {
            new NativeInput { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = MouseLeftDown } } }
        };
        if (SendInput(1, down, size) != 1) return false;

        var up = new[]
        {
            new NativeInput { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInput { Flags = MouseLeftUp } } }
        };
        var released = false;
        try
        {
            Thread.Sleep(MinimumInputPulseMilliseconds);
            released = SendInput(1, up, size) == 1;
            return released;
        }
        finally
        {
            // 即使点击释放事件失败也再次尝试松开鼠标键。
            if (!released) SendInput(1, up, size);
        }
    }

    /// <summary>确认窗口仍有效且就是当前前台窗口。</summary>
    private static bool IsTargetForeground(nint handle) => IsWindow(handle) && GetForegroundWindow() == handle;

    /// <summary>Win32 矩形结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        /// <summary>矩形左边缘。</summary>
        public int Left;
        /// <summary>矩形上边缘。</summary>
        public int Top;
        /// <summary>矩形右边缘。</summary>
        public int Right;
        /// <summary>矩形下边缘。</summary>
        public int Bottom;
    }

    /// <summary>Win32 屏幕坐标结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        /// <summary>横坐标。</summary>
        public int X;
        /// <summary>纵坐标。</summary>
        public int Y;
    }

    /// <summary>SendInput 键盘数据结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        /// <summary>虚拟键码。</summary>
        public ushort VirtualKey;
        /// <summary>硬件扫描码。</summary>
        public ushort ScanCode;
        /// <summary>按键事件标志。</summary>
        public uint Flags;
        /// <summary>事件时间戳。</summary>
        public uint Time;
        /// <summary>附加应用定义信息。</summary>
        public nint ExtraInfo;
    }

    /// <summary>SendInput 鼠标数据结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        /// <summary>屏幕或绝对坐标横坐标。</summary>
        public int X;
        /// <summary>屏幕或绝对坐标纵坐标。</summary>
        public int Y;
        /// <summary>滚轮或扩展按键数据。</summary>
        public uint MouseData;
        /// <summary>鼠标事件标志。</summary>
        public uint Flags;
        /// <summary>事件时间戳。</summary>
        public uint Time;
        /// <summary>附加应用定义信息。</summary>
        public nint ExtraInfo;
    }

    /// <summary>SendInput 联合数据结构。</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        /// <summary>键盘输入数据。</summary>
        [FieldOffset(0)] public KeyboardInput Keyboard;
        /// <summary>鼠标输入数据。</summary>
        [FieldOffset(0)] public MouseInput Mouse;
    }

    /// <summary>SendInput 输入结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        /// <summary>输入设备类型标识。</summary>
        public uint Type;
        /// <summary>与类型对应的键盘或鼠标输入数据。</summary>
        public InputUnion Data;
    }

    /// <summary>检查指定窗口句柄是否仍有效。</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint handle);
    /// <summary>获取当前前台窗口句柄。</summary>
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    /// <summary>获取指定窗口的客户区矩形。</summary>
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint handle, out NativeRect rect);
    /// <summary>将客户区坐标转换为屏幕坐标。</summary>
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint handle, ref NativePoint point);
    /// <summary>读取指定虚拟桌面尺寸或偏移量。</summary>
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    /// <summary>将受控键鼠事件发送至当前交互桌面。</summary>
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);
}
