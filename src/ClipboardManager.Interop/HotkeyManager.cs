using System.Runtime.InteropServices;
using ClipboardManager.Core.Hotkeys;

namespace ClipboardManager.Interop;

/// <summary>
/// 全局热键注册（需求 §3.4）。
/// <para>
/// 关键约束：注册可能因为组合被其它程序占用而失败 —— 必须检测并提示用户换键，
/// 不能静默失败（否则用户会以为程序坏了）。退出时必须注销，否则该组合会被永久占用到进程结束。
/// </para>
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    /// <summary>本进程使用的热键 ID（1..0xBFFF 范围内任意值，仅需进程内唯一）。</summary>
    public const int HotkeyId = 0x0C1B;

    private readonly IntPtr _window;
    private bool _registered;
    private bool _disposed;

    /// <summary>创建管理器。</summary>
    /// <param name="window">接收 WM_HOTKEY 的窗口（仅消息窗口）。</param>
    public HotkeyManager(IntPtr window) => _window = window;

    /// <summary>当前是否注册成功。</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// 尝试注册热键。
    /// </summary>
    /// <param name="spec">热键描述。</param>
    /// <param name="error">失败原因（可能被其它程序占用）。</param>
    public bool TryRegister(HotkeySpec spec, out string? error)
    {
        ArgumentNullException.ThrowIfNull(spec);
        error = null;

        if (_window == IntPtr.Zero)
        {
            error = "消息窗口尚未创建";
            return false;
        }

        Unregister();

        var modifiers = (uint)spec.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_window, HotkeyId, modifiers, spec.VirtualKey))
        {
            var code = Marshal.GetLastWin32Error();
            error = code == 1409
                ? $"热键 {spec} 已被其它程序占用，请在设置中更换"
                : $"注册热键 {spec} 失败，Win32 错误码 {code}";
            return false;
        }

        _registered = true;
        return true;
    }

    /// <summary>注销热键（幂等）。</summary>
    public void Unregister()
    {
        if (!_registered)
        {
            return;
        }

        NativeMethods.UnregisterHotKey(_window, HotkeyId);
        _registered = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
    }
}
