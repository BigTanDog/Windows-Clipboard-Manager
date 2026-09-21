namespace ClipboardManager.Core.Text;

/// <summary>
/// 常见图片文件后缀识别（用户 2026-09-21 需求）。
/// <para>
/// 用途：在资源管理器里 Ctrl+C 复制一个图片文件时，剪贴板里其实是 <c>CF_HDROP</c>（文件列表），
/// 过去只能显示成「文件 + 文件名」。现在识别出是图片文件就给它生成缩略图、按图片显示。
/// </para>
/// <para>
/// 注意：<b>只影响显示</b> —— 记录的存储类型仍是"文件"，粘贴出去的也仍然是文件本身
/// （改成图片会让"粘到资源管理器里变成图片内容"这种错误行为）。
/// </para>
/// </summary>
public static class ImageFileExtensions
{
    /// <summary>可识别的图片后缀（小写、带点）。能否真正解码由成像栈决定，认不出来就退化为普通文件。</summary>
    private static readonly string[] KnownExtensions =
    [
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".gif", ".webp",
        ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif",
    ];

    /// <summary>路径后缀是否属于常见图片格式。</summary>
    /// <param name="path">文件路径（可为 null）。</param>
    public static bool IsImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
        {
            return false;
        }

        foreach (var known in KnownExtensions)
        {
            if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 取「恰好一个图片文件」的路径；条数不是 1、或那一个不是图片时返回 null。
    /// <para>
    /// 刻意只认单个文件：多选一堆文件（可能含图片）时缩略图只会造成误解，保持原样显示更清楚。
    /// </para>
    /// </summary>
    /// <param name="paths">文件路径列表。</param>
    public static string? TryGetSingleImageFile(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return paths.Count == 1 && IsImageFile(paths[0]) ? paths[0] : null;
    }
}
