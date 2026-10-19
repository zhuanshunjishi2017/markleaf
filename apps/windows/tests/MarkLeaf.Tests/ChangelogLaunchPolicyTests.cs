using System.Text.Json;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ChangelogLaunchPolicyTests
{
    [TestMethod]
    public void UpgradeFromVersionWithoutMarkerOpensChangelogOnce()
    {
        Assert.IsTrue(ChangelogLaunchPolicy.ShouldOpen("1.7.8", null, settingsFileExisted: true));
        Assert.IsTrue(ChangelogLaunchPolicy.ShouldOpen("1.7.8 (Build 700)", null, settingsFileExisted: true));
    }

    [TestMethod]
    public void FreshInstallationDoesNotOpenChangelog()
    {
        Assert.IsFalse(ChangelogLaunchPolicy.ShouldOpen("1.7.8", null, settingsFileExisted: false));
    }

    [TestMethod]
    public void NewerVersionOpensChangelogAndSameVersionDoesNot()
    {
        Assert.IsTrue(ChangelogLaunchPolicy.ShouldOpen("1.8.0", "1.7.10", settingsFileExisted: true));
        Assert.IsFalse(ChangelogLaunchPolicy.ShouldOpen("1.7.8", "1.7.8", settingsFileExisted: true));
        Assert.IsFalse(ChangelogLaunchPolicy.ShouldOpen("1.7.8", "1.7.8 (Build 700)", settingsFileExisted: true));
    }

    [TestMethod]
    public void ChangelogMarkerSurvivesSettingsSerialization()
    {
        var settings = new AppSettings
        {
            LastChangelogShownVersion = "1.7.8",
        };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Contains("lastChangelogShownVersion", json);
        Assert.AreEqual("1.7.8", JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        })?.LastChangelogShownVersion);
    }
}
