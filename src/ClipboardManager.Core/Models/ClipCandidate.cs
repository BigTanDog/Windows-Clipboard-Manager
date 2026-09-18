namespace ClipboardManager.Core.Models;

/// <summary>
/// 从剪贴板读出的「原始候选」。
/// <para>
/// 这是跨线程传递的唯一形态：只包含已经拷贝到托管内存的纯数据，
/// <b>绝不包含任何 Win32 句柄或剪贴板引用</b>（AGENTS.md §3）。
/// </para>
/// </summary>
public sealed record ClipCandidate
{
    /// <summary>内容类型。</summary>
    public required ClipContentType Type { get; init; }

    /// <summary>纯文本内容（文本类型）；HTML 类型时为派生纯文本。</summary>
    public string? Text { get; init; }

    /// <summary>文件路径列表（文件类型）。</summary>
    public IReadOnlyList<string> FilePaths { get; init; } = [];

    /// <summary>二进制本体（图片类型，阶段二启用）。</summary>
    public byte[]? Binary { get; init; }

    /// <summary>二进制本体的文件扩展名（不含点），如 <c>png</c>。</summary>
    public string? BlobExtension { get; init; }

    /// <summary>来源进程名（可获取到时）。</summary>
    public string? SourceApp { get; init; }

    /// <summary>捕获时间。</summary>
    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>内容体积估算（用于磁盘上限淘汰排序）。</summary>
    public long SizeBytes
    {
        get
        {
            long size = Binary?.LongLength ?? 0;
            size += Text is null ? 0 : Text.Length * 2L;
            foreach (var path in FilePaths)
            {
                size += path.Length * 2L;
            }

            return size;
        }
    }
}
