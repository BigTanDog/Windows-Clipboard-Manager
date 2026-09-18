namespace ClipboardManager.Core.Clipboard;

/// <summary>
/// CF_HDROP 路径校验（需求 §3.2 / 技术设计 §5.1，AGENTS.md §6「路径处理」）。
/// <para>
/// 规则：长度上限 → 非法字符 → 必须是绝对路径 → 用 <see cref="Path.GetFullPath(string)"/>
/// 规范化（<b>禁止字符串拼接</b>）→ 去重保序 → 条数上限。
/// 只处理路径字符串，<b>绝不读取文件内容</b>。
/// </para>
/// </summary>
public static class PathValidator
{
    /// <summary>单条路径长度上限（Win32 长路径上限）。</summary>
    public const int MaxPathLength = 32767;

    /// <summary>路径条数上限。</summary>
    public const int MaxPaths = 100;

    /// <summary>
    /// 过滤并规范化路径列表。
    /// </summary>
    /// <param name="raw">原始路径（可能含空串、非法字符、相对路径）。</param>
    /// <param name="rejected">被拒绝的条数（用于日志，不含路径内容）。</param>
    public static IReadOnlyList<string> Filter(IReadOnlyList<string> raw, out int rejected)
    {
        ArgumentNullException.ThrowIfNull(raw);
        rejected = 0;

        var result = new List<string>(Math.Min(raw.Count, MaxPaths));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in raw)
        {
            if (result.Count >= MaxPaths)
            {
                rejected++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxPathLength)
            {
                rejected++;
                continue;
            }

            if (candidate.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                rejected++;
                continue;
            }

            string normalized;
            try
            {
                if (!Path.IsPathFullyQualified(candidate))
                {
                    rejected++;
                    continue;
                }

                normalized = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                rejected++;
                continue;
            }

            if (seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }
}
