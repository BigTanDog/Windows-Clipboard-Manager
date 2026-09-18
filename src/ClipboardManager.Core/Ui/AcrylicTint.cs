namespace ClipboardManager.Core.Ui;

/// <summary>
/// 面板透明度（设置项「面板透明度」，历史字段名 acrylicStrength）的映射。纯函数，便于单测。
/// <para>
/// <b>为什么没有系统模糊</b>：Windows 的 <c>SetWindowCompositionAttribute</c> 模糊通道
/// （accent 3 / accent 4）在逐像素透明（分层）窗口上不可靠 —— 2026-09-19 实测：accent 4 在
/// Win11 上根本不模糊（面板区域亮度标准差与无模糊时相同），accent 3 初期能模糊、但过一段时间
/// 会把窗口合成搞坏（内容只剩"鬼影"）。因此本功能降级为<b>纯半透明</b>：只改 WPF 底色 alpha，
/// 不碰任何 DWM/窗口合成接口，稳定优先。
/// </para>
/// <para>
/// 约定：强度 0 = 完全不透明，强度越大越透；即使拉满也保留
/// <see cref="MinOpacityPercent"/>（65%）的底色 —— 面板始终以底色为主，不会透到影响阅读。
/// </para>
/// </summary>
public static class AcrylicTint
{
    /// <summary>强度上限（百分比）。</summary>
    public const int MaxStrength = 100;

    /// <summary>强度上限时仍保留的最低不透明度（百分比）。封顶 65%：按用户要求"别做那么透明"。</summary>
    public const int MinOpacityPercent = 65;

    /// <summary>把任意输入收敛到 0–100。</summary>
    public static int Normalize(int strength) => Math.Clamp(strength, 0, MaxStrength);

    /// <summary>是否启用半透明（强度 &gt; 0）。</summary>
    public static bool IsEnabled(int strength) => Normalize(strength) > 0;

    /// <summary>强度对应的底色不透明度百分比（0 强度 = 100% 不透明）。</summary>
    public static int OpacityPercent(int strength)
    {
        var normalized = Normalize(strength);
        var range = 100 - MinOpacityPercent;
        return 100 - (int)Math.Round(range * (normalized / 100.0));
    }

    /// <summary>强度对应的 alpha 字节（0–255）。</summary>
    public static int AlphaByte(int strength) => (int)Math.Round(255 * OpacityPercent(strength) / 100.0);
}
