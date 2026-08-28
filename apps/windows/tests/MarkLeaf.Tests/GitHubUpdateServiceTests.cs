using MarkLeaf.Services.Updates;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    [DataRow("1.4.0", 1, 4, 0)]
    [DataRow("v1.5.2", 1, 5, 2)]
    [DataRow("1.6.0-beta.1", 1, 6, 0)]
    public void TryParseVersion_AcceptsReleaseTags(string tag, int major, int minor, int build)
    {
        Assert.IsTrue(GitHubUpdateService.TryParseVersion(tag, out var version));
        Assert.AreEqual(new Version(major, minor, build), version);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("release")]
    [DataRow("v1")]
    public void TryParseVersion_RejectsInvalidTags(string tag)
    {
        Assert.IsFalse(GitHubUpdateService.TryParseVersion(tag, out _));
    }
}
