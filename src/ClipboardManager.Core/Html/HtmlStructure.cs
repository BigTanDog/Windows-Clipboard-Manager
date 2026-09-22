namespace ClipboardManager.Core.Html;

/// <summary>
/// 判断 HTML 片段里是否存在「结构 / 语义元素」（用户 2026-09-22 反馈）。
/// <para>
/// 背景：在浏览器网页里选中一段文字复制时，剪贴板会**同时**带 `HTML Format` 与纯文本；
/// 我们的读取优先级是先 HTML，于是"复制一句话"被存成了 HTML 记录、列表里显示「HTML」徽标，
/// 而用户明明以为自己复制的是文字。
/// </para>
/// <para>
/// 规则：<b>只有"纯样式"的片段才降级为文本</b>。判定依据是片段里有没有下面这些元素 ——
/// 它们一旦出现，降级就会丢信息（链接目标、图片、列表/表格结构、代码排版、换行与分段），
/// 必须继续保留 HTML 记录：
/// <list type="bullet">
/// <item>链接与媒体：<c>a img video audio iframe svg</c></item>
/// <item>列表与表格：<c>ul ol li dl dt dd table thead tbody tfoot tr td th caption</c></item>
/// <item>代码与引用：<c>pre code kbd samp blockquote</c></item>
/// <item>结构/换行：<c>p div br hr section article aside header footer main nav figure figcaption</c></item>
/// </list>
/// 反之，纯排版样式（<c>span font b strong i em u s h1~h6 sub sup mark small</c> 的颜色/字体/字号/粗细）
/// 不算结构 —— 浏览器选区恰好总是包一层带 <c>style</c> 的 <c>span</c>，那正是要降级的情形。
/// </para>
/// <para>
/// 为什么换行类标签也算结构：派生纯文本（<see cref="HtmlClipboardParser.ToPlainText"/>）会把空白
/// <b>折叠成单空格</b>，若把多段/多行内容降级成文本，行结构就丢了。所以宁可保留 HTML。
/// </para>
/// </summary>
public static class HtmlStructure
{
    /// <summary>
    /// 出现即"必须保留 HTML"的元素名（小写比较；命名空间前缀会被去掉，
    /// 例如 Word 的 <c>o:p</c> 按 <c>p</c> 处理）。
    /// </summary>
    private static readonly string[] StructuralTags =
    [
        // 链接与媒体
        "a", "img", "video", "audio", "iframe", "svg", "object", "embed",
        // 列表与表格
        "ul", "ol", "li", "dl", "dt", "dd",
        "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption",
        // 代码与引用
        "pre", "code", "kbd", "samp", "blockquote",
        // 结构 / 换行
        "p", "div", "br", "hr", "section", "article", "aside",
        "header", "footer", "main", "nav", "figure", "figcaption",
    ];

    /// <summary>
    /// 片段里是否存在结构 / 语义元素。
    /// </summary>
    /// <param name="html">HTML 片段（可为 null）。</param>
    /// <returns>true = 必须保留 HTML 记录；false = 只是"带样式的文字"，可按纯文本入库。</returns>
    public static bool HasStructuralElement(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return false;
        }

        var index = 0;
        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);
            if (open < 0)
            {
                return false;
            }

            var close = html.IndexOf('>', open + 1);
            if (close < 0)
            {
                return false; // 未闭合的尖括号：后面没有可判定的标签了
            }

            if (IsStructuralTag(html.AsSpan(open + 1, close - open - 1)))
            {
                return true;
            }

            index = close + 1;
        }

        return false;
    }

    /// <summary>标签内部文本（<c>&lt;...&gt;</c> 之间的部分）是否为结构元素。</summary>
    private static bool IsStructuralTag(ReadOnlySpan<char> inner)
    {
        // 结束标签（/p）、注释（!--）、声明（?xml / !DOCTYPE）都不算结构证据
        if (inner.IsEmpty || inner[0] is '/' or '!' or '?')
        {
            return false;
        }

        var length = 0;
        while (length < inner.Length && (char.IsLetterOrDigit(inner[length]) || inner[length] is '-' or ':'))
        {
            length++;
        }

        if (length == 0)
        {
            return false;
        }

        var name = inner[..length];

        // 去掉命名空间前缀：o:p / xhtml:p / svg:rect 之类按本名判定
        var colon = name.LastIndexOf(':');
        if (colon >= 0)
        {
            name = name[(colon + 1)..];
        }

        var text = name.ToString();
        foreach (var tag in StructuralTags)
        {
            if (string.Equals(tag, text, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
