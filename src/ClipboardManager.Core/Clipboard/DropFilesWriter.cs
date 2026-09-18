using System.Buffers.Binary;
using System.Text;

namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 构造 <c>CF_HDROP</c> 数据（把历史里的文件路径列表写回剪贴板，供资源管理器粘贴）。
/// <para>
/// 结构：<c>DROPFILES</c>（pFiles=20, pt, fNC, fWide=1）+ 以 <c>NUL</c> 分隔、
/// <b>双 NUL 结尾</b>的 UTF-16 路径列表。
/// </para>
/// </summary>
public static class DropFilesWriter
{
    /// <summary>DROPFILES 结构大小：DWORD + POINT(8) + BOOL + BOOL。</summary>
    public const int DropFilesHeaderSize = 20;

    /// <summary>
    /// 构造 CF_HDROP 字节。
    /// </summary>
    /// <param name="paths">已经是绝对路径的列表（调用方负责先用 <see cref="PathValidator"/> 过滤）。</param>
    public static byte[] Build(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("路径列表不能为空", nameof(paths));
        }

        var text = string.Join('\0', paths) + "\0\0";
        var textBytes = Encoding.Unicode.GetByteCount(text);
        var buffer = new byte[DropFilesHeaderSize + textBytes];

        BinaryPrimitives.WriteUInt32LittleEndian(buffer, DropFilesHeaderSize);   // pFiles：路径列表相对结构起点的偏移
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 0);            // pt.x
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), 0);            // pt.y
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12), 0);           // fNC：非客户端坐标标志
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), 1);           // fWide：UTF-16

        _ = Encoding.Unicode.GetBytes(text, buffer.AsSpan(DropFilesHeaderSize));
        return buffer;
    }
}
