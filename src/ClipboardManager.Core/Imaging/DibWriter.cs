using System.Buffers.Binary;

namespace ClipboardManager.Core.Imaging;

/// <summary>
/// 把 BGRA32 像素打包成 <c>CF_DIBV5</c> 字节（用于把历史里的图片粘贴回剪贴板）。
/// <para>
/// 结构：BITMAPV5HEADER(124 字节) + 像素数据；像素<b>自下而上</b>（bV5Height 为正）。
/// 掩码固定标准 BGRA 布局并带 alpha 掩码，主流程序（画图、Office、聊天工具）都能识别。
/// </para>
/// </summary>
public static class DibWriter
{
    private const uint BiBitFields = 3;
    private const uint LcsSrgb = 0x73524742; // "sRGB"

    /// <summary>
    /// 构造 CF_DIBV5 数据。
    /// </summary>
    /// <param name="width">宽度（像素），必须为正。</param>
    /// <param name="height">高度（像素），必须为正。</param>
    /// <param name="bgraTopDown">自上而下、无行填充的 BGRA32 像素。</param>
    public static byte[] BuildDibV5(int width, int height, ReadOnlySpan<byte> bgraTopDown)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "宽高必须为正");
        }

        var stride = (long)width * 4;
        var pixelBytes = stride * height;
        if (bgraTopDown.Length < pixelBytes)
        {
            throw new ArgumentException("像素数据长度不足", nameof(bgraTopDown));
        }

        var buffer = new byte[DibParser.SizeBitmapV5Header + pixelBytes];
        var header = buffer.AsSpan(0, DibParser.SizeBitmapV5Header);

        BinaryPrimitives.WriteUInt32LittleEndian(header, DibParser.SizeBitmapV5Header);            // bV5Size
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);                              // bV5Width
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height);                             // bV5Height > 0 → 自下而上
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);                                // bV5Planes
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32);                               // bV5BitCount
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], BiBitFields);                      // bV5Compression
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], (uint)pixelBytes);                 // bV5SizeImage
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 2835);                              // 72 DPI
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], 2835);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 0x00FF0000);                       // red mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 0x0000FF00);                       // green mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], 0x000000FF);                       // blue mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[52..], 0xFF000000);                       // alpha mask
        BinaryPrimitives.WriteUInt32LittleEndian(header[56..], LcsSrgb);                          // bV5CSType

        for (var y = 0; y < height; y++)
        {
            var sourceRow = (int)(height - 1 - y);
            bgraTopDown.Slice(sourceRow * (int)stride, (int)stride)
                .CopyTo(buffer.AsSpan(DibParser.SizeBitmapV5Header + (y * (int)stride)));
        }

        return buffer;
    }
}
