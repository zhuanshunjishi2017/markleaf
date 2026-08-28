using System.Text;
using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ImageAssetServiceTests
{
    private static readonly byte[] MinimalPng =
    [
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        0x00, 0x00, 0x00, 0x0d,
    ];

    private string _testDirectory = null!;
    private string _cacheDirectory = null!;
    private ImageAssetService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.ImageTests", Guid.NewGuid().ToString("N"));
        _cacheDirectory = Path.Combine(_testDirectory, "install", "Cache", "ClipboardImages");
        Directory.CreateDirectory(_testDirectory);
        _service = new ImageAssetService(_cacheDirectory);
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
    public async Task ImportFileAsync_UsesOriginalAbsolutePathWithoutCopying()
    {
        var source = Path.Combine(_testDirectory, "source image.png");
        await File.WriteAllBytesAsync(source, MinimalPng);

        var imported = await _service.ImportFileAsync(source);

        Assert.AreEqual(Path.GetFullPath(source), imported.PhysicalPath);
        Assert.AreEqual(ImageAssetService.ToMarkdownPath(source), imported.MarkdownPath);
        Assert.IsFalse(Directory.Exists(_cacheDirectory));
    }

    [TestMethod]
    public async Task ImportBytesAsync_WritesClipboardImageIntoInstallCache()
    {
        var first = await _service.ImportBytesAsync(MinimalPng, ".png");
        var second = await _service.ImportBytesAsync(MinimalPng, ".png");

        Assert.AreEqual(_cacheDirectory, Path.GetDirectoryName(first.PhysicalPath));
        Assert.AreNotEqual(first.PhysicalPath, second.PhysicalPath);
        Assert.IsTrue(File.Exists(first.PhysicalPath));
        Assert.AreEqual(ImageAssetService.ToMarkdownPath(first.PhysicalPath), first.MarkdownPath);
    }

    [TestMethod]
    public async Task ImportFileAsync_RejectsRenamedNonImageFile()
    {
        var source = Path.Combine(_testDirectory, "payload.png");
        await File.WriteAllTextAsync(source, "not an image", Encoding.UTF8);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => _service.ImportFileAsync(source));
    }

    [TestMethod]
    public void NormalizeLocalImagePaths_ConvertsRelativeReferencesToAbsolutePaths()
    {
        var documentPath = Path.Combine(_testDirectory, "notes", "document.md");
        var imagePath = Path.Combine(_testDirectory, "notes", "images", "示例 图片.png");

        var normalized = _service.NormalizeLocalImagePaths(
            "![示例](images/%E7%A4%BA%E4%BE%8B%20%E5%9B%BE%E7%89%87.png)",
            documentPath);

        StringAssert.Contains(normalized, ImageAssetService.ToMarkdownPath(imagePath));
    }

    [TestMethod]
    public async Task FindMissingImages_ListsMissingLocalFilesAndIgnoresRemoteUrls()
    {
        var existing = Path.Combine(_testDirectory, "existing.png");
        await File.WriteAllBytesAsync(existing, MinimalPng);
        var missing = Path.Combine(_testDirectory, "missing image.png");
        var markdown = $"![existing]({ImageAssetService.ToMarkdownPath(existing)})\n" +
            $"![missing]({ImageAssetService.ToMarkdownPath(missing)})\n" +
            "![remote](https://example.com/image.png)";

        var result = _service.FindMissingImages(markdown, Path.Combine(_testDirectory, "document.md"));

        Assert.HasCount(1, result);
        Assert.AreEqual("missing image.png", result[0].FileName);
        Assert.AreEqual(Path.GetFullPath(missing), result[0].ResolvedPath);
    }

    [TestMethod]
    public void FindMissingImages_IgnoresImageSyntaxInsideFencedCodeBlocks()
    {
        var outside = Path.Combine(_testDirectory, "outside.png");
        var markdown = $"```markdown\n![example](inside.png)\n```\n" +
            $"~~~\n![another](inside-too.png)\n~~~\n" +
            $"![missing]({ImageAssetService.ToMarkdownPath(outside)})";

        var result = _service.FindMissingImages(markdown, Path.Combine(_testDirectory, "document.md"));

        Assert.HasCount(1, result);
        Assert.AreEqual("outside.png", result[0].FileName);
    }

    [TestMethod]
    public void FindMissingImages_IgnoresImageSyntaxInsideInlineCode()
    {
        var outside = Path.Combine(_testDirectory, "outside.png");
        var markdown = "Use `![example](inside.png)` or ``![example](`inside.png`)`` as syntax.\n" +
            $"![missing]({ImageAssetService.ToMarkdownPath(outside)})";

        var result = _service.FindMissingImages(markdown, Path.Combine(_testDirectory, "document.md"));

        Assert.HasCount(1, result);
        Assert.AreEqual("outside.png", result[0].FileName);
    }

    [TestMethod]
    public void ReplaceImagePaths_RewritesOnlySelectedImageReferences()
    {
        const string missing = "C:/old/missing.png";
        var replacement = Path.Combine(_testDirectory, "replacement.png");
        var markdown = $"![missing]({missing})\n![kept](C:/old/kept.png)";

        var updated = ImageAssetService.ReplaceImagePaths(
            markdown,
            new Dictionary<string, string> { [missing] = ImageAssetService.ToMarkdownPath(replacement) });

        StringAssert.Contains(updated, ImageAssetService.ToMarkdownPath(replacement));
        StringAssert.Contains(updated, "C:/old/kept.png");
    }

    [TestMethod]
    public void ReplaceImagePaths_DoesNotRewriteImageSyntaxInsideFencedCodeBlocks()
    {
        const string missing = "C:/old/missing.png";
        var replacement = Path.Combine(_testDirectory, "replacement.png");
        var markdown = $"```markdown\n![example]({missing})\n```\n![missing]({missing})";

        var updated = ImageAssetService.ReplaceImagePaths(
            markdown,
            new Dictionary<string, string> { [missing] = ImageAssetService.ToMarkdownPath(replacement) });

        StringAssert.Contains(updated, $"```markdown\n![example]({missing})\n```");
        StringAssert.EndsWith(updated, $"![missing]({ImageAssetService.ToMarkdownPath(replacement)})");
    }

    [TestMethod]
    public void ReplaceImagePaths_DoesNotRewriteImageSyntaxInsideInlineCode()
    {
        const string missing = "C:/old/missing.png";
        var replacement = Path.Combine(_testDirectory, "replacement.png");
        var markdown = $"Use `![example]({missing})` here.\n![missing]({missing})";

        var updated = ImageAssetService.ReplaceImagePaths(
            markdown,
            new Dictionary<string, string> { [missing] = ImageAssetService.ToMarkdownPath(replacement) });

        StringAssert.Contains(updated, $"`![example]({missing})`");
        StringAssert.EndsWith(updated, $"![missing]({ImageAssetService.ToMarkdownPath(replacement)})");
    }
}
