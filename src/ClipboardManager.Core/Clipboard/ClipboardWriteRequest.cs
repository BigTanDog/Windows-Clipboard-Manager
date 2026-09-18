namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 一次剪贴板写入请求：可以同时携带多种格式（D-08 要求 HTML 记录写回时附纯文本降级）。
/// 由调用方填充，Interop 层在一次开锁内把非空格式全部写上去。
/// </summary>
public sealed record ClipboardWriteRequest
{
    /// <summary>纯文本（CF_UNICODETEXT）。</summary>
    public string? Text { get; init; }

    /// <summary>HTML 片段（会被包成完整的 "HTML Format" 数据）。</summary>
    public string? Html { get; init; }

    /// <summary>CF_DIBV5 字节（由 <c>DibWriter</c> 构造）。</summary>
    public byte[]? DibV5 { get; init; }

    /// <summary>文件路径列表（会被包成 CF_HDROP）。</summary>
    public IReadOnlyList<string>? FilePaths { get; init; }

    /// <summary>
    /// 该记录在库中的内容哈希。写入后登记给自循环过滤器，避免自己的写入被再记一条
    /// （需求 §5.1；序列号是主判据，哈希是时间窗内的兜底判据）。
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>是否至少带了一种格式。</summary>
    public bool HasAnyFormat =>
        !string.IsNullOrEmpty(Text)
        || !string.IsNullOrEmpty(Html)
        || DibV5 is { Length: > 0 }
        || FilePaths is { Count: > 0 };
}
