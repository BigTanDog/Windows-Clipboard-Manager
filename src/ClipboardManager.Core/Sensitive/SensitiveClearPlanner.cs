using ClipboardManager.Core.Html;
using ClipboardManager.Core.Models;

namespace ClipboardManager.Core.Sensitive;

/// <summary>
/// 一次「敏感内容到点自动清空剪贴板」的排定（附加项 B-09）。
/// </summary>
/// <param name="Sequence">排定时该内容对应的剪贴板序列号；到点按它复核，内容被换掉就放弃清空。</param>
/// <param name="Deadline">应当清空的时刻。</param>
/// <param name="Minutes">设置里选的分钟数（只用于日志）。</param>
public sealed record SensitiveClearPlan(long Sequence, DateTimeOffset Deadline, int Minutes);

/// <summary>
/// 敏感内容自动清空的排定规则（纯函数，可单测；附加项 B-09）。
/// <para>
/// 语义：捕获到的内容里含敏感信息（手机号 / 身份证 / 银行卡 / 邮箱 / 密钥 / 密码键值）时，
/// 过了设置的分钟数就把<b>系统剪贴板</b>清掉。清空前会用序列号复核，
/// 因此「用户在这段时间里又复制了别的东西」时什么都不会发生（绝不误清）。
/// </para>
/// <para>
/// 只对<b>文本类</b>内容生效：图片、文件列表没有可判定的文本，不做猜测（宁可不清，也不误判）。
/// </para>
/// <para>
/// 与「脱敏显示」（D-13）共用 <see cref="SensitiveMasker.ContainsSensitive"/> 的判定口径，
/// 但两者开关互不影响。
/// </para>
/// </summary>
public static class SensitiveClearPlanner
{
    /// <summary>
    /// 判断这条剪贴板内容是否需要排定自动清空；返回 null 表示不需要（关闭 / 非敏感 / 无法判定）。
    /// </summary>
    /// <param name="candidate">剪贴板内容（捕获路径给的是已处理候选，设置生效路径给的是原始读取结果）。</param>
    /// <param name="minutes">设置档位（&lt;= 0 表示功能关闭）。</param>
    /// <param name="sequence">该内容对应的剪贴板序列号。</param>
    /// <param name="now">当前时刻。</param>
    public static SensitiveClearPlan? Plan(ClipCandidate candidate, int minutes, long sequence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (minutes <= 0 || sequence < 0)
        {
            return null;
        }

        return SensitiveMasker.ContainsSensitive(TextOf(candidate))
            ? new SensitiveClearPlan(sequence, now.AddMinutes(minutes), minutes)
            : null;
    }

    /// <summary>
    /// 取用于判定的文本：文本类型直接用；HTML 用解析出的纯文本
    /// （设置刚打开时走的是"直接读剪贴板"路径，此时 HTML 候选还没派生纯文本，必须在这里补上）；
    /// 图片与文件列表没有可判定文本。
    /// </summary>
    private static string? TextOf(ClipCandidate candidate)
    {
        if (!string.IsNullOrEmpty(candidate.Text))
        {
            return candidate.Text;
        }

        if (candidate.Type != ClipContentType.Html || candidate.Binary is not { Length: > 0 } raw)
        {
            return null;
        }

        return HtmlClipboardParser.TryParse(raw, out var parsed, out _) && parsed is not null
            ? parsed.PlainText
            : null;
    }
}
