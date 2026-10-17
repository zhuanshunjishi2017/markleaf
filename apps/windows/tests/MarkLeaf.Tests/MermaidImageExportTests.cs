using System.Text.Json;
using MarkLeaf.Editor;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class MermaidImageExportTests
{
    [TestMethod]
    public void ValidatesMimeExtensionBase64AndBounds()
    {
        var svg = Convert.ToBase64String("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());
        var valid = new { mediaType = "image/svg+xml", fileExtension = "svg", base64 = svg, width = 320, height = 180 };
        Assert.AreEqual(320, MermaidImageExportPayload.Parse(JsonSerializer.SerializeToElement(valid)).Width);
        foreach (var invalid in new[] {
            new { mediaType = "image/png", fileExtension = "svg", base64 = svg, width = 320, height = 180 },
            valid with { base64 = "!" }, valid with { width = 0 }, valid with { base64 = new string('A', 16 * 1024 * 1024) },
        }) Assert.ThrowsExactly<InvalidDataException>(() => MermaidImageExportPayload.Parse(JsonSerializer.SerializeToElement(invalid)));
    }
}
