namespace ClipboardManager.Core.Ui;

/// <summary>
/// 面板列表的显示上限（纯逻辑，可单测）。
/// <para>
/// <b>显示上限与保留上限解耦</b>（用户 2026-10-06 拍板）：设置里的「记录条数上限」
/// （<see cref="ClipboardManager.Core.Settings.AppSettings.MaxItems"/>）只负责<b>淘汰</b>——
/// 决定库里留多少条；面板显示则固定为 <see cref="MaxItems"/> 条。
/// </para>
/// <para>
/// 为什么必须解耦：早先显示上限 = 保留上限，用户把上限设成 50 时，库里的记录（含收藏）会被
/// <c>LIMIT 50</c> 截断挤出列表，看着像"记录被删了"（B-16 的用户现象），并被误当成显示顺序问题。
/// 库里的记录数本就受保留上限约束（条数上限 + 磁盘上限），所以固定显示上限不会让面板无限变长。
/// </para>
/// <para>
/// 已知取舍：保留上限选「不限制」且磁盘上限也放开时，库里可能超过 500 条 —— 面板只显示最近
/// 500 条，更早的仍可用搜索找到（用户已确认接受这个硬上限）。
/// </para>
/// </summary>
public static class PanelDisplay
{
    /// <summary>面板最多展示的记录数（硬上限，与保留上限无关）。</summary>
    public const int MaxItems = 500;

    /// <summary>计算面板显示条数：恒定取硬上限。</summary>
    /// <param name="retentionMaxItems">
    /// 设置里的记录条数上限；本函数<b>刻意忽略</b>它（参数保留是为了让"两者已解耦"在调用点可见，
    /// 并有专门的单测守住，防止以后又被改回 <c>Math.Min(保留上限, 硬上限)</c>）。
    /// </param>
    public static int Limit(int retentionMaxItems)
    {
        _ = retentionMaxItems;
        return MaxItems;
    }
}
