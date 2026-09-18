using System.Text;

namespace ClipboardManager.Core.Html;

/// <summary>解析后的 HTML 剪贴板内容。</summary>
/// <param name="Html">真正的 HTML 片段（已按偏移切出）。</param>
/// <param name="PlainText">派生纯文本（去标签、实体解码、空白折叠），用于搜索与写回时的降级格式。</param>
public sealed record HtmlClipboardContent(string Html, string PlainText);

/// <summary>
/// <c>"HTML Format"</c> 解析器（需求 §5.4，技术设计 §5.3）。
/// <para>
/// 关键点：头部里的 <c>StartHTML/EndHTML/StartFragment/EndFragment</c> 是
/// <b>UTF-8 字节偏移</b>（含头部自身），所以必须按字节切片，绝不能按字符串下标切。
/// 全部字段都要做边界校验，畸形数据一律拒绝 —— 剪贴板内容来自任意程序（AGENTS.md §6）。
/// </para>
/// </summary>
public static class HtmlClipboardParser
{
    /// <summary>HTML 字节上限（4MB，与输入上限表一致）。</summary>
    public const int MaxHtmlBytes = 4 * 1024 * 1024;

    /// <summary>派生纯文本上限（用于搜索）。</summary>
    public const int MaxPlainTextChars = 64 * 1024;

    /// <summary>头部扫描上限：头部实际只有一两百字节，扫描窗口足够且避免全量扫描。</summary>
    private const int HeaderScanLimit = 4096;

    private static readonly string[] HeaderNames =
        ["StartFragment", "EndFragment", "StartHTML", "EndHTML", "Version"];

    /// <summary>
    /// 解析 <c>"HTML Format"</c> 的原始字节。
    /// </summary>
    /// <param name="utf8">剪贴板数据（UTF-8）。</param>
    /// <param name="content">解析结果。</param>
    /// <param name="error">失败原因。</param>
    public static bool TryParse(ReadOnlySpan<byte> utf8, out HtmlClipboardContent? content, out string? error)
    {
        content = null;
        error = null;

        if (utf8.IsEmpty)
        {
            error = "HTML 数据为空";
            return false;
        }

        if (utf8.Length > MaxHtmlBytes)
        {
            error = $"HTML 数据超过上限（{utf8.Length} > {MaxHtmlBytes} 字节）";
            return false;
        }

        var header = ParseHeader(utf8);

        if (!TryResolveRange(utf8.Length, header, "StartFragment", "EndFragment", out var start, out var end)
            && !TryResolveRange(utf8.Length, header, "StartHTML", "EndHTML", out start, out end))
        {
            error = "HTML 头缺少可用的偏移字段，或偏移越界";
            return false;
        }

        var html = Encoding.UTF8.GetString(utf8.Slice(start, end - start));
        var plain = ToPlainText(html);
        content = new HtmlClipboardContent(html, plain);
        return true;
    }

    /// <summary>把 HTML 片段转成纯文本（去 script/style、去标签、实体解码、空白折叠）。</summary>
    public static string ToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(html.Length, MaxPlainTextChars + 64));
        var index = 0;

        while (index < html.Length && builder.Length <= MaxPlainTextChars)
        {
            var ch = html[index];

            if (ch == '<')
            {
                if (TryMatchTag(html, index, "script", out var scriptEnd)
                    || TryMatchTag(html, index, "style", out scriptEnd))
                {
                    // 整块丢弃，但要补一个分隔符：否则 "<div>a<script>..</script>b</div>"
                    // 会被拼成 "ab"，把原本分开的两个词粘在一起。
                    AppendSeparator(builder);
                    index = scriptEnd;
                    continue;
                }

                var gt = html.IndexOf('>', index);
                if (gt < 0)
                {
                    break; // 未闭合标签：丢弃剩余内容
                }

                AppendSeparator(builder);
                index = gt + 1;
                continue;
            }

            if (ch == '&')
            {
                if (TryDecodeEntity(html, index, out var decoded, out var consumed))
                {
                    builder.Append(decoded);
                    index += consumed;
                    continue;
                }
            }

            builder.Append(ch);
            index++;
        }

        return CollapseWhitespace(builder.ToString());
    }

    /// <summary>扫描头部字段（ASCII，逐行 <c>Name:Value</c>），遇到第一个非头部行即停止。</summary>
    private static Dictionary<string, long> ParseHeader(ReadOnlySpan<byte> utf8)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var limit = Math.Min(utf8.Length, HeaderScanLimit);
        var offset = 0;

        while (offset < limit && values.Count < HeaderNames.Length)
        {
            var lineEnd = offset;
            while (lineEnd < limit && utf8[lineEnd] is not ((byte)'\r' or (byte)'\n'))
            {
                lineEnd++;
            }

            var line = utf8[offset..lineEnd];
            var colon = line.IndexOf((byte)':');
            if (colon <= 0)
            {
                break; // 头部结束
            }

            var name = Encoding.ASCII.GetString(line[..colon]).Trim();
            var valueText = Encoding.ASCII.GetString(line[(colon + 1)..]).Trim();

            if (Array.Exists(HeaderNames, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                && long.TryParse(valueText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                values[name] = value;
            }

            // 跳过行尾（CRLF / LF / CR）
            offset = lineEnd;
            while (offset < limit && utf8[offset] is (byte)'\r' or (byte)'\n')
            {
                offset++;
            }
        }

        return values;
    }

    /// <summary>取一对偏移字段并校验范围（越界 / 负数 / 起点不小于终点一律视为不可用）。</summary>
    private static bool TryResolveRange(
        int dataLength,
        Dictionary<string, long> header,
        string startName,
        string endName,
        out int start,
        out int end)
    {
        start = 0;
        end = 0;

        if (!header.TryGetValue(startName, out var rawStart) || !header.TryGetValue(endName, out var rawEnd))
        {
            return false;
        }

        if (rawStart <= 0 || rawEnd <= rawStart || rawEnd > dataLength)
        {
            return false;
        }

        start = (int)rawStart;
        end = (int)rawEnd;
        return true;
    }

    /// <summary>判断 <paramref name="index"/> 处是否为指定标签的开标签；是则返回对应闭标签之后的位置。</summary>
    private static bool TryMatchTag(string html, int index, string tagName, out int end)
    {
        end = index;
        if (index + 1 + tagName.Length > html.Length)
        {
            return false;
        }

        if (!html.AsSpan(index + 1, tagName.Length).Equals(tagName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var after = index + 1 + tagName.Length;
        if (after < html.Length && (char.IsLetterOrDigit(html[after]) || html[after] == '-'))
        {
            return false;
        }

        var closing = html.IndexOf("</" + tagName, after, StringComparison.OrdinalIgnoreCase);
        if (closing < 0)
        {
            end = html.Length; // 没有闭标签：丢弃剩余全部内容
            return true;
        }

        var closingEnd = html.IndexOf('>', closing);
        end = closingEnd < 0 ? html.Length : closingEnd + 1;
        return true;
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && !char.IsWhiteSpace(builder[^1]))
        {
            builder.Append(' ');
        }
    }

    /// <summary>解码常见 HTML 实体（命名 + 十进制 + 十六进制），无法识别则返回 false 原样输出。</summary>
    private static bool TryDecodeEntity(string html, int index, out string decoded, out int consumed)
    {
        decoded = string.Empty;
        consumed = 0;

        var semicolon = html.IndexOf(';', index);
        if (semicolon < 0 || semicolon - index > 10)
        {
            return false;
        }

        var entity = html.AsSpan(index + 1, semicolon - index - 1);
        consumed = semicolon - index + 1;

        if (entity.Length == 0)
        {
            return false;
        }

        if (entity[0] == '#')
        {
            var isHex = entity.Length > 1 && (entity[1] is 'x' or 'X');
            var digits = isHex ? entity[2..] : entity[1..];
            if (digits.Length == 0)
            {
                return false;
            }

            if (!int.TryParse(
                    digits,
                    isHex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var code)
                || !Rune.IsValid(code))
            {
                return false;
            }

            decoded = char.ConvertFromUtf32(code);
            return true;
        }

        decoded = entity switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            _ => string.Empty,
        };

        return decoded.Length > 0;
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(Math.Min(text.Length, MaxPlainTextChars + 1));
        var lastWasSpace = false;

        foreach (var ch in text)
        {
            if (builder.Length > MaxPlainTextChars)
            {
                break;
            }

            if (char.IsWhiteSpace(ch))
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

        return builder.ToString().Trim();
    }
}
