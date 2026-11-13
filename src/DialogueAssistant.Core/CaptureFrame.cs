namespace DialogueAssistant.Core;

/// <summary>一次捕获得到的 BGRA32 游戏客户区帧。</summary>
public sealed class CaptureFrame
{
    /// <summary>创建帧快照；像素按从上到下、每像素 BGRA 四字节排列。</summary>
    public CaptureFrame(byte[] pixels, int width, int height, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("BGRA32 像素长度与帧尺寸不一致。", nameof(pixels));

        Pixels = pixels;
        Width = width;
        Height = height;
        CapturedAt = capturedAt;
    }

    /// <summary>帧像素数据，格式为 BGRA32。</summary>
    public byte[] Pixels { get; }

    /// <summary>帧宽度，单位为像素。</summary>
    public int Width { get; }

    /// <summary>帧高度，单位为像素。</summary>
    public int Height { get; }

    /// <summary>帧捕获时间。</summary>
    public DateTimeOffset CapturedAt { get; }
}
