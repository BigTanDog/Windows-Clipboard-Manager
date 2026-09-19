using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Text;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// 列表项的展示模型。
/// <para>
/// 关键点（D-13）：<b>脱敏只发生在这里</b> —— <see cref="Source"/> 始终是原文，
/// 写回剪贴板用的是记录原文，只有展示文本会被脱敏。
/// </para>
/// </summary>
public sealed class ClipItemViewModel
{
    /// <summary>创建展示模型。</summary>
    /// <param name="source">原始记录（原文）。</param>
    /// <param name="masker">脱敏器（设置项关闭时为 <see cref="SensitiveMasker.Disabled"/>）。</param>
    /// <param name="now">当前时间（用于相对时间文案）。</param>
    /// <param name="searchQuery">当前搜索词（非空时展示命中片段）。</param>
    /// <param name="thumbnailPath">缩略图绝对路径（图片记录才有）。</param>
    /// <param name="isCurrentClipboardItem">
    /// 这条是否<strong>正装在系统剪贴板里</strong>（由宿主持 <c>ClipboardPresenceTracker</c> 判定后传入；
    /// 判定必须带实时序列号，否则剪贴板被改写后徽标会失准）。
    /// </param>
    public ClipItemViewModel(
        ClipItem source,
        SensitiveMasker masker,
        DateTimeOffset now,
        string? searchQuery = null,
        string? thumbnailPath = null,
        bool isCurrentClipboardItem = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(masker);

        Source = source;
        TypeLabel = PreviewBuilder.TypeLabel(source.Type);
        KindKey = ResolveKind(source);
        KindLabel = KindLabelFor(KindKey, TypeLabel);
        TimeText = RelativeTime.Format(source.UpdatedAt, now);
        ThumbnailPath = thumbnailPath;
        HasThumbnail = !string.IsNullOrEmpty(thumbnailPath);
        IsPinned = source.IsPinned;
        IsCurrentInClipboard = isCurrentClipboardItem;

        // 搜索命中时展示命中附近的片段（仍要脱敏），否则展示摘要。
        var display = string.IsNullOrWhiteSpace(searchQuery)
            ? source.Preview
            : SearchSnippet.Extract(source.Preview, searchQuery);

        DisplayText = masker.Mask(display);
        Tooltip = DisplayText;
    }

    /// <summary>原始记录（原文，供写回剪贴板使用）。</summary>
    public ClipItem Source { get; }

    /// <summary>显示文本（已脱敏，可能是搜索命中片段）。</summary>
    public string DisplayText { get; }

    /// <summary>类型徽标文字（文本 / HTML / 图片 / 文件）。</summary>
    public string TypeLabel { get; }

    /// <summary>
    /// 列表标识用的种类键：Text / Image / File / Html / Link。
    /// <para>XAML 用它切换徽标配色与矢量图标（DataTrigger 绑定本属性）。</para>
    /// </summary>
    public string KindKey { get; }

    /// <summary>种类徽标文字（与 <see cref="KindKey"/> 对应，网址会显示「网站」）。</summary>
    public string KindLabel { get; }

    /// <summary>判定种类：识别出单个网址时归为 Link（网站），其余按内容类型。</summary>
    private static string ResolveKind(ClipItem source)
    {
        if (source.Type != ClipContentType.Text)
        {
            return source.Type switch
            {
                ClipContentType.Image => "Image",
                ClipContentType.FileList => "File",
                ClipContentType.Html => "Html",
                _ => "Text",
            };
        }

        // 文本类型再看是不是「单个网址」：只用于显示层标识，不改变存储与粘贴内容。
        return LinkText.TryDetectUrl(source.TextContent, out _) || LinkText.TryDetectUrl(source.Preview, out _)
            ? "Link"
            : "Text";
    }

    private static string KindLabelFor(string kindKey, string fallback) => kindKey switch
    {
        "Link" => "网站",
        "Image" => "图片",
        "File" => "文件",
        "Html" => "HTML",
        "Text" => "文本",
        _ => fallback,
    };

    /// <summary>相对时间文案。</summary>
    public string TimeText { get; }

    /// <summary>悬停提示（已脱敏）。</summary>
    public string Tooltip { get; }

    /// <summary>缩略图绝对路径（图片记录）。</summary>
    public string? ThumbnailPath { get; }

    /// <summary>是否有缩略图。</summary>
    public bool HasThumbnail { get; }

    /// <summary>是否已收藏（收藏项永不自动淘汰，需求 §3.3）。</summary>
    public bool IsPinned { get; }

    /// <summary>收藏标记文字（收藏时显示 ★，否则为空以保持紧凑）。</summary>
    public string PinGlyph => IsPinned ? "★" : string.Empty;

    /// <summary>
    /// 是否正是当前系统剪贴板里的那条（列表显示「剪贴板中」徽标）。
    /// <para>它同时是「删除即吊销」的预兆：删掉这一条，剪贴板里的内容也会被一并清空。</para>
    /// </summary>
    public bool IsCurrentInClipboard { get; }
}
