using ClipboardManager.App;
using ClipboardManager.App.Imaging;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Core.Imaging;
using ClipboardManager.Core.Models;
using ClipboardManager.Core.Sensitive;
using ClipboardManager.Core.Text;

namespace ClipboardManager.Tests;

/// <summary>
/// 外观与展示层测试（图标、类型标识、体积文案）。
/// <para>
/// 这些逻辑都是纯函数或纯资源读取，适合直接断言；窗口几何（圆角/标题栏）无法离屏验证，
/// 由实机截图核对（见 README「验收对照」）。
/// </para>
/// </summary>
public class AppearanceTests
{
    // ─────────────── 网址识别（复制的是网站时单独做标识） ───────────────

    [Theory]
    [InlineData("https://www.bilibili.com/video/BV1pQYH6XEZy/")]
    [InlineData("http://example.com")]
    [InlineData("www.example.com")]
    [InlineData("HTTPS://EXAMPLE.COM/PATH?A=1")]
    [InlineData("https://example.com:8443/a/b#frag")]
    public void 单个网址被识别(string input) =>
        Assert.True(LinkText.TryDetectUrl(input, out var url) && url.Length > 0);

    [Theory]
    [InlineData("看这个 https://example.com")]          // 网址夹在句子里
    [InlineData("https://example.com\n第二行")]          // 多行
    [InlineData("example.com")]                          // 缺协议与 www
    [InlineData("http://")]                              // 只有协议
    [InlineData("www.")]                                 // 只有前缀
    [InlineData("http://localhost")]                     // 没有顶级域
    [InlineData("http://127.0.0.1:8080")]                // 纯 IP（顶级域没有字母）
    [InlineData(@"C:\Users\me\Desktop\file.txt")]        // 本地路径
    [InlineData("user@example.com")]                     // 邮箱
    [InlineData("这是一段普通的中文文本。")]
    [InlineData("")]
    [InlineData(null)]
    public void 非网址不被识别(string? input) => Assert.False(LinkText.TryDetectUrl(input, out _));

    [Fact]
    public void 超长文本不当网址()
    {
        var input = "https://example.com/" + new string('a', LinkText.MaxUrlLength);
        Assert.False(LinkText.TryDetectUrl(input, out _));
    }

    // ─────────────── 列表标识（颜色/图标按 KindKey 切换） ───────────────

    [Fact]
    public void 网址显示为网站标识()
    {
        var item = ViewModel(Item("https://www.bilibili.com/video/BV1pQYH6XEZy/"));
        Assert.Equal("Link", item.KindKey);
        Assert.Equal("网站", item.KindLabel);
    }

    [Theory]
    [InlineData(ClipContentType.Text, "Text", "文本")]
    [InlineData(ClipContentType.Image, "Image", "图片")]
    [InlineData(ClipContentType.FileList, "File", "文件")]
    [InlineData(ClipContentType.Html, "Html", "HTML")]
    public void 各类型映射到对应标识(ClipContentType type, string expectedKey, string expectedLabel)
    {
        var item = ViewModel(Item("内容", type));
        Assert.Equal(expectedKey, item.KindKey);
        Assert.Equal(expectedLabel, item.KindLabel);
    }

    [Fact]
    public void 文本里夹着网址仍按文本处理()
    {
        var item = ViewModel(Item("参考资料：https://example.com 可以看看"));
        Assert.Equal("Text", item.KindKey);
    }

    // ─────────────── 体积文案 ───────────────

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(5 * 1024 * 1024 + 512 * 1024, "5.5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void 体积格式化(long bytes, string expected) => Assert.Equal(expected, ByteSize.Format(bytes));

    [Fact]
    public void 带上限的体积文案()
    {
        Assert.Equal("1.5 MB / 500 MB", ByteSize.FormatWithLimit(1536 * 1024, 500));
        Assert.Equal("2 MB（未限制上限）", ByteSize.FormatWithLimit(2 * 1024 * 1024, -1));
    }

    [Fact]
    public void 占用明细包含数据库与缓存文件()
    {
        var usage = new DiskUsageInfo(3 * 1024 * 1024, 1024 * 1024, 2 * 1024 * 1024, 12, 30);

        Assert.Equal("3 MB / 500 MB", usage.Format(500));
        Assert.Contains("数据库 1 MB", usage.Describe(), StringComparison.Ordinal);
        Assert.Contains("缓存文件 12 个", usage.Describe(), StringComparison.Ordinal);
        Assert.Contains("记录 30 条", usage.Describe(), StringComparison.Ordinal);
    }

    // ─────────────── 应用图标资源 ───────────────

    [Fact]
    public void 嵌入的应用图标可读且包含多尺寸()
    {
        var bytes = AppIcon.TryReadBytes();
        Assert.NotNull(bytes);
        Assert.True(bytes!.Length > 1000);

        // 16 / 32 / 256 都要能取出来（16 用于托盘，256 用于窗口与任务栏）
        foreach (var size in new[] { 16, 32, 256 })
        {
            Assert.True(IcoWriter.TryGetImage(bytes, size, out var image, out var actualSize), $"缺少 {size}px 图像");
            Assert.True(image.Length > 0);
            Assert.True(actualSize >= 16);
        }
    }

    [Fact]
    public void 应用图标可作为窗口图标加载()
    {
        var source = AppIcon.TryLoadImageSource();
        Assert.NotNull(source);
        Assert.True(source!.Width >= 32);
    }

    private static ClipItem Item(string? text, ClipContentType type = ClipContentType.Text) => new()
    {
        Id = 1,
        Type = type,
        TextContent = text,
        Preview = text ?? string.Empty,
        ContentHash = "hash",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static ClipItemViewModel ViewModel(ClipItem item) =>
        new(item, SensitiveMasker.Disabled, DateTimeOffset.UnixEpoch);
}
