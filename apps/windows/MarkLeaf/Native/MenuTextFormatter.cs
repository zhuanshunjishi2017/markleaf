using System.Globalization;
using System.Text.RegularExpressions;

namespace MarkLeaf.Native;

internal static partial class MenuTextFormatter
{
    public static string Format(
        string text,
        bool showKeyboardShortcuts,
        bool showMnemonics,
        string uiLanguage)
    {
        var result = text;
        if (!showKeyboardShortcuts)
        {
            var shortcutIndex = result.IndexOf('\t');
            if (shortcutIndex >= 0)
                result = result[..shortcutIndex];
        }

        var effectiveLanguage = string.IsNullOrWhiteSpace(uiLanguage)
            ? CultureInfo.CurrentUICulture.Name
            : uiLanguage;
        if (!showMnemonics && !effectiveLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            result = ParenthesizedMnemonicRegex().Replace(result, string.Empty);

        return result;
    }

    [GeneratedRegex(@"\s*\(&[^)]\)")]
    private static partial Regex ParenthesizedMnemonicRegex();
}
