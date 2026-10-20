using MarkLeaf.Services.Updates;
using System.Text.Json;

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

    [TestMethod]
    public void InstallerSelectionPreservesTheCurrentDeploymentMode()
    {
        const string json = """
            [
              {"name":"MarkLeaf-1.7.9-win-x64.exe","browser_download_url":"https://example.invalid/slim.exe"},
              {"name":"MarkLeaf-1.7.9-win-x64-with-runtime.exe","browser_download_url":"https://example.invalid/runtime.exe"}
            ]
            """;
        var assets = JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();

        Assert.IsTrue(GitHubUpdateService.TrySelectInstaller(
            assets, "win-x64", preferSelfContained: false,
            out var slimName, out var slimUri));
        Assert.AreEqual("MarkLeaf-1.7.9-win-x64.exe", slimName);
        Assert.AreEqual("https://example.invalid/slim.exe", slimUri.ToString());

        Assert.IsTrue(GitHubUpdateService.TrySelectInstaller(
            assets, "win-x64", preferSelfContained: true,
            out var runtimeName, out var runtimeUri));
        Assert.AreEqual("MarkLeaf-1.7.9-win-x64-with-runtime.exe", runtimeName);
        Assert.AreEqual("https://example.invalid/runtime.exe", runtimeUri.ToString());
    }

    [TestMethod]
    public void SelfContainedUpdateDoesNotFallBackToTheSlimInstaller()
    {
        const string json = """
            [{"name":"MarkLeaf-1.7.9-win-x64.exe","browser_download_url":"https://example.invalid/slim.exe"}]
            """;
        var assets = JsonDocument.Parse(json).RootElement.EnumerateArray().ToArray();

        Assert.IsFalse(GitHubUpdateService.TrySelectInstaller(
            assets, "win-x64", preferSelfContained: true,
            out _, out _));
    }
}
