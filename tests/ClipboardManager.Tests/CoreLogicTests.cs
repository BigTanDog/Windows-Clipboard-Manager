using ClipboardManager.Core.Clipboard;
using ClipboardManager.Core.Hotkeys;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Settings;
using ClipboardManager.Core.Text;
using ClipboardManager.Core.Ui;

namespace ClipboardManager.Tests;

/// <summary>Core 纯逻辑测试（快捷键、摘要、相对时间、定位、优先级、设置校正）。</summary>
public class HotkeySpecTests
{
    [Fact]
    public void 解析并规范化()
    {
        Assert.True(HotkeySpec.TryParse("ctrl+shift+v", out var spec, out var error), error);
        Assert.NotNull(spec);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, spec.Modifiers);
        Assert.Equal((uint)'V', spec.VirtualKey);
        Assert.Equal("Ctrl+Shift+V", spec.ToString());
    }

    [Fact]
    public void 支持功能键与命名键()
    {
        Assert.True(HotkeySpec.TryParse("Alt+F4", out var f4, out _));
        Assert.Equal("Alt+F4", f4!.ToString());

        Assert.True(HotkeySpec.TryParse("Win+`", out var tilde, out _));
        Assert.Equal("Win+`", tilde!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("V")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Shift+V+X")]
    [InlineData("Ctrl+不存在的键")]
    public void 非法组合被拒绝(string input)
    {
        Assert.False(HotkeySpec.TryParse(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}

public class ContentHasherTests
{
    [Fact]
    public void 相同内容哈希一致且为64位小写十六进制()
    {
        var first = ContentHasher.ForText("hello 世界");
        var second = ContentHasher.ForText("hello 世界");
        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    [Fact]
    public void 不同内容哈希不同() =>
        Assert.NotEqual(ContentHasher.ForText("a"), ContentHasher.ForText("b"));

    [Fact]
    public void 文件列表顺序不同则哈希不同()
    {
        var first = ContentHasher.ForFileList(["C:\\a.txt", "C:\\b.txt"]);
        var second = ContentHasher.ForFileList(["C:\\b.txt", "C:\\a.txt"]);
        Assert.NotEqual(first, second);
    }
}

public class PreviewBuilderTests
{
    [Fact]
    public void 折叠换行与连续空白()
    {
        Assert.Equal("第一行 第二行 第三行", PreviewBuilder.ForText("第一行\r\n  第二行\n\n  第三行"));
    }

    [Fact]
    public void 超长截断并加省略号()
    {
        var result = PreviewBuilder.ForText(new string('字', 300));
        Assert.Equal(PreviewBuilder.MaxPreviewChars + 1, result.Length);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 文件列表只显示文件名()
    {
        var result = PreviewBuilder.ForFileList(["C:\\Users\\someone\\secret\\报告.pptx", "D:\\b.png"]);
        Assert.Equal("2 个文件：报告.pptx  b.png", result);
        Assert.DoesNotContain("Users", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 空输入安全()
    {
        Assert.Equal(string.Empty, PreviewBuilder.ForText(null));
        Assert.Equal(string.Empty, PreviewBuilder.ForText("   "));
        Assert.Equal(string.Empty, PreviewBuilder.ForFileList([]));
    }

    [Theory]
    [InlineData(135, "8×8 · 135 B")]
    [InlineData(2048, "8×8 · 2 KB")]
    [InlineData(3 * 1024 * 1024, "8×8 · 3 MB")]
    public void 图片摘要按体积选单位(long sizeBytes, string expected) =>
        Assert.Equal(expected, PreviewBuilder.ForImage(8, 8, sizeBytes));

    [Theory]
    [InlineData(ClipContentType.Text, "文本")]
    [InlineData(ClipContentType.Html, "HTML")]
    [InlineData(ClipContentType.Image, "图片")]
    [InlineData(ClipContentType.FileList, "文件")]
    public void 类型标签(ClipContentType type, string expected) =>
        Assert.Equal(expected, PreviewBuilder.TypeLabel(type));
}

public class RelativeTimeTests
{
    [Fact]
    public void 一分钟内显示刚刚() =>
        Assert.Equal("刚刚", RelativeTime.Format(DateTimeOffset.Now.AddSeconds(-30), DateTimeOffset.Now));

    [Fact]
    public void 分钟与小时()
    {
        // 用固定的当地中午：跨零点跑测试时 now.AddHours(-3) 会落到昨天，
        // 文案变成「昨天 HH:mm」，导致断言随运行时刻随机失败（2026-09-19 实测踩到）。
        var now = new DateTimeOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Local));
        Assert.Equal("5 分钟前", RelativeTime.Format(now.AddMinutes(-5), now));
        Assert.Equal("3 小时前", RelativeTime.Format(now.AddHours(-3), now));
    }

    [Fact]
    public void 昨天带时分()
    {
        var now = DateTimeOffset.Now;
        var yesterday = now.AddDays(-1);
        Assert.Equal("昨天 " + yesterday.ToLocalTime().ToString("HH:mm"), RelativeTime.Format(yesterday, now));
    }

    [Fact]
    public void 未来时间按刚刚处理() =>
        Assert.Equal("刚刚", RelativeTime.Format(DateTimeOffset.Now.AddMinutes(10), DateTimeOffset.Now));
}

public class PanelPositioningTests
{
    [Fact]
    public void 缩放百分百时的右下角定位()
    {
        var (left, top) = PanelPositioning.ComputeBottomRight(1920, 1040, 420, 460, 96);
        Assert.Equal(1488, left, 3);
        Assert.Equal(568, top, 3);
    }

    [Fact]
    public void 缩放百分之一百五十时换算为DIP()
    {
        // 150% 缩放：工作区 1920x1040 物理像素 → 1280x693.33 DIP
        var (left, top) = PanelPositioning.ComputeBottomRight(1920, 1040, 420, 460, 144);
        Assert.Equal(848, left, 3);
        Assert.Equal(221.333, top, 3);
    }

    [Fact]
    public void 负坐标副屏也能算出负值()
    {
        var (left, _) = PanelPositioning.ComputeBottomRight(0, 1040, 420, 460, 96);
        Assert.Equal(-432, left, 3);
    }
}

public class ClipboardFormatPriorityTests
{
    [Fact]
    public void 按需求顺序取优先级最高者()
    {
        Assert.Equal(ClipContentType.FileList, ClipboardFormatPriority.Pick([ClipContentType.Text, ClipContentType.FileList]));
        Assert.Equal(ClipContentType.Image, ClipboardFormatPriority.Pick([ClipContentType.Text, ClipContentType.Image]));
        Assert.Equal(ClipContentType.Html, ClipboardFormatPriority.Pick([ClipContentType.Text, ClipContentType.Html]));
        Assert.Equal(ClipContentType.Text, ClipboardFormatPriority.Pick([ClipContentType.Text]));
        Assert.Null(ClipboardFormatPriority.Pick([]));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void 非法档位收敛到最近合法值()
    {
        var settings = new AppSettings { MaxItems = 137, DiskQuotaMb = 900 }.Normalize();
        Assert.Equal(100, settings.MaxItems);
        Assert.Equal(1024, settings.DiskQuotaMb);
    }

    [Fact]
    public void 合法档位保持不变()
    {
        var settings = new AppSettings { MaxItems = -1, DiskQuotaMb = 2048 }.Normalize();
        Assert.Equal(-1, settings.MaxItems);
        Assert.Equal(2048, settings.DiskQuotaMb);
    }

    [Fact]
    public void 主题与热键兜底()
    {
        var settings = new AppSettings { Theme = "Blue", Hotkey = "   " }.Normalize();
        Assert.Equal("system", settings.Theme);
        Assert.Equal(AppSettings.DefaultHotkey, settings.Hotkey);
    }

    [Fact]
    public void 排除应用去空白去重()
    {
        var settings = new AppSettings { ExcludedApps = [" a.exe ", "A.EXE", "  ", "b.exe"] }.Normalize();
        Assert.Equal(["a.exe", "b.exe"], settings.ExcludedApps);
    }

    [Fact]
    public void 默认脱敏开启且幂等()
    {
        Assert.True(AppSettings.Default.MaskSensitiveData);
        var once = new AppSettings().Normalize();
        Assert.Equal(once, once.Normalize());
    }
}
