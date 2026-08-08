using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace MarkLeaf.Services;

internal static class Loc
{
    private static readonly ConcurrentDictionary<string, string> _strings = new();
    private static readonly ConcurrentDictionary<string, string> _fallback = new();

    public static void Initialize(string localesDirectory, string culture = "")
    {
        if (string.IsNullOrWhiteSpace(culture))
            culture = CultureInfo.CurrentUICulture.Name;

        _strings.Clear();
        _fallback.Clear();

        LoadFile(Path.Combine(localesDirectory, "zh-CN.json"), _fallback);

        if (!string.Equals(culture, "zh-CN", StringComparison.OrdinalIgnoreCase))
            LoadFile(Path.Combine(localesDirectory, $"{culture}.json"), _strings);
    }

    private static void LoadFile(string path, ConcurrentDictionary<string, string> target)
    {
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null) return;
            foreach (var (key, value) in dict)
                target[key] = value;
        }
        catch { /* missing or malformed file is non-fatal */ }
    }

    public static string Get(string key)
    {
        if (_strings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;
        if (_fallback.TryGetValue(key, out var fb) && !string.IsNullOrEmpty(fb))
            return fb;
        return $"<{key}>";
    }

    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }
}
