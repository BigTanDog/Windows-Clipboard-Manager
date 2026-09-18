using System.Globalization;

namespace ClipboardManager.Core.Text;

/// <summary>
/// 相对时间文案（列表右侧显示）。纯函数，便于单测与本地化替换。
/// </summary>
public static class RelativeTime
{
    /// <summary>
    /// 把时间点格式化为相对现在的中文文案。
    /// </summary>
    /// <param name="when">目标时间点。</param>
    /// <param name="now">当前时间点（显式传入便于测试）。</param>
    public static string Format(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;
        if (delta < TimeSpan.Zero)
        {
            // 时钟回拨或来源时间戳超前：按「刚刚」处理，不显示负数。
            delta = TimeSpan.Zero;
        }

        if (delta < TimeSpan.FromSeconds(60))
        {
            return "刚刚";
        }

        if (delta < TimeSpan.FromHours(1))
        {
            return $"{(int)delta.TotalMinutes} 分钟前";
        }

        var localNow = now.ToLocalTime();
        var localWhen = when.ToLocalTime();

        if (localNow.Date == localWhen.Date)
        {
            return $"{(int)delta.TotalHours} 小时前";
        }

        if (localNow.Date.AddDays(-1) == localWhen.Date)
        {
            return "昨天 " + localWhen.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return localWhen.Year == localNow.Year
            ? localWhen.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)
            : localWhen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
