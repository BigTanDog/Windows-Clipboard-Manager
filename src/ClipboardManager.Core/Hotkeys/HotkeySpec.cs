using System.Globalization;

namespace ClipboardManager.Core.Hotkeys;

/// <summary>RegisterHotKey 使用的修饰键位标志（与 Win32 MOD_* 常量一致）。</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    /// <summary>无修饰键。本工具会拒绝这种组合（会吞掉正常输入）。</summary>
    None = 0,

    /// <summary>Alt（MOD_ALT = 0x0001）。</summary>
    Alt = 0x0001,

    /// <summary>Ctrl（MOD_CONTROL = 0x0002）。</summary>
    Control = 0x0002,

    /// <summary>Shift（MOD_SHIFT = 0x0004）。</summary>
    Shift = 0x0004,

    /// <summary>Win 键（MOD_WIN = 0x0008）。</summary>
    Win = 0x0008,
}

/// <summary>
/// 全局热键描述：修饰键 + 虚拟键码。纯数据，可在无桌面环境下单测。
/// </summary>
public sealed record HotkeySpec(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SPACE"] = 0x20,
        ["ENTER"] = 0x0D,
        ["RETURN"] = 0x0D,
        ["TAB"] = 0x09,
        ["ESC"] = 0x1B,
        ["ESCAPE"] = 0x1B,
        ["BACKSPACE"] = 0x08,
        ["DELETE"] = 0x2E,
        ["DEL"] = 0x2E,
        ["INSERT"] = 0x2D,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["PAGEUP"] = 0x21,
        ["PAGEDOWN"] = 0x22,
        ["UP"] = 0x26,
        ["DOWN"] = 0x28,
        ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27,
        ["`"] = 0xC0,
        ["-"] = 0xBD,
        ["="] = 0xBB,
        ["["] = 0xDB,
        ["]"] = 0xDD,
        ["\\"] = 0xDC,
        [";"] = 0xBA,
        ["'"] = 0xDE,
        [","] = 0xBC,
        ["."] = 0xBE,
        ["/"] = 0xBF,
    };

    /// <summary>
    /// 解析形如 <c>Ctrl+Shift+V</c> 的字符串。
    /// </summary>
    /// <param name="text">待解析文本。</param>
    /// <param name="spec">解析结果。</param>
    /// <param name="error">失败原因（成功时为 null）。</param>
    /// <returns>是否解析成功。</returns>
    public static bool TryParse(string? text, out HotkeySpec? spec, out string? error)
    {
        spec = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "热键不能为空";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;
        var sawKey = false;

        foreach (var rawToken in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (rawToken.Length == 0)
            {
                continue;
            }

            if (sawKey)
            {
                error = "热键只能包含一个主键";
                return false;
            }

            switch (rawToken.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "SHIFT":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Win;
                    break;
                default:
                    if (!TryParseKey(rawToken, out virtualKey))
                    {
                        error = $"无法识别的按键：{rawToken}";
                        return false;
                    }

                    sawKey = true;
                    break;
            }
        }

        if (!sawKey)
        {
            error = "热键缺少主键（例如 V）";
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            // 无修饰键的全局热键会拦截正常打字，必须拒绝（需求 §3.4 要求可配置且安全）。
            error = "热键至少需要一个修饰键（Ctrl / Shift / Alt / Win）";
            return false;
        }

        spec = new HotkeySpec(modifiers, virtualKey);
        return true;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(DescribeKey(VirtualKey));
        return string.Join('+', parts);
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                virtualKey = c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                virtualKey = c;
                return true;
            }
        }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f'))
        {
            if (int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var fn)
                && fn is >= 1 and <= 24)
            {
                virtualKey = (uint)(0x70 + fn - 1);
                return true;
            }
        }

        return NamedKeys.TryGetValue(token, out virtualKey);
    }

    private static string DescribeKey(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= 0x70 and <= 0x87)
        {
            return "F" + (virtualKey - 0x70 + 1);
        }

        // 先找多字符别名（Enter / PageUp / F1 这类更具可读性），再回退到单字符符号（` - = [ ] \ ; ' , . /）。
        foreach (var pair in NamedKeys)
        {
            if (pair.Value == virtualKey && pair.Key.Length > 1)
            {
                return pair.Key;
            }
        }

        foreach (var pair in NamedKeys)
        {
            if (pair.Value == virtualKey)
            {
                return pair.Key;
            }
        }

        return "0x" + virtualKey.ToString("X2", CultureInfo.InvariantCulture);
    }
}
