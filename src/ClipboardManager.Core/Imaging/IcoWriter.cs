using System.Buffers.Binary;

namespace ClipboardManager.Core.Imaging;

/// <summary>
/// 构造 <c>.ico</c> 字节（用于运行时生成托盘图标，避免在仓库里塞二进制资源）。
/// <para>
/// 采用 Vista+ 支持的「PNG 压缩图标」形式：ICONDIR + 若干 ICONDIRENTRY + 各尺寸的 PNG 数据。
/// 布局是纯字节运算，可单测（AGENTS.md §2）。
/// </para>
/// </summary>
public static class IcoWriter
{
    /// <summary>ICONDIR 固定 6 字节，每个 ICONDIRENTRY 固定 16 字节。</summary>
    public const int DirectorySize = 6;

    /// <summary>单个目录项大小。</summary>
    public const int EntrySize = 16;

    /// <summary>单个尺寸条目。</summary>
    /// <param name="Size">边长（像素，1–256）。</param>
    /// <param name="Png">该尺寸的 PNG 数据。</param>
    public readonly record struct IcoImage(int Size, byte[] Png);

    /// <summary>
    /// 由一组 PNG 构造 ICO 字节。
    /// </summary>
    /// <param name="images">各尺寸图像（建议至少给 16 与 32，托盘在高 DPI 下会取大图）。</param>
    public static byte[] Build(IReadOnlyList<IcoImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("至少需要一个尺寸", nameof(images));
        }

        if (images.Count > 255)
        {
            throw new ArgumentException("ICO 尺寸条目过多", nameof(images));
        }

        foreach (var image in images)
        {
            ArgumentNullException.ThrowIfNull(image.Png);
            if (image.Size is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(images), $"尺寸必须在 1–256 之间：{image.Size}");
            }

            if (image.Png.Length == 0)
            {
                throw new ArgumentException("PNG 数据为空", nameof(images));
            }
        }

        var headerSize = DirectorySize + (EntrySize * images.Count);
        var totalSize = headerSize + images.Sum(static image => image.Png.Length);
        var buffer = new byte[totalSize];

        // ICONDIR：保留字段 0、类型 1（图标）、图像数量
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)images.Count);

        var offset = headerSize;
        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var entry = buffer.AsSpan(DirectorySize + (EntrySize * index), EntrySize);

            // 宽高为 256 时写 0（ICO 规范的历史遗留表示法）
            entry[0] = (byte)(image.Size == 256 ? 0 : image.Size);
            entry[1] = (byte)(image.Size == 256 ? 0 : image.Size);
            entry[2] = 0;   // 调色板颜色数（PNG 图标固定 0）
            entry[3] = 0;   // 保留
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);                       // 颜色平面
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);                      // 位深
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)image.Png.Length);  // 数据长度
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);           // 数据偏移

            image.Png.CopyTo(buffer, offset);
            offset += image.Png.Length;
        }

        return buffer;
    }

    /// <summary>单尺寸便捷构造。</summary>
    /// <param name="size">边长（像素）。</param>
    /// <param name="png">PNG 数据。</param>
    public static byte[] BuildSingle(int size, byte[] png) => Build([new IcoImage(size, png)]);

    /// <summary>
    /// 从 ICO 字节里取出最接近期望尺寸的<b>图像数据</b>（PNG）。
    /// <para>
    /// 用途：<c>CreateIconFromResourceEx</c> 要的是「单个图标图像」（ICONIMAGE），
    /// 不是整个 .ico 文件（含 ICONDIR 目录）；直接传整份文件会失败（实测返回 0、错误码 0）。
    /// </para>
    /// </summary>
    /// <param name="ico">ICO 字节。</param>
    /// <param name="preferredSize">期望边长（像素）。</param>
    /// <param name="image">取出的图像数据。</param>
    /// <param name="width">该图像的边长。</param>
    public static bool TryGetImage(byte[] ico, int preferredSize, out byte[] image, out int width)
    {
        ArgumentNullException.ThrowIfNull(ico);
        image = [];
        width = 0;

        if (ico.Length < DirectorySize)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        if (count == 0 || ico.Length < DirectorySize + (EntrySize * count))
        {
            return false;
        }

        var bestDistance = int.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var entry = ico.AsSpan(DirectorySize + (EntrySize * index), EntrySize);

            // 目录里 0 表示 256（规范的历史遗留写法）
            var entryWidth = entry[0] == 0 ? 256 : entry[0];
            var bytesInRes = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);

            if (bytesInRes <= 0 || offset < 0 || offset + bytesInRes > ico.Length)
            {
                continue;
            }

            var distance = Math.Abs(entryWidth - preferredSize);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            width = entryWidth;
            image = ico[offset..(offset + bytesInRes)];
        }

        return width > 0;
    }
}
