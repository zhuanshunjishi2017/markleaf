using System.Text;
using MarkLeaf.UI;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ClipboardHtmlFormatterTests
{
    [TestMethod]
    public void Create_UsesUtf8ByteOffsetsAndRoundTripsFragment()
    {
        const string fragment = "<strong>叶子</strong>";

        var clipboardHtml = ClipboardHtmlFormatter.Create(fragment);
        var startFragment = ReadOffset(clipboardHtml, "StartFragment:");
        var endFragment = ReadOffset(clipboardHtml, "EndFragment:");
        var bytes = Encoding.UTF8.GetBytes(clipboardHtml);

        Assert.AreEqual(fragment, Encoding.UTF8.GetString(bytes[startFragment..endFragment]));
        Assert.AreEqual(fragment, ClipboardHtmlFormatter.ExtractFragment(clipboardHtml));
    }

    [TestMethod]
    public void ExtractFragment_PreservesUnmarkedHtml()
    {
        const string html = "<p>plain</p>";

        Assert.AreEqual(html, ClipboardHtmlFormatter.ExtractFragment(html));
    }

    private static int ReadOffset(string value, string name)
    {
        var start = value.IndexOf(name, StringComparison.Ordinal) + name.Length;
        return int.Parse(value.AsSpan(start, 10), System.Globalization.CultureInfo.InvariantCulture);
    }
}
