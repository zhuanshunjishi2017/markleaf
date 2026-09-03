namespace MarkLeaf.Commands;

/// <summary>
/// Keys 与规范字符串（如 "Ctrl+N"、"Ctrl+Shift+S"、"F11"、"Ctrl+."）的互转，
/// 字符串格式与菜单右侧显示的快捷键风格一致，也用于 settings.json 持久化。
/// </summary>
public static class ShortcutTextFormatter
{
    public static string Format(Keys keys)
    {
        var parts = new List<string>();
        if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keys & Keys.Shift) != 0) parts.Add("Shift");
        if ((keys & Keys.Alt) != 0) parts.Add("Alt");
        parts.Add(KeyName(keys & Keys.KeyCode));
        return string.Join("+", parts);
    }

    public static bool TryParse(string text, out Keys keys)
    {
        keys = Keys.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = Keys.None;
        var keyToken = string.Empty;
        foreach (var part in text.Split('+'))
        {
            switch (part.Trim().ToUpperInvariant())
            {
                case "CTRL":
                    modifiers |= Keys.Control;
                    break;
                case "SHIFT":
                    modifiers |= Keys.Shift;
                    break;
                case "ALT":
                    modifiers |= Keys.Alt;
                    break;
                default:
                    keyToken = part.Trim();
                    break;
            }
        }

        if (!TryParseKeyName(keyToken, out var key))
        {
            return false;
        }

        keys = modifiers | key;
        return true;
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.A and <= Keys.Z => ((char)('A' + (key - Keys.A))).ToString(),
        >= Keys.F1 and <= Keys.F24 => $"F{key - Keys.F1 + 1}",
        Keys.OemPeriod => ".",
        Keys.Oemcomma => ",",
        Keys.OemMinus => "-",
        Keys.Oemplus => "=",
        Keys.Oemtilde => "`",
        Keys.Oem5 => "\\",
        Keys.Oem2 => "/",
        Keys.Oem4 => "[",
        Keys.Oem6 => "]",
        Keys.Space => "Space",
        Keys.Tab => "Tab",
        _ => key.ToString(),
    };

    private static bool TryParseKeyName(string token, out Keys key)
    {
        key = Keys.None;
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                key = Keys.A + (c - 'A');
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = Keys.D0 + (c - '0');
                return true;
            }

            switch (c)
            {
                case '.': key = Keys.OemPeriod; return true;
                case ',': key = Keys.Oemcomma; return true;
                case '-': key = Keys.OemMinus; return true;
                case '=': key = Keys.Oemplus; return true;
                case '`': key = Keys.Oemtilde; return true;
                case '\\': key = Keys.Oem5; return true;
                case '/': key = Keys.Oem2; return true;
                case '[': key = Keys.Oem4; return true;
                case ']': key = Keys.Oem6; return true;
                default: return false;
            }
        }

        if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token[1..], out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            key = Keys.F1 + (functionNumber - 1);
            return true;
        }

        if (string.Equals(token, "Space", StringComparison.OrdinalIgnoreCase))
        {
            key = Keys.Space;
            return true;
        }

        if (string.Equals(token, "Tab", StringComparison.OrdinalIgnoreCase))
        {
            key = Keys.Tab;
            return true;
        }

        return false;
    }
}
