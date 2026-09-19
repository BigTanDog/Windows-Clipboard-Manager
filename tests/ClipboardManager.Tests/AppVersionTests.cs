using ClipboardManager.App;

namespace ClipboardManager.Tests;

/// <summary>
/// 版本号展示测试（用户 2026-09-19 要求：设置窗口底部能看到当前文件版本）。
/// </summary>
public sealed class AppVersionTests
{
    [Fact]
    public void 版本号展示为三段式()
    {
        // 第四段通常恒为 0，不展示；Build 未定义（-1）时按 0 处理。
        Assert.Equal("1.1.0", AppVersion.Format(new Version(1, 1, 0, 0)));
        Assert.Equal("2.0.3", AppVersion.Format(new Version(2, 0, 3, 7)));
        Assert.Equal("1.1.0", AppVersion.Format(new Version(1, 1)));
    }

    [Fact]
    public void 版本缺失时不抛异常()
    {
        Assert.Equal("未知", AppVersion.Format(null));
    }

    [Fact]
    public void 程序集版本号可用且格式正确()
    {
        // 真正的作用是防回归：版本号必须在 Directory.Build.props 里配好（否则界面会显示「未知」）。
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Display);
        Assert.NotEqual("未知", AppVersion.Display);
    }
}
