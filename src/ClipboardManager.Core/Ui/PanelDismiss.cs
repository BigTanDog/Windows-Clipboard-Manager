namespace ClipboardManager.Core.Ui;

/// <summary>
/// 右键菜单关闭后「是否该顺带收起面板」的决策（纯函数，可单测）。
/// <para>
/// 背景（用户 2026-10-24 反馈）：右键选中一条记录、菜单弹出后<b>不选任何菜单项</b>，
/// 而是直接点面板外部想关掉面板 —— 结果菜单关了，面板却留在原地，
/// 而且之后点什么都不会自己消失，只能点右上角 ✕ 或先左键点一条记录"唤醒"它。
/// </para>
/// <para>
/// 原因：点面板外部的那一刻面板先失去激活（<c>Deactivated</c>），但此时菜单还开着，
/// 面板按设计跳过隐藏（否则菜单会被连带关掉）；等菜单随后关掉时，
/// 面板早已不是激活窗口，不会再收到第二次失活事件 —— 于是就卡在"已失活但仍可见"的状态。
/// 因此菜单关闭后必须补一次判断：本轮菜单<b>没执行任何命令</b>、面板<b>已不是激活窗口</b>
/// 且本来可见 → 说明是"点外部"关的菜单，面板也该一起收起。
/// </para>
/// </summary>
public static class PanelDismiss
{
    /// <summary>判断右键菜单关闭后是否应顺带隐藏面板。</summary>
    /// <param name="hideOnClickOutside">设置项「点击面板外部时自动隐藏」是否开启。</param>
    /// <param name="commandInvoked">本轮菜单是否执行过命令（粘贴 / 收藏 / 删除）。</param>
    /// <param name="contextMenuOpen">菜单是否仍然开着。</param>
    /// <param name="isActive">面板此刻是否仍是激活（前台）窗口。</param>
    /// <param name="isVisible">面板是否可见。</param>
    public static bool ShouldHideAfterContextMenu(
        bool hideOnClickOutside,
        bool commandInvoked,
        bool contextMenuOpen,
        bool isActive,
        bool isVisible)
    {
        // 关掉这个设置的用户要的就是"别自己关"，任何情况下都不隐藏。
        if (!hideOnClickOutside || !isVisible || contextMenuOpen)
        {
            return false;
        }

        // 菜单项点了（粘贴 / 收藏 / 删除）：面板该留着，交回各自的流程处理（粘贴会自己收起）。
        if (commandInvoked)
        {
            return false;
        }

        // 菜单是被 Esc 或点面板内部关掉的：面板仍是激活窗口，下次点外部照常会隐藏。
        return !isActive;
    }
}
