using System.IO;
using ClipboardManager.Core.Html;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Settings;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 附加项 B-09「敏感内容复制后自动清空剪贴板」的测试：
/// 敏感判定（与脱敏显示同口径）、排定规则、设置项默认值与持久化。
/// </summary>
public sealed class SensitiveClearTests : IDisposable
{
    private readonly string _directory;

    public SensitiveClearTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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

    // ────────────────────── 敏感判定 ──────────────────────

    [Theory]
    [InlineData("联系我 13800138000")]
    [InlineData("110101199003071234")]                       // 身份证
    [InlineData("卡号 4111111111111111")]                    // 银行卡（Luhn 通过）
    [InlineData("邮箱 zhangsan@example.com")]
    [InlineData("token: sk-abcdefghijklmnopqrst")]           // 密钥令牌
    [InlineData("password: hunter2")]                        // 密码键值
    public void 含敏感信息的内容会被判定为敏感(string text)
    {
        Assert.True(SensitiveMasker.ContainsSensitive(text));
    }

    [Theory]
    [InlineData("hello 世界，这是一段普通文本")]
    [InlineData("订单号 9999999999999999")]                  // 16 位数字但 Luhn 不通过 → 不脱敏，也不清空
    [InlineData("")]
    [InlineData(null)]
    public void 普通内容不会被判定为敏感(string? text)
    {
        Assert.False(SensitiveMasker.ContainsSensitive(text));
    }

    [Fact]
    public void 判定不受脱敏显示开关影响()
    {
        // 判定是 static 的：关掉「脱敏显示」不该让「自动清空」失效（两个开关彼此独立）。
        var disabled = new SensitiveMasker(enabled: false);

        Assert.False(disabled.Enabled);
        Assert.Equal("13800138000", disabled.Mask("13800138000"));   // 显示层不脱敏
        Assert.True(SensitiveMasker.ContainsSensitive("13800138000")); // 判定照旧命中
    }

    // ────────────────────── 排定规则 ──────────────────────

    [Fact]
    public void 关闭档位时不排定()
    {
        var now = DateTimeOffset.Now;
        var candidate = TextCandidate("13800138000");

        Assert.Null(SensitiveClearPlanner.Plan(candidate, minutes: 0, sequence: 100, now));
        Assert.Null(SensitiveClearPlanner.Plan(candidate, minutes: -1, sequence: 100, now));
    }

    [Fact]
    public void 序列号未知时不排定()
    {
        var candidate = TextCandidate("13800138000");

        Assert.Null(SensitiveClearPlanner.Plan(candidate, minutes: 5, sequence: -1, DateTimeOffset.Now));
    }

    [Fact]
    public void 非敏感内容不排定()
    {
        Assert.Null(SensitiveClearPlanner.Plan(TextCandidate("今天天气不错"), minutes: 5, sequence: 100, DateTimeOffset.Now));
    }

    [Fact]
    public void 敏感内容按分钟数排定()
    {
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.FromHours(8));

        var plan = SensitiveClearPlanner.Plan(TextCandidate("手机 13800138000"), minutes: 5, sequence: 100, now);

        Assert.NotNull(plan);
        Assert.Equal(100, plan!.Sequence);
        Assert.Equal(5, plan.Minutes);
        Assert.Equal(now.AddMinutes(5), plan.Deadline);
    }

    [Fact]
    public void HTML内容按派生纯文本判定()
    {
        // 捕获路径：CaptureProcessor 已经把 HTML 的正文派生到 candidate.Text
        var derived = new ClipCandidate
        {
            Type = ClipContentType.Html,
            Text = "电话 13800138000",
            CapturedAt = DateTimeOffset.Now,
        };
        Assert.NotNull(SensitiveClearPlanner.Plan(derived, minutes: 2, sequence: 7, DateTimeOffset.Now));

        // 设置生效路径：直接读剪贴板拿到的还是原始 HTML（没有派生纯文本）→ 必须自己解析
        var raw = new ClipCandidate
        {
            Type = ClipContentType.Html,
            Binary = HtmlClipboardWriter.Build("<p>电话 13800138000</p>"),
            CapturedAt = DateTimeOffset.Now,
        };
        Assert.NotNull(SensitiveClearPlanner.Plan(raw, minutes: 2, sequence: 7, DateTimeOffset.Now));
    }

    [Fact]
    public void 图片与文件列表不参与判定()
    {
        var now = DateTimeOffset.Now;
        var image = new ClipCandidate
        {
            Type = ClipContentType.Image,
            Binary = new byte[32],
            CapturedAt = now,
        };
        var files = new ClipCandidate
        {
            Type = ClipContentType.FileList,
            FilePaths = [Path.Combine(_directory, "身份证.jpg")],
            CapturedAt = now,
        };

        // 没有可判定的文本：宁可不清，也不误判（文件名里出现"身份证"也不猜）。
        Assert.Null(SensitiveClearPlanner.Plan(image, minutes: 5, sequence: 100, now));
        Assert.Null(SensitiveClearPlanner.Plan(files, minutes: 5, sequence: 100, now));
    }

    private static ClipCandidate TextCandidate(string text) => new()
    {
        Type = ClipContentType.Text,
        Text = text,
        CapturedAt = DateTimeOffset.Now,
    };

    // ────────────────────── 设置项 ──────────────────────

    [Fact]
    public void 自动清空默认关闭()
    {
        Assert.Equal(0, AppSettings.Default.ClearSensitiveAfterMinutes);
        Assert.Equal(0, AppSettings.DefaultSensitiveClearMinutes);
        Assert.Contains(0, AppSettings.SensitiveClearMinutesOptions);
    }

    [Fact]
    public void 档位文案与取值一一对应()
    {
        // 设置窗口的下拉项由档位数组 + 文案函数生成（同一来源），这里锁住文案本身。
        Assert.Equal("关闭", AppSettings.SensitiveClearLabel(0));
        Assert.Equal("5 分钟", AppSettings.SensitiveClearLabel(5));
        Assert.Equal("30 分钟", AppSettings.SensitiveClearLabel(30));
        Assert.Equal(6, AppSettings.SensitiveClearMinutesOptions.Length);
        Assert.All(AppSettings.SensitiveClearMinutesOptions, m => Assert.False(string.IsNullOrWhiteSpace(AppSettings.SensitiveClearLabel(m))));
    }

    [Fact]
    public void 非法档位会被收敛到合法档位()
    {
        var normalized = (AppSettings.Default with { ClearSensitiveAfterMinutes = 777 }).Normalize();

        Assert.Contains(normalized.ClearSensitiveAfterMinutes, AppSettings.SensitiveClearMinutesOptions);
    }

    [Fact]
    public void 新字段参与相等性判定()
    {
        Assert.NotEqual(AppSettings.Default, AppSettings.Default with { ClearSensitiveAfterMinutes = 5 });
    }

    [Fact]
    public void 档位可持久化并读回()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new SettingsStore(path);

        Assert.True(store.TrySave(AppSettings.Default with { ClearSensitiveAfterMinutes = 10 }, out var error), error);

        Assert.Equal(10, new SettingsStore(path).Load().ClearSensitiveAfterMinutes);
    }

    [Fact]
    public void 旧设置文件缺少该字段时保持关闭()
    {
        // 默认值 0 == bool/int 的零值 → 老文件不需要 schema 迁移（与 clearClipboardOnDelete 不同）。
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            path,
            """{ "schemaVersion": 1, "maxItems": 50, "clearClipboardOnDelete": true }""");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(0, settings.ClearSensitiveAfterMinutes);
        Assert.True(settings.ClearClipboardOnDelete);   // 上一条迁移仍然生效
    }
}
