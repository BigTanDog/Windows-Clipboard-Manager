using ClipboardManager.Core.AutoStart;

namespace ClipboardManager.Core.Settings;

/// <summary>
/// 用户设置（持久化于 <c>data/settings.json</c>）。
/// 所有字段在读取后都必须经 <see cref="Normalize"/> 校正，避免手工改坏文件导致异常。
/// </summary>
public sealed record AppSettings
{
    /// <summary>设置文件结构版本（用于将来迁移）。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>记录条数上限的可选档位（-1 表示不限制）。</summary>
    public static readonly int[] MaxItemsOptions = [50, 100, 200, 500, -1];

    /// <summary>磁盘占用上限（MB）的可选档位（-1 表示不限制）。</summary>
    public static readonly int[] DiskQuotaMbOptions = [200, 500, 1024, 2048, -1];

    /// <summary>默认条数上限（需求 §3.3）。</summary>
    public const int DefaultMaxItems = 100;

    /// <summary>默认磁盘上限 MB（产品计划 D-09）。</summary>
    public const int DefaultDiskQuotaMb = 500;

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
        foreach (var app in ExcludedApps)
        {
            hash.Add(app, StringComparer.OrdinalIgnoreCase);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// 把非法值收敛到合法范围：档位不在候选表内取最接近的合法档位，
    /// 主题/热键格式非法则回退默认值。保证 <see cref="Normalize"/> 幂等。
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
            AutoStartDelaySeconds = Math.Clamp(AutoStartDelaySeconds, 0, AutoStartCommand.MaxDelaySeconds),
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
