using System.IO;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Ui;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 附加项测试：B-01 点击面板外部自动隐藏（设置项）与 B-04 亚克力强度（映射 + 持久化）。
/// <para>
/// 面板失去激活后确实会隐藏这类行为依赖真实窗口，无法离屏断言，由实机操作核对（见 README）。
/// 这里覆盖的是纯逻辑：强度归一化、不透明度映射、颜色打包顺序、设置读写。
/// </para>
/// </summary>
public sealed class AddOnTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public AddOnTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 忽略清理失败。
        }
    }

    // ─────────────── B-04 亚克力强度映射 ───────────────

    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    [InlineData(100, 100)]
    [InlineData(999, 100)]
    public void 强度归一化到_0_100(int input, int expected) => Assert.Equal(expected, AcrylicTint.Normalize(input));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    public void 强度大于零才启用亚克力(int strength, bool expected) =>
        Assert.Equal(expected, AcrylicTint.IsEnabled(strength));

    [Theory]
    [InlineData(0, 100)]   // 关闭时完全不透明
    [InlineData(50, 82)]   // 半强度：保留 82% 底色
    [InlineData(100, 65)]  // 拉满也保留 65% 底色（按用户要求"别做那么透明"）
    public void 强度映射为底色不透明度(int strength, int expectedPercent) =>
        Assert.Equal(expectedPercent, AcrylicTint.OpacityPercent(strength));

    [Theory]
    [InlineData(0, 255)]
    [InlineData(100, 166)]
    public void 不透明度映射为_alpha_字节(int strength, int expectedAlpha) =>
        Assert.Equal(expectedAlpha, AcrylicTint.AlphaByte(strength));

    // ─────────────── 设置项（B-01 / B-04） ───────────────

    [Fact]
    public void 自动隐藏默认开启_亚克力默认关闭()
    {
        Assert.True(AppSettings.Default.HideOnClickOutside);
        Assert.Equal(0, AppSettings.Default.AcrylicStrength);
    }

    [Fact]
    public void 亚克力强度读取后自动收敛()
    {
        Assert.Equal(100, new AppSettings { AcrylicStrength = 150 }.Normalize().AcrylicStrength);
        Assert.Equal(0, new AppSettings { AcrylicStrength = -3 }.Normalize().AcrylicStrength);
        Assert.Equal(60, new AppSettings { AcrylicStrength = 60 }.Normalize().AcrylicStrength);
    }

    [Fact]
    public void 归一化幂等()
    {
        var once = new AppSettings { AcrylicStrength = 999, HideOnClickOutside = false }.Normalize();
        var twice = once.Normalize();

        Assert.Equal(once, twice);
    }

    [Fact]
    public void 相等比较包含新增字段()
    {
        var baseline = new AppSettings();

        Assert.NotEqual(baseline, baseline with { HideOnClickOutside = false });
        Assert.NotEqual(baseline, baseline with { AcrylicStrength = 30 });
    }

    [Fact]
    public void 新增字段可持久化并读回()
    {
        var store = new SettingsStore(_path);
        store.Load();

        var saved = new AppSettings { HideOnClickOutside = false, AcrylicStrength = 65, Theme = "dark" };
        Assert.True(store.TrySave(saved, out var error), error);

        var reloaded = new SettingsStore(_path).Load();
        Assert.False(reloaded.HideOnClickOutside);
        Assert.Equal(65, reloaded.AcrylicStrength);
        Assert.Equal("dark", reloaded.Theme);
    }
}
