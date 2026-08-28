namespace MarkLeaf.Services.Settings;

public readonly record struct ScreenArea(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

public static class WindowPlacementCalculator
{
    private const int MinimumWidthDip = 900;
    private const int MinimumHeightDip = 600;
    private const int VisibleMarginDip = 80;

    public static WindowSettings Normalize(
        WindowSettings saved,
        int targetDpi,
        IReadOnlyList<ScreenArea> workingAreas)
    {
        var safeTargetDpi = Math.Max(96, targetDpi);
        var minimumWidth = ScaleDip(MinimumWidthDip, safeTargetDpi);
        var minimumHeight = ScaleDip(MinimumHeightDip, safeTargetDpi);
        var width = Math.Max(minimumWidth, ScaleDip(saved.Width, safeTargetDpi));
        var height = Math.Max(minimumHeight, ScaleDip(saved.Height, safeTargetDpi));

        var candidate = new WindowSettings
        {
            Left = saved.Left,
            Top = saved.Top,
            Width = width,
            Height = height,
            Dpi = safeTargetDpi,
            IsMaximized = saved.IsMaximized,
            WorkspaceWidth = Math.Max(ScaleDip(160, safeTargetDpi), ScaleDip(saved.WorkspaceWidth, safeTargetDpi)),
            OutlineWidth = Math.Max(ScaleDip(160, safeTargetDpi), ScaleDip(saved.OutlineWidth, safeTargetDpi)),
            OutlineDetached = saved.OutlineDetached,
            SidebarCollapsed = saved.SidebarCollapsed,
            SidebarActiveOutline = saved.SidebarActiveOutline,
        };

        if (workingAreas.Count == 0)
        {
            return candidate;
        }

        var visibleMargin = ScaleDip(VisibleMarginDip, safeTargetDpi);
        var isVisible = workingAreas.Any(area =>
            candidate.Left < area.Right - visibleMargin
            && candidate.Left + candidate.Width > area.Left + visibleMargin
            && candidate.Top < area.Bottom - visibleMargin
            && candidate.Top + candidate.Height > area.Top + visibleMargin);

        if (isVisible)
        {
            return candidate;
        }

        var primary = workingAreas[0];
        candidate.Width = Math.Min(candidate.Width, primary.Width);
        candidate.Height = Math.Min(candidate.Height, primary.Height);
        candidate.Left = primary.Left + Math.Max(0, (primary.Width - candidate.Width) / 2);
        candidate.Top = primary.Top + Math.Max(0, (primary.Height - candidate.Height) / 2);
        candidate.IsMaximized = false;
        return candidate;
    }

    public static int ToLogicalPixels(int physicalPixels, int dpi)
    {
        var safeDpi = Math.Max(96, dpi);
        return (int)Math.Round(physicalPixels * 96d / safeDpi);
    }

    private static int ScaleDip(int value, int dpi) => (int)Math.Round(value * dpi / 96d);
}
