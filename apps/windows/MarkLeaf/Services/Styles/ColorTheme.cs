namespace MarkLeaf.Services.Styles;

internal sealed record ColorTheme(
    string Id,
    string DisplayName,
    bool IsDark,
    IReadOnlyDictionary<string, Color> Colors);
