using MarkLeaf.Documents;
using MarkLeaf.Native;
using MarkLeaf.Workspace;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class WorkspaceServiceTests
{
    [TestInitialize]
    public void Setup() => LocTestHelper.EnsureInitialized();

    [TestMethod]
    public async Task GetChildrenAsync_SortsFoldersBeforeFilesAndSkipsHiddenEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "z-folder"));
            Directory.CreateDirectory(Path.Combine(root, "a-folder"));
            await File.WriteAllTextAsync(Path.Combine(root, "b.md"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "a.md"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "ignored.json"), string.Empty);
            var hidden = Path.Combine(root, "hidden.md");
            await File.WriteAllTextAsync(hidden, string.Empty);
            File.SetAttributes(hidden, FileAttributes.Hidden);

            var entries = await new WorkspaceService().GetChildrenAsync(root);

            CollectionAssert.AreEqual(
                new[] { "a-folder", "z-folder", "a.md", "b.md", "notes.txt" },
                entries.Select(entry => entry.Name).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void ShellIconSize_UsesDpiSpecificImageListSizes()
    {
        Assert.AreEqual(16, ShellIconService.GetIconSize(96));
        Assert.AreEqual(20, ShellIconService.GetIconSize(120));
        Assert.AreEqual(24, ShellIconService.GetIconSize(144));
        Assert.AreEqual(32, ShellIconService.GetIconSize(192));
    }

    [TestMethod]
    public async Task WorkspaceChangeDebouncer_CollapsesRepeatedSignals()
    {
        var count = 0;
        using var fired = new ManualResetEventSlim();
        using var debouncer = new WorkspaceChangeDebouncer(
            TimeSpan.FromMilliseconds(50),
            () =>
            {
                Interlocked.Increment(ref count);
                fired.Set();
            });

        for (var index = 0; index < 20; index++)
        {
            debouncer.Signal();
        }

        Assert.IsTrue(fired.Wait(TimeSpan.FromSeconds(2)));
        await Task.Delay(100);
        Assert.AreEqual(1, count);
    }

    [TestMethod]
    public async Task GetChildrenAsync_HandlesLargeFoldersWithoutDroppingEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (var index = 0; index < 1000; index++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"file-{index:D4}.md"), string.Empty);
            }

            var entries = await new WorkspaceService().GetChildrenAsync(root);

            Assert.HasCount(1000, entries);
            Assert.AreEqual("file-0000.md", entries[0].Name);
            Assert.AreEqual("file-0999.md", entries[^1].Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task GetChildrenAsync_IncludesMarkdownCaseInsensitively()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "upper.MD"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "markdown.markdown"), string.Empty);

            var entries = await new WorkspaceService().GetChildrenAsync(root);

            CollectionAssert.AreEqual(new[] { "upper.MD" }, entries.Select(entry => entry.Name).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task GetChildrenAsync_IncludesTextFilesCaseInsensitively()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "notes.TXT"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "ignored.json"), string.Empty);

            var entries = await new WorkspaceService().GetChildrenAsync(root);

            CollectionAssert.AreEqual(new[] { "notes.TXT" }, entries.Select(entry => entry.Name).ToArray());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task GetDocumentsAsync_RecursesAndIncludesMarkdownAndTextFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "子目录");
        Directory.CreateDirectory(child);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "root.md"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(child, "child.txt"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(child, "ignored.json"), string.Empty);

            var documents = await new WorkspaceService().GetDocumentsAsync(root);

            Assert.HasCount(2, documents);
            CollectionAssert.AreEquivalent(
                new[] { "root.md", "child.txt" },
                documents.Select(document => document.Name).ToArray());
            Assert.AreEqual("子目录", documents.Single(document => document.Name == "child.txt").FolderName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task GetDocumentsAsync_ReturnsPlainTextPreviewFromDocumentStart()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "preview.md"),
                "---\ntitle: hidden\n---\n# 标题 **粗体** [链接](https://example.com) `代码`");

            var documents = await new WorkspaceService().GetDocumentsAsync(root);

            Assert.HasCount(1, documents);
            Assert.AreEqual("标题 粗体 链接 代码", documents[0].Preview);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task GetDocumentsAsync_RefreshesCachedPreviewAfterExplicitInvalidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "preview.md");
            var service = new WorkspaceService();
            await File.WriteAllTextAsync(path, "# 旧预览");
            var first = await service.GetDocumentsAsync(root);

            await File.WriteAllTextAsync(path, "# 新的预览内容");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
            var cached = await service.GetDocumentsAsync(root);
            service.InvalidatePreview(path);
            var refreshed = await service.GetDocumentsAsync(root);

            Assert.AreEqual("旧预览", first[0].Preview);
            Assert.AreEqual("旧预览", cached[0].Preview);
            Assert.AreEqual("新的预览内容", refreshed[0].Preview);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_SearchesVisibleMarkdownTextAndBuildsTwoCharacterContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "search.md"),
                "# 开头甲乙**目标文字**结尾 [链接](https://example.com)");

            var results = await new WorkspaceService().SearchAsync(root, "目标");

            Assert.HasCount(1, results);
            Assert.IsTrue(results[0].IsContentMatch);
            Assert.AreEqual("甲乙目标文字结尾 链接", results[0].Snippet);
            Assert.AreEqual("目标", results[0].Query);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_DoesNotMatchMarkdownMarkersOrLinkDestinations()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "search.md"),
                "# 标题 **正文** [链接](https://marker-only.example.com)");

            var markerResults = await new WorkspaceService().SearchAsync(root, "**");
            var destinationResults = await new WorkspaceService().SearchAsync(root, "marker-only");

            Assert.IsEmpty(markerResults);
            Assert.IsEmpty(destinationResults);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_FilenameMatchUsesDocumentPreviewWithoutOpeningFind()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "目标文件.md"), "## 第一段 *内容*");

            var results = await new WorkspaceService().SearchAsync(root, "目标");

            Assert.HasCount(1, results);
            Assert.IsFalse(results[0].IsContentMatch);
            Assert.AreEqual("第一段 内容", results[0].Snippet);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SearchAsync_PlainTextKeepsLiteralPunctuation()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "notes.txt"), "普通 **星号** 文本");

            var results = await new WorkspaceService().SearchAsync(root, "**星号**");

            Assert.HasCount(1, results);
            Assert.IsTrue(results[0].IsContentMatch);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void WorkspaceDocumentTimeFormatter_FormatsRelativeAndCalendarDates()
    {
        var now = new DateTime(2026, 8, 1, 16, 30, 0);

        Assert.AreEqual("今天 08:15", WorkspaceDocumentTimeFormatter.Format(new DateTime(2026, 8, 1, 8, 15, 0), now));
        Assert.AreEqual("昨天 12:29", WorkspaceDocumentTimeFormatter.Format(new DateTime(2026, 7, 31, 12, 29, 0), now));
        Assert.AreEqual("7月30日", WorkspaceDocumentTimeFormatter.Format(new DateTime(2026, 7, 30, 9, 8, 0), now));
        Assert.AreEqual("7月29日", WorkspaceDocumentTimeFormatter.Format(new DateTime(2026, 7, 29, 9, 8, 0), now));
        Assert.AreEqual("7月25日", WorkspaceDocumentTimeFormatter.Format(new DateTime(2026, 7, 25, 9, 8, 0), now));
        Assert.AreEqual("3月9日", WorkspaceDocumentTimeFormatter.Format(new DateTime(2026, 3, 9, 9, 8, 0), now));
        Assert.AreEqual("2025/9/12", WorkspaceDocumentTimeFormatter.Format(new DateTime(2025, 9, 12, 9, 8, 0), now));
    }

    [TestMethod]
    public void GetAvailableUntitledDocumentPath_SkipsExistingNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "未命名.md"), string.Empty);
            Directory.CreateDirectory(Path.Combine(root, "未命名 (2).md"));

            var path = new WorkspaceService().GetAvailableUntitledDocumentPath(root);

            Assert.AreEqual(Path.Combine(root, "未命名 (3).md"), path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public void GetAvailableUntitledDocumentPath_PlainTextUsesTxtExtension()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "未命名.txt"), string.Empty);

            var path = new WorkspaceService().GetAvailableUntitledDocumentPath(
                root,
                NewDocumentKind.PlainText);

            Assert.AreEqual(Path.Combine(root, "未命名 (2).txt"), path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void GetAvailableUntitledDirectoryPath_SkipsExistingNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "markleaf-workspace-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "未命名文件夹"));

            var path = new WorkspaceService().GetAvailableUntitledDirectoryPath(root);

            Assert.AreEqual(Path.Combine(root, "未命名文件夹 (2)"), path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
