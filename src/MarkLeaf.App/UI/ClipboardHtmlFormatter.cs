using System.Globalization;
using System.Text;

namespace MarkLeaf.UI;

internal static class ClipboardHtmlFormatter
{
    private const string StartMarker = "<!--StartFragment-->";
    private const string EndMarker = "<!--EndFragment-->";
    private const string HeaderTemplate =
        "Version:0.9\r\n" +
        "StartHTML:{0:D10}\r\n" +
        "EndHTML:{1:D10}\r\n" +
        "StartFragment:{2:D10}\r\n" +
        "EndFragment:{3:D10}\r\n";

    public static string Create(string fragment)
    {
        var htmlPrefix = "<!DOCTYPE html><html><body>" + StartMarker;
        var htmlSuffix = EndMarker + "</body></html>";
        var emptyHeader = string.Format(CultureInfo.InvariantCulture, HeaderTemplate, 0, 0, 0, 0);
        var startHtml = Encoding.UTF8.GetByteCount(emptyHeader);
        var startFragment = startHtml + Encoding.UTF8.GetByteCount(htmlPrefix);
        var endFragment = startFragment + Encoding.UTF8.GetByteCount(fragment);
        var endHtml = endFragment + Encoding.UTF8.GetByteCount(htmlSuffix);
        var header = string.Format(
            CultureInfo.InvariantCulture,
            HeaderTemplate,
            startHtml,
            endHtml,
            startFragment,
            endFragment);
        return header + htmlPrefix + fragment + htmlSuffix;
    }

    public static string ExtractFragment(string clipboardHtml)
    {
        var markerStart = clipboardHtml.IndexOf(StartMarker, StringComparison.OrdinalIgnoreCase);
        if (markerStart >= 0)
        {
            markerStart += StartMarker.Length;
            var markerEnd = clipboardHtml.IndexOf(EndMarker, markerStart, StringComparison.OrdinalIgnoreCase);
            if (markerEnd >= markerStart)
            {
                return clipboardHtml[markerStart..markerEnd];
            }
        }

        return clipboardHtml;
    }
}
