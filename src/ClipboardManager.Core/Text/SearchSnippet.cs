namespace ClipboardManager.Core.Text;

/// <summary>
/// 搜索命中片段提取（技术设计 §6.3）：命中位置在展示窗口之外时，截取命中附近的上下文。
/// 产出的片段仍要经过脱敏器再显示（D-13）。
/// </summary>
public static class SearchSnippet
{
    /// <summary>片段长度上限。</summary>
    public const int DefaultSnippetChars = 120;

    /// <summary>
    /// 截取包含首次命中的片段（两侧加省略号）。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="query">查询词（大小写不敏感）。</param>
    /// <param name="maxChars">片段长度上限。</param>
    public static string Extract(string? text, string? query, int maxChars = DefaultSnippetChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (maxChars <= 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(query) || text.Length <= maxChars)
        {
            return Truncate(text, maxChars);
        }

        var index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return Truncate(text, maxChars);
        }

        var leading = Math.Max(0, (maxChars - query.Length) / 2);
        var start = Math.Max(0, index - leading);
        var length = Math.Min(maxChars, text.Length - start);

        var snippet = text.Substring(start, length);
        if (start > 0)
        {
            snippet = "…" + snippet;
        }

        if (start + length < text.Length)
        {
            snippet += "…";
        }

        return snippet;
    }

    private static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : string.Concat(text.AsSpan(0, maxChars), "…");
}
