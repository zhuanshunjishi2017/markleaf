using MarkLeaf.Services.Styles;

namespace MarkLeaf.UI.Dialogs;

internal static class DialogColors
{
    public static Color Primary => Get("bg-primary");
    public static Color Secondary => Get("bg-secondary");

    private static Color Get(string name)
        => ColorThemeService.GetActiveColors().TryGetValue(name, out var color)
            ? color
            : SystemColors.Control;
}
