using ClipboardManager.Core.Models;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Text;

namespace ClipboardManager.App.ViewModels;

/// <summary>
/// 列表项的展示模型。
/// <para>
/// 关键点（D-13）：<b>脱敏只发生在这里</b> —— <see cref="Source"/> 始终是原文，
/// 写回剪贴板用的是 <see cref="ClipItem.TextContent"/>，展示才用 <see cref="DisplayText"/>。
/// </para>
/// </summary>
public sealed class ClipItemViewModel
{
    /// <summary>创建展示模型。</summary>
    /// <param name="source">原始记录（原文）。</param>
    /// <param name="masker">脱敏器（设置项关闭时为 <see cref="SensitiveMasker.Disabled"/>）。</param>
    /// <param name="now">当前时间（用于相对时间文案，显式传入便于单测）。</param>
    public ClipItemViewModel(ClipItem source, SensitiveMasker masker, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(masker);

        Source = source;
        DisplayText = masker.Mask(source.Preview);
        TimeText = RelativeTime.Format(source.UpdatedAt, now);
        Tooltip = DisplayText;
    }

    /// <summary>原始记录（原文，供写回剪贴板使用）。</summary>
    public ClipItem Source { get; }

    /// <summary>显示文本（已脱敏）。</summary>
    public string DisplayText { get; }

    /// <summary>相对时间文案。</summary>
    public string TimeText { get; }

    /// <summary>悬停提示（已脱敏）。</summary>
    public string Tooltip { get; }
}
