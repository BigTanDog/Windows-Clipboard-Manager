namespace ClipboardManager.Core.Text;

/// <summary>
/// 判定一段剪贴板文本是不是「单个网址」。
/// <para>
/// 用途：列表里给网站类型单独的标识（需求：复制的是网站时做出区分）。
/// 纯字符串判定：不联网、不做 DNS 解析、不补全 scheme，只做保守的形态检查，
/// 宁可漏判也不要把普通文本误判成网站。
/// </para>
/// </summary>
public static class LinkText
{
    /// <summary>可接受的最长网址（超过则视为普通文本，避免把大段内容当网址）。</summary>
    public const int MaxUrlLength = 2048;

    /// <summary>
    /// 尝试识别单个网址。
    /// </summary>
    /// <param name="text">剪贴板文本（原文）。</param>
    /// <param name="url">识别出的网址（未识别时为空串）。</param>
    /// <returns>是否为单个网址。</returns>
    public static bool TryDetectUrl(string? text, out string url)
    {
        url = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = text.Trim();
        if (candidate.Length > MaxUrlLength)
        {
            return false;
        }

        // 内部还有空白（含换行）说明是整段文本里夹着网址，不是「复制了一个网站」
        foreach (var ch in candidate)
        {
            if (char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        var hasScheme = candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var hasWww = candidate.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
        if (!hasScheme && !hasWww)
        {
            return false;
        }

        // 取出 host 部分（去掉协议、路径、查询、片段）
        var afterScheme = hasScheme ? candidate[(candidate.IndexOf("//", StringComparison.Ordinal) + 2)..] : candidate;
        var hostEnd = afterScheme.IndexOfAny(['/', '?', '#']);
        var host = hostEnd < 0 ? afterScheme : afterScheme[..hostEnd];
        if (host.Length < 4 || host.StartsWith('.') || host.EndsWith('.'))
        {
            return false;
        }

        foreach (var ch in host)
        {
            var allowed = char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' or ':' or '[' or ']' or '%' or '+';
            if (!allowed)
            {
                return false;
            }
        }

        // 至少要有「名字.顶级域」，且顶级域 ≥2 个字母（排除 "http://1.2" 这类）
        var lastDot = host.LastIndexOf('.');
        if (lastDot <= 0 || host.Length - lastDot - 1 < 2)
        {
            return false;
        }

        var hasTopLevelLetter = false;
        for (var index = lastDot + 1; index < host.Length; index++)
        {
            if (char.IsLetter(host[index]))
            {
                hasTopLevelLetter = true;
                break;
            }
        }

        if (!hasTopLevelLetter)
        {
            return false;
        }

        url = candidate;
        return true;
    }
}
