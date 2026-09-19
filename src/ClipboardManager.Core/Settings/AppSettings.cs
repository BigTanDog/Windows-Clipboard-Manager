using ClipboardManager.Core.AutoStart;
using ClipboardManager.Core.Ui;

namespace ClipboardManager.Core.Settings;

/// <summary>
/// 用户设置（持久化于 <c>data/settings.json</c>）。
/// 所有字段在读取后都必须经 <see cref="Normalize"/> 校正，避免手工改坏文件导致异常。
/// </summary>
public sealed record AppSettings
{
    /// <summary>
    /// 设置文件结构版本（迁移用）。
    /// <para>
    /// 版本 2（2026-09-19）：新增 <see cref="ClearClipboardOnDelete"/>。
    /// </para>
    /// <para>
    /// <b>为什么新增"默认开启"的开关必须升版本号</b>：本项目的设置用源生成 JSON 反序列化，
    /// 文件里<b>缺字段</b>时得到的是该类型的零值（<c>bool</c> → <c>false</c>），
    /// <b>不是</b>属性声明上的初始值。实测（<c>ClipboardRevokeTests.旧设置文件缺少新字段时取默认值</c>）确认：
    /// 若不做迁移，老用户升级后新功能会静默变成"关闭"，而且界面上看不出来。
    /// 所以：新增默认开启的开关 = 升 <see cref="CurrentSchemaVersion"/> + 在
    /// <see cref="Normalize"/> 里按旧版本号补上默认值。
    /// </para>
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>引入 <see cref="ClearClipboardOnDelete"/> 之前的设置文件版本（低于此版本视为"没这个字段"）。</summary>
    private const int SchemaVersionBeforeClearClipboardOnDelete = 2;

    /// <summary>记录条数上限的可选档位（-1 表示不限制）。</summary>
    public static readonly int[] MaxItemsOptions = [50, 100, 200, 500, -1];

    /// <summary>磁盘占用上限（MB）的可选档位（-1 表示不限制）。</summary>
    public static readonly int[] DiskQuotaMbOptions = [200, 500, 1024, 2048, -1];

    /// <summary>敏感内容自动清空剪贴板的档位（分钟；0 = 关闭，附加项 B-09）。</summary>
    public static readonly int[] SensitiveClearMinutesOptions = [0, 1, 2, 5, 10, 30];

    /// <summary>
    /// 档位的中文文案（设置窗口的下拉项由此生成，保证「文案 ↔ 取值」永远一一对应，不会因两边各写一份而错位）。
    /// </summary>
    /// <param name="minutes">档位分钟数（&lt;= 0 表示关闭）。</param>
    public static string SensitiveClearLabel(int minutes) => minutes <= 0 ? "关闭" : $"{minutes} 分钟";

    /// <summary>默认条数上限（需求 §3.3）。</summary>
    public const int DefaultMaxItems = 100;

    /// <summary>默认磁盘上限 MB（产品计划 D-09）。</summary>
    public const int DefaultDiskQuotaMb = 500;

    /// <summary>敏感内容自动清空的默认档位（0 = 关闭；附加项 B-09 用户要求默认关闭）。</summary>
    public const int DefaultSensitiveClearMinutes = 0;

    /// <summary>默认热键（需求 §3.4）。</summary>
    public const string DefaultHotkey = "Ctrl+Shift+V";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>记录条数上限，-1 表示不限制。</summary>
    public int MaxItems { get; init; } = DefaultMaxItems;

    /// <summary>磁盘占用上限（MB），-1 表示不限制。</summary>
    public int DiskQuotaMb { get; init; } = DefaultDiskQuotaMb;

    /// <summary>唤出面板的全局热键。</summary>
    public string Hotkey { get; init; } = DefaultHotkey;

    /// <summary>粘贴时是否自动向先前的前台窗口发送 Ctrl+V。</summary>
    public bool AutoPaste { get; init; } = true;

    /// <summary>是否开机自启（写 HKCU Run）。</summary>
    public bool AutoStart { get; init; }

    /// <summary>开机自启的延迟秒数（0 = 不延迟；需求 §3.5「可选延迟启动，避免拖慢开机」）。</summary>
    public int AutoStartDelaySeconds { get; init; }

    /// <summary>排除的应用（进程名，不含路径）。</summary>
    public IReadOnlyList<string> ExcludedApps { get; init; } = [];

    /// <summary>是否启用「按来源进程名排除」辅助过滤。</summary>
    public bool ExcludeByProcessName { get; init; } = true;

    /// <summary>是否记录图片。</summary>
    public bool CaptureImages { get; init; } = true;

    /// <summary>主题：system / light / dark。</summary>
    public string Theme { get; init; } = "system";

    /// <summary>是否对敏感信息做脱敏显示（产品计划 D-13，默认开启）。</summary>
    public bool MaskSensitiveData { get; init; } = true;

    /// <summary>
    /// 敏感内容复制后自动清空剪贴板的分钟数（**0 = 关闭，默认关闭**；附加项 B-09）。
    /// <para>
    /// 开启后：复制到含敏感信息（手机号 / 身份证 / 银行卡 / 邮箱 / 密钥 / 密码键值）的文本时，
    /// 过 N 分钟把<b>系统剪贴板</b>清掉。清空前用序列号复核，期间你又复制了别的内容则什么都不会发生。
    /// 判定口径与「脱敏显示」共用同一套规则，但两个开关彼此独立。
    /// </para>
    /// <para>档位见 <see cref="SensitiveClearMinutesOptions"/>；识别不到敏感信息的类型（图片 / 文件）不生效。</para>
    /// <para>
    /// 注意：本字段默认值是 0（关闭），与类型零值一致 —— 因此<b>不需要</b>像
    /// <see cref="ClearClipboardOnDelete"/> 那样做 schema 迁移（老设置文件里缺这个字段时读到的 0 正是期望值）。
    /// </para>
    /// </summary>
    public int ClearSensitiveAfterMinutes { get; init; }

    /// <summary>
    /// 点击面板外部（面板失去激活）时自动隐藏（附加项 B-01，默认开启）。
    /// <para>右键菜单打开期间不触发，避免菜单刚弹出面板就被收起。</para>
    /// </summary>
    public bool HideOnClickOutside { get; init; } = true;

    /// <summary>
    /// 亚克力强度 0–100（附加项 B-04）：0 = 不透明（关闭），数值越大越透、模糊越明显。
    /// </summary>
    public int AcrylicStrength { get; init; }

    /// <summary>
    /// 删除历史记录时，如果它正是当前系统剪贴板里的内容，就一并清空剪贴板（默认开启）。
    /// <para>
    /// 效果：删除后桌面右键的「粘贴」立刻变灰，不会再误粘贴出已删掉的内容。
    /// 只有「内容确实是我们这一条」时才清（判定与清空在剪贴板锁内原子完成）；
    /// 剪贴板已换成别的内容时<strong>绝不动它</strong>。
    /// </para>
    /// <para>
    /// 边界：只影响系统当前剪贴板 —— Windows 剪贴板历史（Win+V）里的副本、
    /// 已经粘贴到别处的内容、其它剪贴板管理器的数据库都管不到（系统没有按条目删除的接口）。
    /// </para>
    /// </summary>
    public bool ClearClipboardOnDelete { get; init; } = true;

    /// <summary>
    /// 单击条目是否直接粘贴并收起面板。
    /// <para>
    /// 默认 <c>false</c>：<b>单击只选中</b>，粘贴交给双击或 <c>Enter</c> —— 避免"只想选中看看"时
    /// 面板被立刻收起（用户反馈）；设为 <c>true</c> 恢复原来的单击即粘贴行为。
    /// </para>
    /// </summary>
    public bool SingleClickPaste { get; init; }

    /// <summary>默认设置。</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>
    /// 值相等比较。
    /// <para>
    /// 必须显式实现：record 自动生成的比较对 <see cref="ExcludedApps"/> 这类数组字段
    /// 用的是引用相等，会导致「内容相同但不相等」的隐蔽 bug（例如判断设置是否被修改）。
    /// </para>
    /// </summary>
    public bool Equals(AppSettings? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return SchemaVersion == other.SchemaVersion
            && MaxItems == other.MaxItems
            && DiskQuotaMb == other.DiskQuotaMb
            && string.Equals(Hotkey, other.Hotkey, StringComparison.Ordinal)
            && AutoPaste == other.AutoPaste
            && AutoStart == other.AutoStart
            && AutoStartDelaySeconds == other.AutoStartDelaySeconds
            && ExcludeByProcessName == other.ExcludeByProcessName
            && CaptureImages == other.CaptureImages
            && string.Equals(Theme, other.Theme, StringComparison.Ordinal)
            && MaskSensitiveData == other.MaskSensitiveData
            && HideOnClickOutside == other.HideOnClickOutside
            && AcrylicStrength == other.AcrylicStrength
            && SingleClickPaste == other.SingleClickPaste
            && ClearClipboardOnDelete == other.ClearClipboardOnDelete
            && ClearSensitiveAfterMinutes == other.ClearSensitiveAfterMinutes
            && ExcludedApps.SequenceEqual(other.ExcludedApps, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(MaxItems);
        hash.Add(DiskQuotaMb);
        hash.Add(Hotkey, StringComparer.Ordinal);
        hash.Add(AutoPaste);
        hash.Add(AutoStart);
        hash.Add(AutoStartDelaySeconds);
        hash.Add(ExcludeByProcessName);
        hash.Add(CaptureImages);
        hash.Add(Theme, StringComparer.Ordinal);
        hash.Add(MaskSensitiveData);
        hash.Add(HideOnClickOutside);
        hash.Add(AcrylicStrength);
        hash.Add(SingleClickPaste);
        hash.Add(ClearClipboardOnDelete);
        hash.Add(ClearSensitiveAfterMinutes);
        foreach (var app in ExcludedApps)
        {
            hash.Add(app, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// 把非法值收敛到合法范围：档位不在候选表内取最接近的合法档位，
    /// 主题/热键格式非法则回退默认值；并按 <see cref="SchemaVersion"/> 补齐新增开关的默认值。
    /// 保证 <see cref="Normalize"/> 幂等。
    /// </summary>
    public AppSettings Normalize()
    {
        var theme = Theme?.Trim().ToLowerInvariant();
        if (theme is not ("system" or "light" or "dark"))
        {
            theme = "system";
        }

        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            MaxItems = NormalizeOption(MaxItems, MaxItemsOptions, DefaultMaxItems),
            DiskQuotaMb = NormalizeOption(DiskQuotaMb, DiskQuotaMbOptions, DefaultDiskQuotaMb),
            Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? DefaultHotkey : Hotkey.Trim(),
            Theme = theme,

            // 迁移：v2 之前的设置文件里没有 clearClipboardOnDelete 字段，
            // 反序列化只会给出默认值 false —— 这里按「当时的设计默认值（开启）」补上。
            ClearClipboardOnDelete = SchemaVersion < SchemaVersionBeforeClearClipboardOnDelete
                ? true
                : ClearClipboardOnDelete,

            // B-09 的档位默认 0（关闭），与类型零值一致 → 老文件缺字段时读到的 0 就是期望值，
            // 不需要迁移（这条注释是刻意留的：下次新增「默认关闭」的字段可以照此判断）。
            ClearSensitiveAfterMinutes = NormalizeOption(
                ClearSensitiveAfterMinutes,
                SensitiveClearMinutesOptions,
                DefaultSensitiveClearMinutes),
            AutoStartDelaySeconds = Math.Clamp(AutoStartDelaySeconds, 0, AutoStartCommand.MaxDelaySeconds),
            AcrylicStrength = AcrylicTint.Normalize(AcrylicStrength),
            ExcludedApps = ExcludedApps?
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [],
        };
    }

    /// <summary>
    /// 把任意整数收敛到给定的档位表：命中则原样返回，否则取绝对值最接近的档位。
    /// </summary>
    private static int NormalizeOption(int value, int[] options, int fallback)
    {
        if (options.Contains(value))
        {
            return value;
        }

        var best = options[0];
        var bestDistance = long.MaxValue;
        foreach (var option in options)
        {
            var distance = Math.Abs((long)option - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = option;
            }
        }

        return bestDistance == long.MaxValue ? fallback : best;
    }
}
