using System.Globalization;
using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Styles;

internal static partial class ColorThemeService
{
    private const string FilePrefix = "colors-";
    private const string TypeMarker = "@type: color-theme";
    private const string NameMarker = "@name:";
    private const string ModeMarker = "@mode:";

    private static readonly List<ColorTheme> Themes = [];
    private static string _stylesDirectory = "";

    public static IReadOnlyList<ColorTheme> All => Themes;

    public static string ActiveThemeId { get; private set; } = "";

    public static event Action? ThemeChanged;

    public static void Initialize(string stylesDirectory)
    {
        _stylesDirectory = stylesDirectory;
        Themes.Clear();

        if (!Directory.Exists(stylesDirectory))
            return;

        foreach (var file in Directory.GetFiles(stylesDirectory, $"{FilePrefix}*.css"))
        {
            var theme = ParseColorTheme(file);
            if (theme is not null)
                Themes.Add(theme);
        }

        if (Themes.Count > 0 && string.IsNullOrEmpty(ActiveThemeId))
            ActiveThemeId = Themes[0].Id;
    }

    public static ColorTheme? TryGetTheme(string id)
    {
        return Themes.Find(t => string.Equals(t.Id, id, StringComparison.Ordinal));
    }

    public static IReadOnlyDictionary<string, Color> GetActiveColors()
    {
        var theme = TryGetTheme(ActiveThemeId);
        return theme?.Colors ?? new Dictionary<string, Color>();
    }

    public static string GetActiveThemeCss()
    {
        if (string.IsNullOrEmpty(_stylesDirectory) || string.IsNullOrEmpty(ActiveThemeId))
            return "";

        var file = Path.Combine(_stylesDirectory, $"{FilePrefix}{ActiveThemeId}.css");
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static bool IsActiveThemeDark()
    {
        var theme = TryGetTheme(ActiveThemeId);
        return theme?.IsDark == true;
    }

    public static void SetActiveTheme(string id)
    {
        if (!string.Equals(ActiveThemeId, id, StringComparison.Ordinal)
            && TryGetTheme(id) is not null)
        {
            ActiveThemeId = id;
            ThemeChanged?.Invoke();
        }
    }

    public static bool IsColorThemeFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
    }

    private static ColorTheme? ParseColorTheme(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);

            if (!content.Contains(TypeMarker, StringComparison.Ordinal))
                return null;

            var displayName = Path.GetFileNameWithoutExtension(filePath);
            var isDark = false;
            foreach (Match match in CommentBlock().Matches(content))
            {
                foreach (var line in match.Groups[1].Value.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith(NameMarker, StringComparison.Ordinal))
                        displayName = trimmed[NameMarker.Length..].Trim();
                    else if (trimmed.StartsWith(ModeMarker, StringComparison.Ordinal))
                        isDark = string.Equals(trimmed[ModeMarker.Length..].Trim(), "dark", StringComparison.OrdinalIgnoreCase);
                }
            }

            var id = Path.GetFileNameWithoutExtension(filePath);
            if (id.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase))
                id = id[FilePrefix.Length..];

            var colors = new Dictionary<string, Color>();
            var rootMatch = RootBlock().Match(content);
            if (rootMatch.Success)
            {
                foreach (Match prop in CustomProperty().Matches(rootMatch.Groups[1].Value))
                {
                    var name = prop.Groups[1].Value.Trim();
                    var value = prop.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(name) && TryParseColor(value, out var color))
                        colors[name] = color;
                }
            }

            if (colors.Count == 0)
                return null;

            return new ColorTheme(id, displayName, isDark, colors);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryParseColor(string value, out Color color)
    {
        color = default;

        if (string.IsNullOrEmpty(value))
            return false;

        value = value.Trim();

        // #RRGGBB or #RRGGBBAA
        if (value.StartsWith('#') && value.Length is 7 or 9)
        {
            try
            {
                var hex = int.Parse(value[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (value.Length == 7)
                    hex |= unchecked((int)0xFF000000); // #RRGGBB → 补全 alpha
                color = Color.FromArgb(hex);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // rgb(r, g, b) or rgba(r, g, b, a)
        var rgbMatch = RgbFunction().Match(value);
        if (rgbMatch.Success)
        {
            var r = int.Parse(rgbMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var g = int.Parse(rgbMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var b = int.Parse(rgbMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            var a = rgbMatch.Groups[4].Success
                ? (int)(float.Parse(rgbMatch.Groups[4].Value, CultureInfo.InvariantCulture) * 255)
                : 255;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }

    [GeneratedRegex(@"/\*([\s\S]*?)\*/")]
    private static partial Regex CommentBlock();

    [GeneratedRegex(@":root\s*\{([^}]*)\}", RegexOptions.Singleline)]
    private static partial Regex RootBlock();

    [GeneratedRegex(@"--([\w-]+)\s*:\s*([^;]+);")]
    private static partial Regex CustomProperty();

    [GeneratedRegex(@"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)")]
    private static partial Regex RgbFunction();
}
