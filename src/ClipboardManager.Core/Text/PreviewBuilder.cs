using System.Text;

namespace ClipboardManager.Core.Text;

/// <summary>
/// 生成列表展示用的单行摘要（需求 §4.3：<c>preview</c> 限长，建议 200 字符）。
/// 注意：这里产出的是<b>原文摘要</b>，落库保存；显示时再由脱敏器处理（D-13）。
/// </summary>
public static class PreviewBuilder
{
    /// <summary>摘要字符上限。</summary>
    public const int MaxPreviewChars = 200;

    /// <summary>文本摘要：换行折叠为空格、连续空白压缩、超长截断并追加省略号。</summary>
    public static string ForText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, MaxPreviewChars + 1));
        var lastWasSpace = false;

        foreach (var ch in text)
        {
            if (builder.Length > MaxPreviewChars)
            {
                break;
            }

            var isSpace = char.IsWhiteSpace(ch);
            if (isSpace)
            {
                if (lastWasSpace)
                {
                    continue;
                }

                builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        var preview = builder.ToString().Trim();
        return preview.Length > MaxPreviewChars
            ? string.Concat(preview.AsSpan(0, MaxPreviewChars), "…")
            : preview;
    }

    /// <summary>图片摘要：尺寸 + 体积（需求 §3.4 列表项显示内容摘要）。</summary>
    public static string ForImage(int width, int height, long sizeBytes)
    {
        var bytes = Math.Max(sizeBytes, 0);
        var size = bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        };

        return $"{width}×{height} · {size}";
    }

    /// <summary>内容类型标签（面板列表左侧的类型徽标文字，替代图标）。</summary>
    public static string TypeLabel(Models.ClipContentType type) => type switch
    {
        Models.ClipContentType.Text => "文本",
        Models.ClipContentType.Html => "HTML",
        Models.ClipContentType.Image => "图片",
        Models.ClipContentType.FileList => "文件",
        _ => "未知",
    };

    /// <summary>文件列表摘要：显示文件名（不显示完整路径中的用户目录），保留顺序。</summary>
    public static string ForFileList(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        var names = paths.Select(static p =>
        {
            var name = Path.GetFileName(p);
            return string.IsNullOrEmpty(name) ? p : name;
        });

        var joined = string.Join("  ", names);
        var summary = $"{paths.Count} 个文件：{joined}";
        return summary.Length > MaxPreviewChars
            ? string.Concat(summary.AsSpan(0, MaxPreviewChars), "…")
            : summary;
    }
}
