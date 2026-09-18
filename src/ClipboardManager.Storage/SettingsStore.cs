using System.Text.Json;
using ClipboardManager.Core.Settings;

namespace ClipboardManager.Storage;

/// <summary>
/// 设置持久化（<c>data/settings.json</c>，技术设计 §7.4）。
/// 读取时对损坏文件做备份并回退默认值；写入使用「临时文件 + 替换」的原子写。
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private readonly AppLog? _log;

    /// <summary>创建存储。</summary>
    /// <param name="path">设置文件路径。</param>
    /// <param name="log">日志器（可空）。</param>
    public SettingsStore(string path, AppLog? log = null)
    {
        _path = path;
        _log = log;
        Current = AppSettings.Default;
    }

    /// <summary>当前生效的设置（始终是 <see cref="AppSettings.Normalize"/> 之后的值）。</summary>
    public AppSettings Current { get; private set; }

    /// <summary>读取设置；文件不存在或损坏时返回默认值（损坏文件备份为 <c>.bad</c>）。</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                Current = AppSettings.Default;
                return Current;
            }

            var json = File.ReadAllText(_path);
            var parsed = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            Current = (parsed ?? AppSettings.Default).Normalize();
            return Current;
        }
        catch (Exception ex)
        {
            _log?.Error("settings.json 解析失败，已回退默认值", ex);
            TryBackupBrokenFile();
            Current = AppSettings.Default;
            return Current;
        }
    }

    /// <summary>原子写入设置。</summary>
    /// <param name="settings">要保存的设置（会先 Normalize）。</param>
    /// <param name="error">失败原因。</param>
    public bool TrySave(AppSettings settings, out string? error)
    {
        ArgumentNullException.ThrowIfNull(settings);
        error = null;
        var normalized = settings.Normalize();

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(normalized, SettingsJsonContext.Default.AppSettings);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, overwrite: true);
            Current = normalized;
            return true;
        }
        catch (Exception ex)
        {
            error = $"保存设置失败：{ex.Message}";
            return false;
        }
    }

    private void TryBackupBrokenFile()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Move(_path, _path + ".bad", overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _log?.Error("备份损坏的设置文件失败", ex);
        }
    }
}
