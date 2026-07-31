namespace MarkLeaf.Editor;

internal static class EditorCoordinateConverter
{
    private const int DefaultDpi = 96;

    public static Point CssToDevicePoint(double clientX, double clientY, int dpi)
    {
        var scale = Math.Max(DefaultDpi, dpi) / (double)DefaultDpi;
        return new Point(
            (int)Math.Round(clientX * scale),
            (int)Math.Round(clientY * scale));
    }
}
