using ClipboardManager.Core.AutoStart;
using ClipboardManager.Core.Imaging;
using ClipboardManager.Core.Retention;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Theme;
using ClipboardManager.Core.Ui;

namespace ClipboardManager.Tests;

/// <summary>淘汰策略测试（需求 §3.3 + 产品计划 D-09）。全部为纯函数用例，不碰数据库与磁盘。</summary>
public class RetentionPlannerTests
{
    [Fact]
    public void 条数上限_淘汰最旧的非收藏()
    {
        var items = new[]
        {
            Candidate(1, updatedAt: 100),
            Candidate(2, updatedAt: 200),
            Candidate(3, updatedAt: 300),
        };

        var doomed = RetentionPlanner.PlanByCount(items, 2);

        Assert.Equal([1], doomed);
    }

    [Fact]
    public void 条数上限_收藏不占额度也不被淘汰()
    {
        var items = new[]
        {
            Candidate(1, pinned: true, updatedAt: 10),   // 最旧，但收藏
            Candidate(2, updatedAt: 20),
            Candidate(3, updatedAt: 30),
            Candidate(4, updatedAt: 40),
        };

        var doomed = RetentionPlanner.PlanByCount(items, 2);

        // 非收藏有 2/3/4，保留最新两条（3、4），淘汰 2；收藏的 1 不动
        Assert.Equal([2], doomed);
    }

    [Fact]
    public void 条数上限_不限制时返回空()
    {
        var items = new[] { Candidate(1, updatedAt: 1), Candidate(2, updatedAt: 2) };

        Assert.Empty(RetentionPlanner.PlanByCount(items, -1));
        Assert.Empty(RetentionPlanner.PlanByCount(items, 5));
    }

    [Fact]
    public void 磁盘上限_未超限不淘汰()
    {
        var items = new[] { Candidate(1, size: 100), Candidate(2, size: 200) };

        var plan = RetentionPlanner.PlanByDiskQuota(items, 1000);

        Assert.Empty(plan.EvictIds);
        Assert.Equal(300, plan.TotalBytes);
        Assert.False(plan.OverQuotaAfterEviction);
    }

    [Fact]
    public void 磁盘上限_优先淘汰体积最大者()
    {
        var items = new[]
        {
            Candidate(1, size: 100, updatedAt: 10),
            Candidate(2, size: 900, updatedAt: 20),   // 最大 → 先淘汰
            Candidate(3, size: 200, updatedAt: 30),
        };

        // 合计 1200，上限 1000 → 淘汰 2（900）后剩 300，达成
        var plan = RetentionPlanner.PlanByDiskQuota(items, 1000);

        Assert.Equal([2], plan.EvictIds);
        Assert.False(plan.OverQuotaAfterEviction);
    }

    [Fact]
    public void 磁盘上限_同级按最旧优先()
    {
        var items = new[]
        {
            Candidate(1, size: 400, updatedAt: 10),
            Candidate(2, size: 400, updatedAt: 20),
            Candidate(3, size: 400, updatedAt: 30),
        };

        // 合计 1200，上限 600 → 需淘汰 600，即两条；同体积按最旧先删
        var plan = RetentionPlanner.PlanByDiskQuota(items, 600);

        Assert.Equal([1, 2], plan.EvictIds);
        Assert.False(plan.OverQuotaAfterEviction);
    }

    [Fact]
    public void 磁盘上限_收藏永不淘汰_只剩收藏超限时给标记()
    {
        var items = new[]
        {
            Candidate(1, pinned: true, size: 800, updatedAt: 10),
            Candidate(2, size: 300, updatedAt: 20),
        };

        var plan = RetentionPlanner.PlanByDiskQuota(items, 500);

        // 非收藏的 2 会被淘汰，但收藏的 800 仍超过 500 → 必须提示用户而不是继续删
        Assert.Equal([2], plan.EvictIds);
        Assert.True(plan.OverQuotaAfterEviction);
        Assert.Equal(800, plan.PinnedBytes);
        Assert.Equal(1100, plan.TotalBytes);
    }

    [Fact]
    public void 磁盘上限_不限制时不淘汰()
    {
        var items = new[] { Candidate(1, size: 10_000_000) };

        Assert.Empty(RetentionPlanner.PlanByDiskQuota(items, -1).EvictIds);
    }

    [Fact]
    public void 体积为负的脏数据按零处理()
    {
        var items = new[] { Candidate(1, size: -5), Candidate(2, size: 100) };

        var plan = RetentionPlanner.PlanByDiskQuota(items, 50);

        Assert.Equal(100, plan.TotalBytes);
        Assert.Contains(2L, plan.EvictIds);
    }

    private static RetentionCandidate Candidate(
        long id,
        bool pinned = false,
        long size = 0,
        long updatedAt = 0) => new(id, pinned, size, updatedAt);
}

/// <summary>主题解析测试（需求 §2：跟随系统；设置三档）。</summary>
public class ThemeResolverTests
{
    [Theory]
    [InlineData("system", true, ThemeResolver.Light)]
    [InlineData("system", false, ThemeResolver.Dark)]
    [InlineData("light", false, ThemeResolver.Light)]
    [InlineData("dark", true, ThemeResolver.Dark)]
    [InlineData("LIGHT", false, ThemeResolver.Light)]
    [InlineData(" dark ", true, ThemeResolver.Dark)]
    [InlineData(null, false, ThemeResolver.Dark)]
    [InlineData("未知值", true, ThemeResolver.Light)]
    public void 解析主题(string? setting, bool systemLight, string expected) =>
        Assert.Equal(expected, ThemeResolver.Resolve(setting, systemLight));

    [Theory]
    [InlineData("system", true)]
    [InlineData("light", true)]
    [InlineData("dark", true)]
    [InlineData("", false)]
    [InlineData("blue", false)]
    public void 设置值合法性(string? setting, bool expected) =>
        Assert.Equal(expected, ThemeResolver.IsValidSetting(setting));
}

/// <summary>开机自启命令行测试（需求 §3.5）。</summary>
public class AutoStartCommandTests
{
    [Fact]
    public void 路径含空格时加引号()
    {
        var command = AutoStartCommand.Build(@"C:\Program Files\Clipboard Manager\ClipboardManager.exe", 0);

        Assert.Equal(@"""C:\Program Files\Clipboard Manager\ClipboardManager.exe""", command);
        Assert.StartsWith("\"", command, StringComparison.Ordinal);
        Assert.EndsWith("\"", command, StringComparison.Ordinal);
    }

    [Fact]
    public void 带延迟时附加参数()
    {
        var command = AutoStartCommand.Build(@"D:\app\ClipboardManager.exe", 15);

        Assert.Equal(@"""D:\app\ClipboardManager.exe"" --delay=15", command);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(999, AutoStartCommand.MaxDelaySeconds)]
    public void 延迟值被夹紧(int input, int expected)
    {
        var command = AutoStartCommand.Build("app.exe", input);

        if (expected == 0)
        {
            Assert.DoesNotContain("--delay", command, StringComparison.Ordinal);
        }
        else
        {
            Assert.EndsWith($"--delay={expected}", command, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("--delay=30", 30)]
    [InlineData("--delay=1", 1)]
    [InlineData("--delay=0", 0)]
    [InlineData("--delay=-1", 0)]
    [InlineData("--delay=999", 0)]
    [InlineData("--delay=abc", 0)]
    [InlineData("--delay=", 0)]
    [InlineData("--diag", 0)]
    public void 解析延迟参数(string argument, int expected) =>
        Assert.Equal(expected, AutoStartCommand.ParseDelay([argument, "--diag"]));

    [Fact]
    public void 无参数时零延迟()
    {
        Assert.Equal(0, AutoStartCommand.ParseDelay(null));
        Assert.Equal(0, AutoStartCommand.ParseDelay([]));
    }

    [Fact]
    public void 空路径抛异常() =>
        Assert.Throws<ArgumentException>(() => AutoStartCommand.Build("   ", 0));
}

/// <summary>ICO 字节布局测试（托盘图标运行时生成）。</summary>
public class IcoWriterTests
{
    [Fact]
    public void 单尺寸布局正确()
    {
        byte[] png = [1, 2, 3, 4, 5];

        var ico = IcoWriter.BuildSingle(16, png);

        Assert.Equal(6 + 16 + png.Length, ico.Length);
        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));            // reserved
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));            // type = icon
        Assert.Equal(1, BitConverter.ToUInt16(ico, 4));            // count

        var entry = 6;
        Assert.Equal(16, ico[entry]);                              // width
        Assert.Equal(16, ico[entry + 1]);                          // height
        Assert.Equal(1, BitConverter.ToUInt16(ico, entry + 4));    // planes
        Assert.Equal(32, BitConverter.ToUInt16(ico, entry + 6));   // bit count
        Assert.Equal((uint)png.Length, BitConverter.ToUInt32(ico, entry + 8));
        Assert.Equal((uint)(6 + 16), BitConverter.ToUInt32(ico, entry + 12));
        Assert.Equal(png, ico[(6 + 16)..]);
    }

    [Fact]
    public void 尺寸256按规范写0()
    {
        var ico = IcoWriter.BuildSingle(256, [9]);

        Assert.Equal(0, ico[6]);
        Assert.Equal(0, ico[7]);
    }

    [Fact]
    public void 多尺寸时偏移连续()
    {
        var ico = IcoWriter.Build([new IcoWriter.IcoImage(16, [1, 1]), new IcoWriter.IcoImage(32, [2, 2, 2])]);

        Assert.Equal(2, BitConverter.ToUInt16(ico, 4));
        var first = BitConverter.ToUInt32(ico, 6 + 12);
        var second = BitConverter.ToUInt32(ico, 6 + 16 + 12);

        Assert.Equal((uint)(6 + 32), first);
        Assert.Equal(first + 2, second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void 非法尺寸抛异常(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IcoWriter.BuildSingle(size, [1]));

    [Fact]
    public void 空输入抛异常()
    {
        Assert.Throws<ArgumentException>(() => IcoWriter.Build([]));
        Assert.Throws<ArgumentException>(() => IcoWriter.BuildSingle(16, []));
    }

    [Fact]
    public void 取出最接近期望尺寸的图像()
    {
        byte[] png16 = [1, 0, 1];
        byte[] png32 = [3, 2, 3, 4];
        var ico = IcoWriter.Build([new IcoWriter.IcoImage(16, png16), new IcoWriter.IcoImage(32, png32)]);

        Assert.True(IcoWriter.TryGetImage(ico, 16, out var small, out var smallSize));
        Assert.Equal(16, smallSize);
        Assert.Equal(png16, small);

        Assert.True(IcoWriter.TryGetImage(ico, 32, out var large, out var largeSize));
        Assert.Equal(32, largeSize);
        Assert.Equal(png32, large);

        // 期望 30 → 与 32 更近（|30-32|=2 < |30-16|=14）
        Assert.True(IcoWriter.TryGetImage(ico, 30, out _, out var nearest));
        Assert.Equal(32, nearest);

        // 恰好等距（24）时保留先出现的较小图（避免不必要地放大）
        Assert.True(IcoWriter.TryGetImage(ico, 24, out _, out var tieBreak));
        Assert.Equal(16, tieBreak);
    }

    [Fact]
    public void 越界偏移的图像被跳过()
    {
        var ico = IcoWriter.BuildSingle(16, [9, 9]);
        // 把数据长度改大，使偏移 + 长度越界
        BitConverter.GetBytes(999u).CopyTo(ico, 6 + 8);

        Assert.False(IcoWriter.TryGetImage(ico, 16, out _, out _));
    }

    [Fact]
    public void 非法输入返回false()
    {
        Assert.False(IcoWriter.TryGetImage([], 16, out _, out _));
        Assert.False(IcoWriter.TryGetImage([1, 2, 3], 16, out _, out _));
    }
}

/// <summary>托盘回调参数解析测试（NOTIFYICON_VERSION_4 的位运算规则）。</summary>
public class TrayNotificationTests
{
    [Fact]
    public void 解析事件与图标id()
    {
        // 事件在低位字、图标 id 在高位字
        var lParam = new IntPtr((5 << 16) | 0x0205);

        Assert.Equal(0x0205u, TrayNotification.ResolveEvent(lParam));
        Assert.Equal(5u, TrayNotification.ResolveIconId(lParam));
    }

    [Fact]
    public void 解析鼠标坐标_支持负坐标多屏()
    {
        const int x = 1920;
        const int y = -120; // 副屏在主屏上方时为负
        var wParam = new IntPtr(((y & 0xFFFF) << 16) | (x & 0xFFFF));

        Assert.Equal(x, TrayNotification.ResolveX(wParam));
        Assert.Equal(y, TrayNotification.ResolveY(wParam));
    }

    [Fact]
    public void 零值安全()
    {
        Assert.Equal(0u, TrayNotification.ResolveEvent(IntPtr.Zero));
        Assert.Equal(0, TrayNotification.ResolveX(IntPtr.Zero));
        Assert.Equal(0, TrayNotification.ResolveY(IntPtr.Zero));
    }
}

/// <summary>设置项：延迟自启字段的收敛与相等性。</summary>
public class AppSettingsPhase3Tests
{
    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(999, AutoStartCommand.MaxDelaySeconds)]
    public void 延迟秒数被夹紧(int input, int expected)
    {
        var normalized = (AppSettings.Default with { AutoStartDelaySeconds = input }).Normalize();

        Assert.Equal(expected, normalized.AutoStartDelaySeconds);
    }

    [Fact]
    public void 相等性包含延迟字段()
    {
        var a = AppSettings.Default with { AutoStartDelaySeconds = 10 };
        var b = AppSettings.Default with { AutoStartDelaySeconds = 20 };

        Assert.NotEqual(a, b);
        Assert.Equal(a, a with { });
        Assert.Equal(a.GetHashCode(), (a with { }).GetHashCode());
    }

    [Fact]
    public void 新增字段不破坏既有默认值()
    {
        var settings = AppSettings.Default.Normalize();

        Assert.Equal(AppSettings.DefaultMaxItems, settings.MaxItems);
        Assert.Equal(AppSettings.DefaultDiskQuotaMb, settings.DiskQuotaMb);
        Assert.Equal(AppSettings.DefaultHotkey, settings.Hotkey);
        Assert.True(settings.MaskSensitiveData);
        Assert.True(settings.CaptureImages);
    }
}
