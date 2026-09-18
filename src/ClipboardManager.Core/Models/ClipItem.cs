namespace ClipboardManager.Core.Models;

/// <summary>
/// 一条已落库的剪贴板历史记录（只读快照）。
/// </summary>
public sealed record ClipItem
{
    /// <summary>自增主键（仅存储层使用，UI 不需要关心）。</summary>
    public required long Id { get; init; }

    /// <summary>内容类型。</summary>
    public required ClipContentType Type { get; init; }

    /// <summary>纯文本内容（文本类型全量；其他类型为搜索用摘要）。</summary>
    public string? TextContent { get; init; }

    /// <summary>本体文件相对路径（图片 / HTML）。</summary>
    public string? BlobPath { get; init; }

    /// <summary>文件路径列表。</summary>
    public IReadOnlyList<string> FilePaths { get; init; } = [];

    /// <summary>列表展示用摘要（≤200 字符，<b>原文</b>；显示时由脱敏器处理）。</summary>
    public required string Preview { get; init; }

    /// <summary>内容 SHA-256（去重键）。</summary>
    public required string ContentHash { get; init; }

    /// <summary>内容体积（字节）。</summary>
    public long SizeBytes { get; init; }

    /// <summary>是否收藏（收藏项永不参与自动淘汰）。</summary>
    public bool IsPinned { get; init; }

    /// <summary>来源进程名（可获取到时）。</summary>
    public string? SourceApp { get; init; }

    /// <summary>首次记录时间。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>最近一次复制时间（去重命中时刷新）。</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
