using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorCoordinateConverterTests
{
    [TestMethod]
    [DataRow(96, 120, 80)]
    [DataRow(120, 150, 100)]
    [DataRow(144, 180, 120)]
    [DataRow(192, 240, 160)]
    public void CssToDevicePoint_ScalesForCurrentMonitorDpi(int dpi, int expectedX, int expectedY)
    {
        var actual = EditorCoordinateConverter.CssToDevicePoint(120, 80, dpi);

        Assert.AreEqual(new Point(expectedX, expectedY), actual);
    }

    [TestMethod]
    [DataRow(96, 1.0, 120, 80)]
    [DataRow(96, 1.5, 180, 120)]
    [DataRow(96, 2.0, 240, 160)]
    [DataRow(120, 1.5, 225, 150)]
    public void CssToDevicePoint_ScalesForWebViewZoom(int dpi, double zoomFactor, int expectedX, int expectedY)
    {
        var actual = EditorCoordinateConverter.CssToDevicePoint(120, 80, dpi, zoomFactor);

        Assert.AreEqual(new Point(expectedX, expectedY), actual);
    }
}
