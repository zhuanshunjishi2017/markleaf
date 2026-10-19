namespace MarkLeaf.UI.Dialogs;

/// <summary>Distinguish PDF loading surfaces from the exported page, including blank pages.</summary>
internal static class ExportPreviewFramePolicy
{
    internal static bool IsPainted(Bitmap frame, bool allowBlankPage, Color? pageBackground = null)
    {
        if (allowBlankPage) return true;
        // Ignore viewer chrome and scrollbars at the outer edges. Require a
        // substantial painted area: a small loading spinner is not a dark page.
        var contentSamples = 0;
        var backgroundSamples = 0;
        var variedSamples = 0;
        // The center may itself be the loading spinner. Sample near the
        // content's corner so a small central spinner cannot make the whole
        // otherwise uniform shell appear to be non-uniform document content.
        var reference = frame.GetPixel(frame.Width / 12, frame.Height / 12);
        for (var row = 1; row < 40; row++)
        for (var column = 1; column < 48; column++)
        {
            var pixel = frame.GetPixel(frame.Width / 12 + column * frame.Width * 5 / (48 * 6),
                frame.Height / 12 + row * frame.Height * 5 / (40 * 6));
            if (pixel.A > 0 && (pixel.R < 235 || pixel.G < 235 || pixel.B < 235)) contentSamples++;
            if (pageBackground is { } background && Distance(pixel, background) <= 2) backgroundSamples++;
            if (Distance(pixel, reference) > 12) variedSamples++;
        }
        // A uniform gray PDF shell is not a rendered page. Accept the actual
        // export background (also valid for an empty page), or substantial
        // non-uniform content such as an image. A small spinner is insufficient.
        const int samples = 39 * 47;
        return contentSamples >= samples / 10
            && (backgroundSamples >= samples / 10 || variedSamples >= samples / 20);
    }

    private static int Distance(Color left, Color right) =>
        Math.Max(Math.Abs(left.R - right.R), Math.Max(Math.Abs(left.G - right.G), Math.Abs(left.B - right.B)));
}
