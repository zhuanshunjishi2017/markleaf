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
}
