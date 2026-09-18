namespace ClipboardManager.Core.AutoStart;

/// <summary>
/// 开机自启命令行的构造与解析（需求 §3.5：写 <c>HKCU\...\Run</c>，可选延迟启动）。
/// <para>
/// 命令行拼接是经典出错点：程序路径含空格时必须加引号，否则 Windows 会把路径截断成
/// 多个参数。这里把规则固化成纯函数并单测（AGENTS.md §2）。
/// </para>
/// </summary>
public static class AutoStartCommand
{
    /// <summary>延迟启动参数前缀（秒）。</summary>
    public const string DelayArgumentPrefix = "--delay=";

    /// <summary>延迟上限（秒）—— 再长就没有实际意义了。</summary>
    public const int MaxDelaySeconds = 120;

    /// <summary>
    /// 构造写入注册表的命令行。
    /// </summary>
    /// <param name="executablePath">程序完整路径。</param>
    /// <param name="delaySeconds">延迟启动秒数；0 表示不延迟。</param>
    public static string Build(string executablePath, int delaySeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        // 路径统一加双引号；路径内部的双引号按 Windows 规则转义（正常路径不会出现）。
        var quoted = "\"" + executablePath.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

        var delay = Math.Clamp(delaySeconds, 0, MaxDelaySeconds);
        return delay > 0 ? $"{quoted} {DelayArgumentPrefix}{delay}" : quoted;
    }

    /// <summary>
    /// 从命令行参数里解析延迟秒数；缺省、非法或超范围一律返回 0（不延迟）。
    /// </summary>
    /// <param name="args">进程参数（可能为 null）。</param>
    public static int ParseDelay(string[]? args)
    {
        if (args is null)
        {
            return 0;
        }

        foreach (var arg in args)
        {
            if (arg is null || !arg.StartsWith(DelayArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = arg[DelayArgumentPrefix.Length..];
            if (int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && seconds is > 0 and <= MaxDelaySeconds)
            {
                return seconds;
            }
        }

        return 0;
    }
}
