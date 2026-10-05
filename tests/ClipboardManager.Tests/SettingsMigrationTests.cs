using System.IO;
using ClipboardManager.Core.Settings;
using ClipboardManager.Storage;

namespace ClipboardManager.Tests;

/// <summary>
/// 设置文件的结构版本迁移（技术设计 §7.4）。
/// <para>
/// 判据：源生成反序列化对「文件里缺失的字段」给出的是类型零值，<b>不是</b>属性初始值 ——
/// 所以每个"默认开启"的开关都必须有迁移，否则老用户升级后功能会静默失效（界面上看不出来）。
/// </para>
/// </summary>
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string _directory;

    public SettingsMigrationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "clipboard-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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
    public void 老设置文件缺少删除确认开关时迁移为开启()
    {
        // v2 时代的设置文件：没有 confirmDeletePinned 字段
        var settings = Load("""{"schemaVersion":2,"maxItems":100,"hotkey":"Ctrl+Shift+V"}""");

        Assert.True(settings.ConfirmDeletePinned);
        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
    }

    [Fact]
    public void 用户显式关闭后不会被迁移重新打开()
    {
        var settings = Load("""{"schemaVersion":3,"confirmDeletePinned":false}""");

        Assert.False(settings.ConfirmDeletePinned);
    }

    [Fact]
    public void 两个历史开关的迁移互不干扰()
    {
        // v1：既没有 clearClipboardOnDelete（v2 引入）也没有 confirmDeletePinned（v3 引入）
        var settings = Load("""{"schemaVersion":1}""");

        Assert.True(settings.ClearClipboardOnDelete);
        Assert.True(settings.ConfirmDeletePinned);
    }

    [Fact]
    public void 默认值与本轮新增字段一致()
    {
        Assert.Equal(3, AppSettings.CurrentSchemaVersion);
        Assert.True(AppSettings.Default.ConfirmDeletePinned);      // 默认开启（用户指定）
        Assert.False(AppSettings.Default.ClearPinnedOnClearHistory); // 默认保留收藏（B-12）
    }

    private AppSettings Load(string json)
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, json);
        return new SettingsStore(path).Load();
    }
}
