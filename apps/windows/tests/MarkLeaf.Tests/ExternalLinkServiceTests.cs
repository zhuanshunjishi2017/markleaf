using MarkLeaf.Services.ExternalLinks;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ExternalLinkServiceTests
{
    [TestMethod]
    [DataRow("https://example.com")]
    [DataRow("http://example.com/path")]
    [DataRow("mailto:test@example.com")]
    public void IsAllowed_AcceptsSafeProtocols(string value)
    {
        Assert.IsTrue(ExternalLinkService.IsAllowed(value));
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("file:///C:/secret.txt")]
    [DataRow("not-a-url")]
    public void IsAllowed_RejectsUnsafeOrRelativeValues(string value)
    {
        Assert.IsFalse(ExternalLinkService.IsAllowed(value));
    }
    [TestMethod]
    public void LocalTargets_AcceptExistingDirectoriesAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-links-" + Guid.NewGuid());
        var folder = Path.Combine(root, "会议记录");
        Directory.CreateDirectory(folder);
        try
        {
            var document = Path.Combine(root, "readme.md");
            File.WriteAllText(document, "hello");
            Assert.IsTrue(ExternalLinkService.IsOpenableLocalPath(folder));
            Assert.IsTrue(ExternalLinkService.IsOpenableLocalPath(document));
            Assert.IsFalse(ExternalLinkService.IsOpenableLocalPath(Path.Combine(root, "missing")));
            Assert.IsNull(ExternalLinkService.TryResolveLocalPath("会议记录/", null));
            foreach (var href in new[] { "会议记录/", "会议记录", Uri.EscapeDataString("会议记录") + "/", new Uri(folder).AbsoluteUri })
            {
                var resolved = ExternalLinkService.TryResolveLocalPath(href, document);
                Assert.IsNotNull(resolved);
                Assert.AreEqual(folder, Path.TrimEndingDirectorySeparator(resolved));
                Assert.IsTrue(ExternalLinkService.IsOpenableLocalPath(resolved));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
