using System.IO;
using ClipboardManager.Core.Settings;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>设置持久化测试（默认值、往返、损坏文件兜底）。</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 忽略清理失败。
        }
    }

    [Fact]
    public void 文件不存在时返回默认值()
    {
        var store = new SettingsStore(_path);
        var settings = store.Load();

        Assert.Equal(AppSettings.DefaultMaxItems, settings.MaxItems);
        Assert.Equal(AppSettings.DefaultHotkey, settings.Hotkey);
        Assert.True(settings.MaskSensitiveData);
    }

    [Fact]
    public void 保存后可原样读回()
    {
        var store = new SettingsStore(_path);
        store.Load();

        var saved = new AppSettings
        {
            MaxItems = 200,
            DiskQuotaMb = 1024,
            Hotkey = "Ctrl+Alt+C",
            AutoPaste = false,
            MaskSensitiveData = false,
            Theme = "dark",
            ExcludedApps = ["keepass.exe"],
        };

        Assert.True(store.TrySave(saved, out var error), error);

        var reloaded = new SettingsStore(_path).Load();
        Assert.Equal(200, reloaded.MaxItems);
        Assert.Equal(1024, reloaded.DiskQuotaMb);
        Assert.Equal("Ctrl+Alt+C", reloaded.Hotkey);
        Assert.False(reloaded.AutoPaste);
        Assert.False(reloaded.MaskSensitiveData);
        Assert.Equal("dark", reloaded.Theme);
        Assert.Equal(["keepass.exe"], reloaded.ExcludedApps);
    }

    [Fact]
    public void 损坏文件回退默认值并备份()
    {
        File.WriteAllText(_path, "{ 这不是合法 JSON");
        var store = new SettingsStore(_path);

        var settings = store.Load();

        Assert.Equal(AppSettings.DefaultMaxItems, settings.MaxItems);
        Assert.True(File.Exists(_path + ".bad"));
    }

    [Fact]
    public void 非法值在保存时被收敛()
    {
        var store = new SettingsStore(_path);
        Assert.True(store.TrySave(new AppSettings { MaxItems = 777, Theme = "Neon" }, out _));

        var reloaded = new SettingsStore(_path).Load();
        Assert.Contains(reloaded.MaxItems, AppSettings.MaxItemsOptions);
        Assert.Equal("system", reloaded.Theme);
    }

    [Fact]
    public void 保存不留下临时文件()
    {
        var store = new SettingsStore(_path);
        Assert.True(store.TrySave(new AppSettings(), out _));
        Assert.False(File.Exists(_path + ".tmp"));
    }
}
