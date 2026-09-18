using System.Buffers.Binary;

namespace ClipboardManager.Core.Imaging;

/// <summary>解析后的位图：统一为<b>自上而下</b>、<b>BGRA32</b>、无行填充的像素缓冲。</summary>
/// <param name="Width">宽度（像素）。</param>
/// <param name="Height">高度（像素，恒为正）。</param>
/// <param name="BgraPixels">像素数据，长度 = Width × Height × 4。</param>
public sealed record DibImage(int Width, int Height, byte[] BgraPixels);

/// <summary>
/// CF_DIB / CF_DIBV5 解析器（需求 §3.2 图片类型，技术设计 §5.2）。
/// <para>
/// 输入来自任意第三方程序，一律视为不可信数据（AGENTS.md §6）：所有偏移、长度、位深、
/// 压缩方式都要校验，任何不合法都直接拒绝并给出原因，绝不越界读取。
/// </para>
/// </summary>
public static class DibParser
{
    /// <summary>BITMAPINFOHEADER 大小。</summary>
    public const int SizeBitmapInfoHeader = 40;

    /// <summary>BITMAPV4HEADER 大小。</summary>
    public const int SizeBitmapV4Header = 108;

    /// <summary>BITMAPV5HEADER 大小。</summary>
    public const int SizeBitmapV5Header = 124;

    /// <summary>像素缓冲上限（64MB），超过直接拒绝，避免畸形数据导致巨量分配。</summary>
    public const long MaxPixelBytes = 64L * 1024 * 1024;

    private const uint BiRgb = 0;
    private const uint BiBitFields = 3;
    private const uint AlphaMaskStandard = 0xFF000000;
    private const uint V5AlphaMaskOffset = 52;

    /// <summary>
    /// 解析 DIB 数据。
    /// </summary>
    /// <param name="data">剪贴板中 CF_DIB / CF_DIBV5 的原始字节。</param>
    /// <param name="image">解析结果（自上而下 BGRA32）。</param>
    /// <param name="error">失败原因。</param>
    public static bool TryParse(ReadOnlySpan<byte> data, out DibImage? image, out string? error)
    {
        image = null;
        error = null;

        if (data.Length < SizeBitmapInfoHeader)
        {
            error = "数据长度不足，无法容纳 BITMAPINFOHEADER";
            return false;
        }

        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (headerSize is not (SizeBitmapInfoHeader or SizeBitmapV4Header or SizeBitmapV5Header))
        {
            error = $"不支持的 DIB 头大小 {headerSize}";
            return false;
        }

        if (data.Length < headerSize)
        {
            error = "数据长度小于 DIB 头声明的大小";
            return false;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        var heightRaw = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

        if (width <= 0 || heightRaw == 0)
        {
            error = "位图宽高非法";
            return false;
        }

        if (planes != 1)
        {
            error = "位图 planes 非法";
            return false;
        }

        if (bitCount is not (24 or 32))
        {
            error = $"暂不支持 {bitCount} 位位图";
            return false;
        }

        var pixelOffset = headerSize;
        var hasAlpha = false;

        if (compression == BiBitFields)
        {
            // BITMAPINFOHEADER + BI_BITFIELDS：3 个掩码紧跟在头后面；
            // V4/V5 的掩码在头内部。只有标准 BGRA 掩码布局才处理，其它直接拒绝。
            if (headerSize == SizeBitmapInfoHeader)
            {
                if (data.Length < headerSize + 12)
                {
                    error = "BI_BITFIELDS 缺少掩码字段";
                    return false;
                }

                if (!HasStandardColorMasks(data.Slice(headerSize, 12)))
                {
                    error = "不支持的像素掩码布局";
                    return false;
                }

                pixelOffset += 12;
            }
            else if (!HasStandardColorMasks(data.Slice(40, 12)))
            {
                error = "不支持的像素掩码布局";
                return false;
            }

            if (headerSize >= SizeBitmapV5Header
                && BinaryPrimitives.ReadUInt32LittleEndian(data[(int)V5AlphaMaskOffset..]) == AlphaMaskStandard)
            {
                hasAlpha = true;
            }
        }
        else if (compression != BiRgb)
        {
            error = $"不支持的压缩方式 {compression}";
            return false;
        }

        var width64 = (long)width;
        var height64 = Math.Abs((long)heightRaw);
        var stride = (int)(((width64 * bitCount + 31) / 32) * 4);
        var pixelBytes = (long)stride * height64;

        if (pixelBytes <= 0 || pixelBytes > MaxPixelBytes)
        {
            error = $"位图像素量非法或过大（{pixelBytes} 字节）";
            return false;
        }

        if (pixelOffset + pixelBytes > data.Length)
        {
            error = $"像素数据被截断（需要 {pixelOffset + pixelBytes} 字节，实际 {data.Length} 字节）";
            return false;
        }

        var pixels = new byte[width64 * height64 * 4];
        Convert(data, pixels, width, height64, stride, bitCount, pixelOffset, heightRaw < 0, hasAlpha);

        // 防御：不少程序把 32 位图整张的 alpha 写成 0（整图透明），这里按不透明处理。
        if (hasAlpha && !HasAnyNonZeroAlpha(pixels))
        {
            SetAlphaOpaque(pixels);
        }

        image = new DibImage(width, (int)height64, pixels);
        return true;
    }

    private static bool HasStandardColorMasks(ReadOnlySpan<byte> masks) =>
        BinaryPrimitives.ReadUInt32LittleEndian(masks) == 0x00FF0000
        && BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]) == 0x0000FF00
        && BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]) == 0x000000FF;

    private static bool HasAnyNonZeroAlpha(byte[] bgra)
    {
        for (var index = 3; index < bgra.Length; index += 4)
        {
            if (bgra[index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void SetAlphaOpaque(byte[] bgra)
    {
        for (var index = 3; index < bgra.Length; index += 4)
        {
            bgra[index] = 255;
        }
    }

    /// <summary>把源像素转换为自上而下、BGRA32、无填充的缓冲。</summary>
    private static void Convert(
        ReadOnlySpan<byte> source,
        byte[] destination,
        int width,
        long height,
        int stride,
        int bitCount,
        int pixelOffset,
        bool topDown,
        bool hasAlpha)
    {
        var rowBytes = width * 4;

        for (var y = 0; y < height; y++)
        {
            // 自下而上的 DIB（高度为正）需要翻转行序。
            var sourceRow = topDown ? (int)y : (int)(height - 1 - y);
            var sourceSpan = source.Slice(pixelOffset + (sourceRow * stride), stride);
            var targetSpan = destination.AsSpan((int)y * rowBytes, rowBytes);

            if (bitCount == 32)
            {
                // 只拷有效像素（丢弃行填充），并按需覆盖 alpha。
                sourceSpan[..rowBytes].CopyTo(targetSpan);
                if (!hasAlpha)
                {
                    for (var index = 3; index < targetSpan.Length; index += 4)
                    {
                        targetSpan[index] = 255;
                    }
                }
            }
            else
            {
                // 24bpp：BGR → BGRA，alpha 固定 255。
                for (var x = 0; x < width; x++)
                {
                    var sourceIndex = x * 3;
                    var targetIndex = x * 4;
                    targetSpan[targetIndex] = sourceSpan[sourceIndex];
                    targetSpan[targetIndex + 1] = sourceSpan[sourceIndex + 1];
                    targetSpan[targetIndex + 2] = sourceSpan[sourceIndex + 2];
                    targetSpan[targetIndex + 3] = 255;
                }
            }
        }
    }
}
