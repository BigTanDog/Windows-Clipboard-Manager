using ClipboardManager.Core.Hotkeys;

namespace ClipboardManager.Tests;

/// <summary>
/// 设置页「按键录制」用到的构造逻辑（HotkeySpec.TryCreate）测试。
/// <para>
/// 录制出来的一定是能被 <see cref="HotkeySpec.TryParse"/> 读回来的规范文本，
/// 否则保存后重启就"热键失效"了 —— 下面用往返断言把这个约束钉住。
/// </para>
/// </summary>
public class HotkeyCaptureTests
{
    [Theory]
    [InlineData('V', HotkeyModifiers.Control, "Ctrl+V")]
    [InlineData('V', HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+V")]
    [InlineData('C', HotkeyModifiers.Control | HotkeyModifiers.Alt, "Ctrl+Alt+C")]
    [InlineData('1', HotkeyModifiers.Alt, "Alt+1")]
    public void 字母数字键可录制(char letter, HotkeyModifiers modifiers, string expected)
    {
        Assert.True(HotkeySpec.TryCreate(letter, modifiers, out var spec, out var error), error);
        Assert.Equal(expected, spec!.ToString());
    }

    [Theory]
    [InlineData(0x70, HotkeyModifiers.Control, "Ctrl+F1")]     // F1
    [InlineData(0x7B, HotkeyModifiers.Win, "Win+F12")]         // F12
    [InlineData(0x21, HotkeyModifiers.Control | HotkeyModifiers.Shift, "Ctrl+Shift+PageUp")]
    [InlineData(0xBD, HotkeyModifiers.Control, "Ctrl+-")]      // 符号键
    public void 功能键与符号键可录制(uint virtualKey, HotkeyModifiers modifiers, string expected)
    {
        Assert.True(HotkeySpec.TryCreate(virtualKey, modifiers, out var spec, out var error), error);
        Assert.Equal(expected, spec!.ToString());
    }

    [Fact]
    public void 没有修饰键时拒绝()
    {
        Assert.False(HotkeySpec.TryCreate('V', HotkeyModifiers.None, out _, out var error));
        Assert.Contains("Ctrl", error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0u)]          // 无效键码
    [InlineData(0x00)]        // 同上（Key.None）
    [InlineData(0x5D)]        // Apps 键：没有可读名字
    [InlineData(0xAD)]        // 静音键：没有可读名字
    public void 无名字的键拒绝录制(uint virtualKey)
    {
        Assert.False(HotkeySpec.IsSupportedKey(virtualKey));
        Assert.False(HotkeySpec.TryCreate(virtualKey, HotkeyModifiers.Control, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('0')]
    [InlineData(0x70)]
    [InlineData(0x87)]
    [InlineData(0x0D)]   // Enter
    [InlineData(0x2E)]   // Delete
    [InlineData(0xBF)]   // /
    public void 有名字的键被认可(uint virtualKey) => Assert.True(HotkeySpec.IsSupportedKey(virtualKey));

    [Fact]
    public void 录制的文本必须能解析回来()
    {
        var cases = new (uint Key, HotkeyModifiers Modifiers)[]
        {
            ('V', HotkeyModifiers.Control | HotkeyModifiers.Shift),
            (0x70, HotkeyModifiers.Alt),
            (0x21, HotkeyModifiers.Win | HotkeyModifiers.Control),
            (0xBE, HotkeyModifiers.Shift),   // .
        };

        foreach (var (key, modifiers) in cases)
        {
            Assert.True(HotkeySpec.TryCreate(key, modifiers, out var created, out var error), error);

            var text = created!.ToString();
            Assert.True(HotkeySpec.TryParse(text, out var parsed, out var parseError), parseError);
            Assert.Equal(created, parsed);
        }
    }
}
