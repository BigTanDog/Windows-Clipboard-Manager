using System.Text;
using System.Text.RegularExpressions;

namespace ClipboardManager.Core.Sensitive;

/// <summary>
/// 敏感信息脱敏器（产品计划 D-13）。
/// <para>
/// <b>只用于显示层</b>：不改变数据库存储内容，也不改变写回剪贴板的内容
/// （粘贴出去的必须是原文，否则工具失去意义）。防的是「屏幕被旁人看到 / 截图外泄」，
/// <b>不防「data 目录被拷走」</b>。
/// </para>
/// <para>
/// 安全约束（AGENTS.md §6「剪贴板内容一律视为不可信输入」）：
/// 所有正则都用 <see cref="RegexOptions.NonBacktracking"/> —— 线性时间匹配，
/// 从构造上排除 ReDoS；因此也不使用超时参数（非回溯引擎不需要超时）。
/// 同理，所有模式都不含先行/后行断言，边界判定改由代码手工检查（<see cref="IsBoundary"/>）。
/// </para>
/// </summary>
public sealed class SensitiveMasker
{
    /// <summary>掩码字符。</summary>
    private const char MaskChar = '*';

    /// <summary>掩码固定长度：不泄漏原始长度。</summary>
    private const int MaskWidth = 6;

    /// <summary>邮箱本地部分的掩码长度（技术设计 §6.5：首字符 + 4 个 *）。</summary>
    private const int EmailLocalMaskWidth = 4;

    /// <summary>邮箱域名的掩码长度（5 个 * 加顶级域）。</summary>
    private const int EmailDomainMaskWidth = 5;

    /// <summary>单次脱敏的扫描上限（字符）。超出部分不扫描，避免单条超大文本拖慢 UI。</summary>
    public const int MaxScanChars = 64 * 1024;

    private static readonly SensitiveMasker DisabledInstance = new(enabled: false);

    private readonly bool _enabled;

    /// <summary>创建脱敏器。</summary>
    /// <param name="enabled">是否启用脱敏；false 时 <see cref="Mask"/> 原样返回（用于设置项关闭）。</param>
    public SensitiveMasker(bool enabled = true) => _enabled = enabled;

    /// <summary>关闭状态的实例（设置项关闭时使用）。</summary>
    public static SensitiveMasker Disabled => DisabledInstance;

    /// <summary>是否启用。</summary>
    public bool Enabled => _enabled;

    /// <summary>
    /// 对一段文本做脱敏。多次命中按「规则声明顺序」处理，已脱敏区间不再参与后续规则匹配。
    /// </summary>
    public string Mask(string? input)
    {
        if (!_enabled || string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var truncated = input.Length > MaxScanChars;
        var text = truncated ? input[..MaxScanChars] : input;
        var edits = CollectEdits(text);

        if (edits is null || edits.Count == 0)
        {
            return truncated ? text + "…" : input;
        }

        edits.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var builder = new StringBuilder(text.Length + 8);
        var cursor = 0;
        foreach (var edit in edits)
        {
            builder.Append(text, cursor, edit.Start - cursor);
            builder.Append(edit.Replacement);
            cursor = edit.Start + edit.Length;
        }

        builder.Append(text, cursor, text.Length - cursor);
        if (truncated)
        {
            builder.Append('…');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 判定一段文本是否含敏感信息（供「敏感内容复制后自动清空剪贴板」使用，附加项 B-09）。
    /// <para>
    /// <b>与脱敏显示共用同一套规则与边界判定</b>，保证"显示上会被打码的内容"与"会被自动清空的内容"口径一致。
    /// 注意它是 <c>static</c>：<b>不受实例 <see cref="Enabled"/> 影响</b> —— 用户关掉「脱敏显示」不该让
    /// 自动清空失效，两者是彼此独立的开关。
    /// </para>
    /// </summary>
    /// <param name="input">待判定文本（可为 null）。</param>
    public static bool ContainsSensitive(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        // 与 Mask 一致：只扫描前 MaxScanChars 个字符（线性时间，避免超大文本拖慢）。
        var text = input.Length > MaxScanChars ? input[..MaxScanChars] : input;
        return CollectEdits(text) is { Count: > 0 };
    }

    /// <summary>收集脱敏区间 —— <see cref="Mask"/> 与 <see cref="ContainsSensitive"/> 唯一的规则执行处。</summary>
    private static List<MaskEdit>? CollectEdits(string text)
    {
        List<MaskEdit>? edits = null;
        foreach (var rule in Rules)
        {
            foreach (Match match in rule.Pattern.Matches(text))
            {
                if (Overlaps(edits, match.Index, match.Length))
                {
                    continue;
                }

                var replacement = rule.BuildReplacement(match, text);
                if (replacement is null)
                {
                    continue;
                }

                (edits ??= []).Add(new MaskEdit(match.Index, match.Length, replacement));
            }
        }

        return edits;
    }

    /// <summary>判定区间是否与已脱敏区间重叠（区间数很少，线性扫描足够）。</summary>
    private static bool Overlaps(List<MaskEdit>? edits, int start, int length)
    {
        if (edits is null)
        {
            return false;
        }

        var end = start + length;
        foreach (var edit in edits)
        {
            var editEnd = edit.Start + edit.Length;
            if (start < editEnd && edit.Start < end)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>要求匹配区间两侧都不是数字（用于纯数字类规则，替代无法用于非回溯引擎的断言）。</summary>
    private static bool IsDigitBoundary(string text, int start, int length)
    {
        var left = start - 1;
        if (left >= 0 && char.IsDigit(text[left]))
        {
            return false;
        }

        var right = start + length;
        return right >= text.Length || !char.IsDigit(text[right]);
    }

    /// <summary>要求匹配区间两侧都不是字母或数字（用于身份证 / 密钥类规则）。</summary>
    private static bool IsAlphaNumericBoundary(string text, int start, int length)
    {
        var left = start - 1;
        if (left >= 0 && char.IsLetterOrDigit(text[left]))
        {
            return false;
        }

        var right = start + length;
        return right >= text.Length || !char.IsLetterOrDigit(text[right]);
    }

    /// <summary>保留前若干位，其余用固定长度掩码替换。</summary>
    private static string PrefixMask(string value, int visiblePrefix)
    {
        var visible = Math.Min(Math.Max(visiblePrefix, 0), value.Length);
        return string.Concat(value.AsSpan(0, visible).ToString(), new string(MaskChar, MaskWidth));
    }

    /// <summary>银行卡号 Luhn 校验：显著降低「订单号 / 时间戳被误判为卡号」的概率。</summary>
    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleDigit = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i] - '0';
            if (digit is < 0 or > 9)
            {
                return false;
            }

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }

    private sealed record MaskRule(string Name, Regex Pattern, Func<Match, string, string?> BuildReplacement);

    private readonly record struct MaskEdit(int Start, int Length, string Replacement);

    /// <summary>
    /// 规则表。顺序即优先级；全部不含断言、回溯结构（NonBacktracking 要求）。
    /// </summary>
    private static readonly MaskRule[] Rules =
    [
        new MaskRule(
            "手机号",
            new Regex(@"1[3-9]\d{9}", RegexOptions.NonBacktracking),
            static (m, text) => IsDigitBoundary(text, m.Index, m.Length) ? PrefixMask(m.Value, 3) : null),

        new MaskRule(
            "身份证",
            new Regex(@"\d{17}[\dXx]", RegexOptions.NonBacktracking),
            static (m, text) => IsAlphaNumericBoundary(text, m.Index, m.Length) ? PrefixMask(m.Value, 3) : null),

        new MaskRule(
            "银行卡",
            new Regex(@"\d{13,19}", RegexOptions.NonBacktracking),
            static (m, text) => IsDigitBoundary(text, m.Index, m.Length) && PassesLuhn(m.Value)
                ? PrefixMask(m.Value, 4)
                : null),

        new MaskRule(
            "邮箱",
            new Regex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", RegexOptions.NonBacktracking),
            static (m, _) => MaskEmail(m.Value)),

        new MaskRule(
            "密钥令牌",
            new Regex(
                @"sk-[A-Za-z0-9_-]{8,}|ghp_[A-Za-z0-9]{20,}|eyJ[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}\.[A-Za-z0-9_-]{4,}",
                RegexOptions.NonBacktracking),
            static (m, _) => PrefixMask(m.Value, 3)),

        new MaskRule(
            "密码键值",
            new Regex(
                @"(密码|口令|password|passwd|pwd|token|secret|api[_-]?key)(\s*[:=：]\s*)(\S+)",
                RegexOptions.NonBacktracking | RegexOptions.IgnoreCase),
            static (m, _) =>
            {
                var value = m.Groups[3];
                return string.Concat(m.Value.AsSpan(0, value.Index - m.Index).ToString(), new string(MaskChar, MaskWidth));
            }),
    ];

    /// <summary>
    /// 邮箱脱敏：本地部分只保留首字符；域名保留顶级域
    /// （顶级域是两段式时一并保留，如 <c>co.uk</c> / <c>com.cn</c>，便于用户辨认）。
    /// </summary>
    private static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0)
        {
            return PrefixMask(email, 1);
        }

        var local = email[..at];
        var segments = email[(at + 1)..].Split('.');

        var keepFrom = segments.Length - 1;
        if (segments.Length >= 2 && segments[^1].Length <= 2)
        {
            keepFrom = segments.Length - 2;
        }

        var keptDomain = string.Join('.', segments[keepFrom..]);
        return string.Concat(
            local[0],
            new string(MaskChar, EmailLocalMaskWidth),
            "@",
            new string(MaskChar, EmailDomainMaskWidth),
            ".",
            keptDomain);
    }

    /// <summary>规则名（用于设置页预览与日志，不含用户数据）。</summary>
    public static IReadOnlyList<string> RuleNames { get; } = Rules.Select(static r => r.Name).ToArray();
}
