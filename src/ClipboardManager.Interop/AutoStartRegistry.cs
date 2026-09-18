using Microsoft.Win32;

namespace ClipboardManager.Interop;

/// <summary>
/// 开机自启项的读写（需求 §3.5）。
/// <para>
/// 只写 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> —— <b>不碰 HKLM</b>，
/// 避免需要管理员权限，也避免污染整机配置。除这一处外，本程序不写注册表
/// （AGENTS.md §4：数据只落程序目录 <c>data/</c>）。
/// </para>
/// </summary>
public static class AutoStartRegistry
{
    /// <summary>注册表路径（当前用户）。</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>值名（同一程序只用这一个值，避免残留多条自启项）。</summary>
    public const string ValueName = "ClipboardManager";

    /// <summary>写入（或覆盖）自启命令。</summary>
    /// <param name="command">完整命令行（用 <c>AutoStartCommand.Build</c> 构造）。</param>
    /// <param name="error">失败原因。</param>
    public static bool TrySet(string command, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "无法打开 HKCU 的 Run 注册表项";
                return false;
            }

            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            error = $"写注册表失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>删除自启项（不存在视为成功）。</summary>
    /// <param name="error">失败原因。</param>
    public static bool TryRemove(out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            error = $"删除注册表值失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>读取当前自启命令；读取失败或不存在返回 null。</summary>
    public static string? TryGet()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    /// <summary>自启项是否存在且与期望命令一致（用于设置界面回显真实状态）。</summary>
    /// <param name="expectedCommand">期望的命令行。</param>
    public static bool IsEnabledWith(string expectedCommand) =>
        string.Equals(TryGet(), expectedCommand, StringComparison.OrdinalIgnoreCase);
}
