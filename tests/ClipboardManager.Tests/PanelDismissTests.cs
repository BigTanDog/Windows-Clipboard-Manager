using ClipboardManager.Core.Ui;

namespace ClipboardManager.Tests;

/// <summary>
/// 右键菜单关闭后是否该顺带收起面板（用户 2026-10-24 反馈：
/// 右键选中一条后直接点面板外部，菜单关了但面板关不掉）。
/// </summary>
public class PanelDismissTests
{
    [Fact]
    public void 菜单被点外部关掉时面板应一起收起()
    {
        // 点外部：菜单已关、本轮没执行命令、面板已不是激活窗口 → 收起
        Assert.True(PanelDismiss.ShouldHideAfterContextMenu(
            hideOnClickOutside: true,
            commandInvoked: false,
            contextMenuOpen: false,
            isActive: false,
            isVisible: true));
    }

    [Fact]
    public void 点了菜单项时面板要留着()
    {
        // 收藏 / 删除本身就是"在面板里继续操作"，不能顺手关掉面板
        Assert.False(PanelDismiss.ShouldHideAfterContextMenu(
            hideOnClickOutside: true,
            commandInvoked: true,
            contextMenuOpen: false,
            isActive: false,
            isVisible: true));
    }

    [Fact]
    public void 菜单还在开的时候不能收起()
    {
        Assert.False(PanelDismiss.ShouldHideAfterContextMenu(
            hideOnClickOutside: true,
            commandInvoked: false,
            contextMenuOpen: true,
            isActive: false,
            isVisible: true));
    }

    [Fact]
    public void 面板仍是激活窗口时不能收起()
    {
        // Esc 关菜单 / 点了面板内部：面板还激活，下次点外部会走正常链路，这里不该抢着关
        Assert.False(PanelDismiss.ShouldHideAfterContextMenu(
            hideOnClickOutside: true,
            commandInvoked: false,
            contextMenuOpen: false,
            isActive: true,
            isVisible: true));
    }

    [Fact]
    public void 关掉点击外部隐藏设置后永不自动收起()
    {
        // 设置里关掉「点击面板外部时自动隐藏」，连这个补丁路径也不能违反用户意愿
        Assert.False(PanelDismiss.ShouldHideAfterContextMenu(
            hideOnClickOutside: false,
            commandInvoked: false,
            contextMenuOpen: false,
            isActive: false,
            isVisible: true));
    }

    [Fact]
    public void 面板本来就不可见时不做多余动作()
    {
        Assert.False(PanelDismiss.ShouldHideAfterContextMenu(
            hideOnClickOutside: true,
            commandInvoked: false,
            contextMenuOpen: false,
            isActive: false,
            isVisible: false));
    }
}
