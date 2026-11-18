using MarkLeaf.Services;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ChangelogResourceTests
{
    [TestMethod]
    [DataRow("zh-CN", "changelog.md")]
    [DataRow("zh-TW", "changelog.zh-TW.md")]
    [DataRow("zh-HK", "changelog.zh-TW.md")]
    [DataRow("en-US", "changelog.en-US.md")]
    [DataRow("en-GB", "changelog.en-US.md")]
    [DataRow("ja-JP", "changelog.ja-JP.md")]
    [DataRow("de-DE", "changelog.md")]
    public void LanguageSelectsBundledTranslation(string language, string expected)
    {
        WithDirectories((source, cache) =>
        {
            foreach (var name in new[] { "changelog.md", "changelog.zh-TW.md", "changelog.en-US.md", "changelog.ja-JP.md" })
                File.WriteAllText(Path.Combine(source, name), name);
            var selected = ChangelogResource.PrepareCachedPath(source, cache, language);
            Assert.AreEqual(Path.Combine(cache, expected), selected);
            Assert.AreEqual(expected, File.ReadAllText(selected!));
            Assert.AreEqual("changelog.md", File.ReadAllText(Path.Combine(cache, "changelog.md")));
        });
    }

    [TestMethod]
    public void MissingTranslationFallsBackToChineseAndUpdatesCache()
    {
        WithDirectories((source, cache) =>
        {
            File.WriteAllText(Path.Combine(source, "changelog.md"), "latest history");
            Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "changelog.md"), "old history");
            var selected = ChangelogResource.PrepareCachedPath(source, cache, "en-US");
            Assert.AreEqual(Path.Combine(cache, "changelog.md"), selected);
            Assert.AreEqual("latest history", File.ReadAllText(selected!));
        });
    }

    [TestMethod]
    public void MissingAllResourcesReturnsNull()
    {
        WithDirectories((source, cache) => Assert.IsNull(ChangelogResource.PrepareCachedPath(source, cache, "en-US")));
    }

    private static void WithDirectories(Action<string, string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-changelog-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "resources");
        Directory.CreateDirectory(source);
        try { test(source, Path.Combine(root, "cache")); }
        finally { Directory.Delete(root, true); }
    }
}
