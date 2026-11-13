using System.Runtime.InteropServices;
using DialogueAssistant.Core;

namespace DialogueAssistant.Windows;

/// <summary>通过独立的 Win32 GDI BitBlt 实现捕获，不调用仓库内的游戏捕获项目。</summary>
public sealed class WindowFrameCapture : IGameFrameSource
{
    private const uint Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;

    /// <summary>捕获目标窗口客户区；目标最小化、失效或尺寸无效时返回 null。</summary>
    public CaptureFrame? Capture(GameWindowDescriptor window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsWindow(window.Handle) || IsIconic(window.Handle)) return null;
        if (!GetClientRect(window.Handle, out var clientRect)) return null;

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return null;

        var sourceDc = GetDC(window.Handle);
        if (sourceDc == nint.Zero) return null;

        var memoryDc = CreateCompatibleDC(sourceDc);
        if (memoryDc == nint.Zero)
        {
            ReleaseDC(window.Handle, sourceDc);
            return null;
        }

        var bitmapInfo = new BitmapInfo
        {
            Colors = new uint[1],
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
                ImageSize = (uint)checked(width * height * 4)
            }
        };
        var bits = nint.Zero;
        var bitmap = CreateDIBSection(sourceDc, ref bitmapInfo, DibRgbColors, out bits, nint.Zero, 0);
        if (bitmap == nint.Zero || bits == nint.Zero)
        {
            if (bitmap != nint.Zero) DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(window.Handle, sourceDc);
            return null;
        }

        var previousObject = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, sourceDc, 0, 0, Srccopy)) return null;

            var pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new CaptureFrame(pixels, width, height, DateTimeOffset.UtcNow);
        }
        finally
        {
            SelectObject(memoryDc, previousObject);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(window.Handle, sourceDc);
        }
    }

    /// <summary>Win32 客户区矩形结构。</summary>
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

    /// <summary>GDI DIB 位图信息头结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        /// <summary>结构尺寸。</summary>
        public uint Size;
        /// <summary>位图宽度。</summary>
        public int Width;
        /// <summary>位图高度；负数代表自上而下存储。</summary>
        public int Height;
        /// <summary>颜色平面数。</summary>
        public ushort Planes;
        /// <summary>每像素位数。</summary>
        public ushort BitCount;
        /// <summary>压缩模式。</summary>
        public uint Compression;
        /// <summary>图像字节数。</summary>
        public uint ImageSize;
        /// <summary>水平分辨率。</summary>
        public int XPelsPerMeter;
        /// <summary>垂直分辨率。</summary>
        public int YPelsPerMeter;
        /// <summary>颜色表使用数量。</summary>
        public uint ColorsUsed;
        /// <summary>重要颜色数量。</summary>
        public uint ColorsImportant;
    }

    /// <summary>GDI DIB 位图信息结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        /// <summary>位图头。</summary>
        public BitmapInfoHeader Header;
        /// <summary>颜色表保留项。</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public uint[] Colors;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint handle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint handle, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateDIBSection(nint deviceContext, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rasterOperation);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);
}
