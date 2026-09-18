using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// 剪贴板格式探测优先级（需求 §3.2）：一次复制往往同时存在多种格式，取优先级最高者。
/// 以数据形式表达，便于单测断言与后续调整。
/// </summary>
public static class ClipboardFormatPriority
{
    /// <summary>优先级从高到低。</summary>
    public static readonly ClipContentType[] Order =
    [
        ClipContentType.FileList,
        ClipContentType.Image,
        ClipContentType.Html,
        ClipContentType.Text,
    ];

    /// <summary>给定当前可用的格式集合，返回优先级最高的一个；都不支持则返回 null。</summary>
    public static ClipContentType? Pick(IReadOnlyCollection<ClipContentType> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        foreach (var candidate in Order)
        {
            if (available.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
