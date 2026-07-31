using System.Text;
using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ImageAssetServiceTests
{
    private static readonly byte[] MinimalPng =
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 0];
    private string _testDirectory = null!;
    private ImageAssetService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.ImageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _service = new ImageAssetService(Path.Combine(_testDirectory, "drafts"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ImportFileAsync_UsesIsolatedDraftDirectoryAndResolvesCollision()
    {
        var source = Path.Combine(_testDirectory, "source image.png");
        await File.WriteAllBytesAsync(source, MinimalPng);
        var document = new MarkdownDocument();

        var first = await _service.ImportFileAsync(document, source);
        var second = await _service.ImportFileAsync(document, source);

        Assert.AreEqual("source image.png", first.RelativePath);
        Assert.AreEqual("source image-2.png", second.RelativePath);
        Assert.IsTrue(first.PhysicalPath.StartsWith(
            Path.Combine(_testDirectory, "drafts", document.Id.ToString("N")),
            StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("https://assets.local/source%20image.png", first.VirtualUrl);
    }

    [TestMethod]
    public async Task ImportFileAsync_RejectsUnsupportedFile()
    {
        var source = Path.Combine(_testDirectory, "payload.svg");
        await File.WriteAllTextAsync(source, "<svg><script /></svg>", Encoding.UTF8);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => _service.ImportFileAsync(new MarkdownDocument(), source));
    }

    [TestMethod]
    public async Task PrepareMigrationAsync_MovesDraftReferencesIntoDocumentAssetsWithoutDeletingDraft()
    {
        var document = new MarkdownDocument();
        var imported = await _service.ImportBytesAsync(document, MinimalPng, ".png");
        var targetDocument = Path.Combine(_testDirectory, "notes.md");
        var markdown = $"![粘贴图片]({imported.RelativePath})";

        var migration = await _service.PrepareMigrationAsync(document, targetDocument, markdown);

        StringAssert.Contains(migration.Markdown, "notes.assets/");
        Assert.HasCount(1, migration.CopiedFiles);
        Assert.IsTrue(File.Exists(migration.CopiedFiles[0]));
        Assert.IsTrue(File.Exists(imported.PhysicalPath));
        Assert.HasCount(1, migration.PathMappings);
    }

    [TestMethod]
    public async Task PrepareMigrationAsync_SaveAsCopiesAllAssetsAndRenamesCollision()
    {
        var originalDocumentPath = Path.Combine(_testDirectory, "original.md");
        var originalAssets = ImageAssetService.GetDocumentAssetDirectory(originalDocumentPath);
        Directory.CreateDirectory(originalAssets);
        await File.WriteAllBytesAsync(Path.Combine(originalAssets, "diagram.png"), MinimalPng);
        var document = new MarkdownDocument { FilePath = originalDocumentPath };
        var targetDocumentPath = Path.Combine(_testDirectory, "copy.md");
        var targetAssets = ImageAssetService.GetDocumentAssetDirectory(targetDocumentPath);
        Directory.CreateDirectory(targetAssets);
        await File.WriteAllBytesAsync(Path.Combine(targetAssets, "diagram.png"), [9]);

        var migration = await _service.PrepareMigrationAsync(
            document,
            targetDocumentPath,
            "![图](original.assets/diagram.png)");

        StringAssert.Contains(migration.Markdown, "copy.assets/diagram-2.png");
        Assert.IsTrue(File.Exists(Path.Combine(targetAssets, "diagram-2.png")));
        Assert.IsTrue(File.Exists(Path.Combine(originalAssets, "diagram.png")));
    }

    [TestMethod]
    public async Task RollbackMigration_RemovesOnlyNewCopies()
    {
        var document = new MarkdownDocument();
        var imported = await _service.ImportBytesAsync(document, MinimalPng, ".png");
        var migration = await _service.PrepareMigrationAsync(
            document,
            Path.Combine(_testDirectory, "target.md"),
            $"![图]({imported.RelativePath})");

        ImageAssetService.RollbackMigration(migration);

        Assert.IsFalse(File.Exists(migration.CopiedFiles[0]));
        Assert.IsTrue(File.Exists(imported.PhysicalPath));
    }

    [TestMethod]
    public async Task PrepareMigrationAsync_FailureRollsBackCopiesCreatedEarlierInTheBatch()
    {
        var document = new MarkdownDocument();
        var draftDirectory = _service.GetAssetDirectory(document);
        Directory.CreateDirectory(draftDirectory);
        await File.WriteAllBytesAsync(Path.Combine(draftDirectory, "a-valid.png"), MinimalPng);
        await File.WriteAllTextAsync(Path.Combine(draftDirectory, "z-invalid.png"), "not an image");
        var targetDocument = Path.Combine(_testDirectory, "notes.md");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            _service.PrepareMigrationAsync(document, targetDocument, "![image](a-valid.png)"));

        var targetAssets = ImageAssetService.GetDocumentAssetDirectory(targetDocument);
        Assert.IsFalse(Directory.Exists(targetAssets) && Directory.EnumerateFiles(targetAssets).Any());
        Assert.IsTrue(File.Exists(Path.Combine(draftDirectory, "a-valid.png")));
    }

    [TestMethod]
    public async Task ImportFileAsync_RejectsRenamedNonImageFile()
    {
        var source = Path.Combine(_testDirectory, "payload.png");
        await File.WriteAllTextAsync(source, "not an image", Encoding.UTF8);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => _service.ImportFileAsync(new MarkdownDocument(), source));
    }

    [TestMethod]
    public async Task FindUnreferencedAssets_ReturnsOnlyManagedFilesMissingFromMarkdown()
    {
        var documentPath = Path.Combine(_testDirectory, "notes.md");
        var assets = ImageAssetService.GetDocumentAssetDirectory(documentPath);
        Directory.CreateDirectory(assets);
        await File.WriteAllBytesAsync(Path.Combine(assets, "used image.png"), MinimalPng);
        await File.WriteAllBytesAsync(Path.Combine(assets, "unused.png"), MinimalPng);
        var document = new MarkdownDocument { FilePath = documentPath };

        var unused = _service.FindUnreferencedAssets(
            document,
            "![used](notes.assets/used%20image.png)\n![external](../elsewhere.png)");

        Assert.HasCount(1, unused);
        Assert.AreEqual("unused.png", Path.GetFileName(unused[0]));
        Assert.IsTrue(File.Exists(unused[0]));
    }

    [TestMethod]
    public async Task FindUnreferencedAssets_DoesNotDeleteFileWhenReferenceIsRemoved()
    {
        var document = new MarkdownDocument();
        var imported = await _service.ImportBytesAsync(document, MinimalPng, ".png");

        var unused = _service.FindUnreferencedAssets(document, string.Empty);

        CollectionAssert.Contains(unused.ToArray(), imported.PhysicalPath);
        Assert.IsTrue(File.Exists(imported.PhysicalPath));
    }

    [TestMethod]
    public async Task DeleteUnreferencedAssets_PermanentlyDeletesOnlyUnusedManagedImages()
    {
        var documentPath = Path.Combine(_testDirectory, "notes.md");
        var assets = ImageAssetService.GetDocumentAssetDirectory(documentPath);
        Directory.CreateDirectory(assets);
        var usedPath = Path.Combine(assets, "used.png");
        var unusedPath = Path.Combine(assets, "unused.png");
        await File.WriteAllBytesAsync(usedPath, MinimalPng);
        await File.WriteAllBytesAsync(unusedPath, MinimalPng);
        var document = new MarkdownDocument { FilePath = documentPath };

        var deleted = _service.DeleteUnreferencedAssets(
            document,
            "![used](notes.assets/used.png)");

        CollectionAssert.AreEqual(new[] { unusedPath }, deleted.ToArray());
        Assert.IsTrue(File.Exists(usedPath));
        Assert.IsFalse(File.Exists(unusedPath));
    }
}
