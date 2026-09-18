namespace ClipboardManager.Core.Text;

/// <summary>
/// 体积文案格式化（设置页「当前占用」与上限提示共用）。
/// <para>
/// 口径与 Windows 资源管理器一致：1024 进制、最多一位小数（GB 两位），
/// 单位之间用空格分隔，便于与系统显示对照。
/// </para>
/// </summary>
public static class ByteSize
{
    /// <summary>把字节数格式化为可读文案（负数按 0 处理）。</summary>
    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
        };
    }

    /// <summary>
    /// 格式化为「已用 / 上限」文案；上限为负（不限制）时只显示已用。
    /// </summary>
    /// <param name="usedBytes">已用字节数。</param>
    /// <param name="limitMegabytes">上限（MB，负数表示不限制）。</param>
    public static string FormatWithLimit(long usedBytes, int limitMegabytes)
    {
        var used = Format(usedBytes);
        return limitMegabytes < 0
            ? $"{used}（未限制上限）"
            : $"{used} / {limitMegabytes} MB";
    }
}
