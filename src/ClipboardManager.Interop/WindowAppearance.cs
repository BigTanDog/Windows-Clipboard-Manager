using System.Runtime.InteropServices;
using ClipboardManager.Core.Ui;

namespace ClipboardManager.Interop;

/// <summary>
/// 窗口外观增强（DWM）：深色标题栏与圆角。
/// <para>
/// 都是「有则更好」的装饰：Windows 10 上圆角属性会被忽略、深色标题栏需要 1809+，
/// 失败一律静默跳过，绝不因为外观问题影响功能。
/// </para>
/// </summary>
public static class WindowAppearance
{
    /// <summary>属性：标题栏使用深色（Windows 10 1809+）。</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>属性：窗口圆角偏好（Windows 11）。</summary>
    private const int DwmwaWindowCornerPreference = 33;

    /// <summary>值：圆角。</summary>
    private const int DwmwcpRound = 2;

    /// <summary>
    /// 按当前主题应用窗口外观（深色标题栏 + 圆角）。
    /// </summary>
    /// <param name="hWnd">窗口句柄（必须已创建）。</param>
    /// <param name="dark">是否深色主题。</param>
    public static void ApplyTheme(IntPtr hWnd, bool dark)
    {
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        var darkValue = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(hWnd, DwmwaUseImmersiveDarkMode, ref darkValue, sizeof(int));

        var corner = DwmwcpRound;
        _ = NativeMethods.DwmSetWindowAttribute(hWnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
    }

    /// <summary>窗口合成属性号：<c>WCA_ACCENT_POLICY</c>。</summary>
    private const int WcaAccentPolicy = 19;

    /// <summary>策略值：不使用合成（恢复成普通不透明窗口）。</summary>
    private const int AccentDisabled = 0;

    /// <summary>
    /// 策略值：模糊背景（Win10 起的老接口，<b>实测在 Windows 11 上才是真模糊</b>）。
    /// <para>
    /// 为什么不选 <c>ACCENT_ENABLE_ACRYLICBLURBEHIND(4)</c>：2026-09-19 在本机用「黑白条纹背景 +
    /// 面板区域亮度标准差」实测，accent 4 的标准差与无模糊时几乎相同（84.99 vs 基线 84.52，等于没模糊，
    /// 只有染色），而 accent 3 把标准差压到 33.16 —— 是货真价实的模糊。用户也反馈"模糊强度太低"，
    /// 因此改用 accent 3。
    /// </para>
    /// </summary>
    private const int AccentEnableBlurBehind = 3;

    /// <summary>
    /// 应用或关闭亚克力背景（附加项 B-04）。
    /// <para>
    /// 说明：调用方需要保证窗口允许逐像素透明（WPF 的 <c>AllowsTransparency=true</c>）并把自身背景
    /// 调成半透明，否则模糊会被不透明内容整片遮住（本方法只负责告诉系统"给我加模糊"）。
    /// </para>
    /// </summary>
    /// <param name="hWnd">窗口句柄。</param>
    /// <param name="strength">强度 0–100；0 表示关闭并恢复不透明。</param>
    /// <param name="red">主题底色红分量（染色与面板底色保持一致）。</param>
    /// <param name="green">主题底色绿分量。</param>
    /// <param name="blue">主题底色蓝分量。</param>
    /// <returns>是否成功交给系统（失败只影响外观，调用方不应据此改变功能行为）。</returns>
    public static bool ApplyAcrylic(IntPtr hWnd, int strength, byte red, byte green, byte blue)
    {
        if (hWnd == IntPtr.Zero)
        {
            return false;
        }

        var enabled = AcrylicTint.IsEnabled(strength);
        var policy = new ACCENTPOLICY
        {
            AccentState = enabled ? AccentEnableBlurBehind : AccentDisabled,

            // 社区约定值 2：让模糊区域覆盖整个窗口客户区（而不是只覆盖标题栏）
            AccentFlags = 2,
            GradientColor = enabled ? unchecked((int)AcrylicTint.PackAbgr(red, green, blue, strength)) : 0,
        };

        var size = Marshal.SizeOf<ACCENTPOLICY>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, fDeleteOld: false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attribute = WcaAccentPolicy,
                Data = buffer,
                SizeOfData = size,
            };

            return NativeMethods.SetWindowCompositionAttribute(hWnd, ref data) != 0;
        }
        catch (Exception)
        {
            // 未公开 API：任何异常都只当作"这台机器不支持"，不影响功能。
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
