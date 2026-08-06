using System.Text.RegularExpressions;

namespace MarkLeaf.Services.Styles;

internal sealed record StyleDefinition(string Id, string DisplayName, string Css, string? DependsOn = null);

internal static class StyleService
{
    private const string BaseFileName = "base.css";
    private const string NameMarker = "@name:";
    private const string DependsMarker = "@depends:";

    private static readonly List<StyleDefinition> StylesList = [];
    private static string _baseCss = "";

    public static string BaseCss => _baseCss;

    public static string StylesDirectory { get; private set; } = "";

    public static IReadOnlyList<StyleDefinition> Styles => StylesList;

    public static string DefaultStyleId => StylesList.Count > 0 ? StylesList[0].Id : "serif";

    /// <summary>
    /// 扫描样式目录，将每个 CSS 文件加载为一个排版样式。文件名为样式 ID，
    /// 文件内首个注释中的 @name 作为显示名，@depends 声明依赖的样式。
    /// </summary>
    public static void Initialize(string stylesDirectory)
    {
        StylesDirectory = stylesDirectory;
        StylesList.Clear();
        _baseCss = "";

        if (!Directory.Exists(stylesDirectory))
        {
            return;
        }

        var basePath = Path.Combine(stylesDirectory, BaseFileName);
        if (File.Exists(basePath))
        {
            _baseCss = File.ReadAllText(basePath);
        }

        var loaded = new List<StyleDefinition>();
        foreach (var file in Directory.GetFiles(stylesDirectory, "*.css")
                     .OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, BaseFileName, StringComparison.OrdinalIgnoreCase)
                || ColorThemeService.IsColorThemeFile(file))
            {
                continue;
            }

            var id = Path.GetFileNameWithoutExtension(fileName);
            var css = File.ReadAllText(file);
            var (displayName, dependsOn) = ParseMetadata(css, id);
            loaded.Add(new StyleDefinition(id, displayName, css, dependsOn));
        }

        // 按依赖关系拓扑排序：被依赖的样式先注入，依赖者后注入，
        // 确保级联优先级与 @depends 声明的意图一致。
        StylesList.AddRange(TopologicalSort(loaded));
    }

    private static List<StyleDefinition> TopologicalSort(List<StyleDefinition> styles)
    {
        var sorted = new List<StyleDefinition>();
        var remaining = new List<StyleDefinition>(styles);
        var added = new HashSet<string>(StringComparer.Ordinal);
        var changed = true;

        while (remaining.Count > 0 && changed)
        {
            changed = false;
            for (var i = remaining.Count - 1; i >= 0; i--)
            {
                var style = remaining[i];
                if (style.DependsOn is null || added.Contains(style.DependsOn))
                {
                    sorted.Add(style);
                    added.Add(style.Id);
                    remaining.RemoveAt(i);
                    changed = true;
                }
            }
        }

        // 剩余项（循环依赖或依赖缺失）追加在末尾。
        sorted.AddRange(remaining);
        return sorted;
    }

    public static IReadOnlyList<(string Id, string DisplayName)> GetAllStyles()
        => StylesList.Select(style => (style.Id, style.DisplayName)).ToArray();

    public static StyleDefinition? TryGetStyle(string id)
    {
        foreach (var style in StylesList)
        {
            if (string.Equals(style.Id, id, StringComparison.Ordinal))
            {
                return style;
            }
        }

        return null;
    }

    private static (string DisplayName, string? DependsOn) ParseMetadata(string css, string fallbackId)
    {
        var displayName = fallbackId;
        string? dependsOn = null;

        foreach (Match match in Regex.Matches(css, @"/\*([\s\S]*?)\*/"))
        {
            foreach (var line in match.Groups[1].Value.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(NameMarker, StringComparison.Ordinal))
                {
                    displayName = trimmed[NameMarker.Length..].Trim();
                }
                else if (trimmed.StartsWith(DependsMarker, StringComparison.Ordinal))
                {
                    dependsOn = trimmed[DependsMarker.Length..].Trim();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = fallbackId;
        }

        if (string.IsNullOrWhiteSpace(dependsOn))
        {
            dependsOn = null;
        }

        return (displayName, dependsOn);
    }
}
