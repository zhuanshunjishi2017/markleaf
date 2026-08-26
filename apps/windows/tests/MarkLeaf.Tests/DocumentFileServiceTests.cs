using System.Text;
using MarkLeaf.Documents;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class DocumentFileServiceTests
{
    private string _testDirectory = null!;
    private DocumentFileService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MarkLeaf.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _service = new DocumentFileService();
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
    public async Task OpenAsync_DetectsUtf8BomAndCrLf()
    {
        var path = Path.Combine(_testDirectory, "bom.md");
        await File.WriteAllTextAsync(path, "第一行\r\n第二行\r\n", new UTF8Encoding(true));

        var document = await _service.OpenAsync(path);

        Assert.IsTrue(document.HasBom);
        Assert.AreEqual("utf-8", document.Encoding.WebName);
        Assert.AreEqual("\r\n", document.NewLine);
        Assert.AreEqual("第一行\r\n第二行\r\n", document.Markdown);
        Assert.IsFalse(document.IsDirty);
    }

    [TestMethod]
    public async Task OpenAsync_DetectsUtf16LittleEndianAndLf()
    {
        var path = Path.Combine(_testDirectory, "utf16.txt");
        await File.WriteAllTextAsync(path, "alpha\nbeta\n", new UnicodeEncoding(false, true, true));

        var document = await _service.OpenAsync(path);

        Assert.IsTrue(document.HasBom);
        Assert.AreEqual("utf-16", document.Encoding.WebName);
        Assert.AreEqual("\n", document.NewLine);
        Assert.AreEqual("alpha\nbeta\n", document.Markdown);
    }

    [TestMethod]
    public async Task OpenAsync_DetectsUtf16WithoutBom()
    {
        var path = Path.Combine(_testDirectory, "utf16-no-bom.txt");
        await File.WriteAllBytesAsync(path, new UnicodeEncoding(false, false, true).GetBytes("alpha\nbeta"));

        var document = await _service.OpenAsync(path);

        Assert.IsFalse(document.HasBom);
        Assert.AreEqual("utf-16", document.Encoding.WebName);
        Assert.AreEqual("alpha\nbeta", document.Markdown);
    }

    [TestMethod]
    public async Task OpenAsync_DetectsGbkText()
    {
        var path = Path.Combine(_testDirectory, "gbk.md");
        var bytes = DocumentEncodingPolicy.Encode("中文内容€", DocumentEncodingPolicy.Gbk);
        await File.WriteAllBytesAsync(path, bytes);

        var document = await _service.OpenAsync(path);

        Assert.AreEqual(DocumentEncodingPolicy.Gbk.Id, document.EncodingPolicyId);
        Assert.AreEqual("中文内容€", document.Markdown);
    }

    [TestMethod]
    public async Task OpenAsync_CanReloadWithExplicitEncoding()
    {
        var path = Path.Combine(_testDirectory, "big5.md");
        await File.WriteAllBytesAsync(path, DocumentEncodingPolicy.Encode("繁體內容", DocumentEncodingPolicy.Big5));

        var document = await _service.OpenAsync(path, DocumentEncodingPolicy.Big5);

        Assert.AreEqual(DocumentEncodingPolicy.Big5.Id, document.EncodingPolicyId);
        Assert.AreEqual("繁體內容", document.Markdown);
    }

    [TestMethod]
    public async Task OpenAsync_WithExplicitEncoding_DoesNotModifySourceFile()
    {
        var path = Path.Combine(_testDirectory, "direct-read.md");
        var originalBytes = DocumentEncodingPolicy.Encode("磁盘内容", DocumentEncodingPolicy.Utf8);
        await File.WriteAllBytesAsync(path, originalBytes);
        var originalWriteTime = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, originalWriteTime);

        var document = await _service.OpenAsync(path, DocumentEncodingPolicy.Utf8);

        CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(path));
        Assert.AreEqual(originalWriteTime, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual("磁盘内容", document.Markdown);
        Assert.IsFalse(document.IsDirty);
    }

    [TestMethod]
    public async Task SaveAsync_PreservesEncodingBomAndNewLines()
    {
        var path = Path.Combine(_testDirectory, "preserve.md");
        await File.WriteAllTextAsync(path, "old\r\n", new UTF8Encoding(true));
        var document = await _service.OpenAsync(path);

        await _service.SaveAsync(document, "new\nline\n", 7, path);

        var bytes = await File.ReadAllBytesAsync(path);
        CollectionAssert.AreEqual(new byte[] { 0xef, 0xbb, 0xbf }, bytes[..3]);
        Assert.AreEqual("new\r\nline\r\n", Encoding.UTF8.GetString(bytes[3..]));
        Assert.AreEqual(7, document.Revision);
        Assert.AreEqual("new\r\nline\r\n", document.Markdown);
        Assert.IsNotNull(document.LastKnownFingerprint);
    }

    [TestMethod]
    public async Task SaveAsync_CreatesNewFileWithoutBomByDefault()
    {
        var path = Path.Combine(_testDirectory, "new-file");
        var document = _service.CreateNew();
        document.NewLine = "\n";

        await _service.SaveAsync(document, "content\n", 1, path);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.AreEqual("content\n", Encoding.UTF8.GetString(bytes));
        Assert.AreEqual(Path.GetFullPath(path), document.FilePath);
    }

    [TestMethod]
    public async Task SaveAsync_UsesSelectedNewDocumentEncoding()
    {
        var path = Path.Combine(_testDirectory, "gb18030.md");
        var document = _service.CreateNew("\n", NewDocumentKind.Markdown, DocumentEncodingPolicy.Gb18030);

        await _service.SaveAsync(document, "中文\n", 1, path);

        var reopened = await _service.OpenAsync(path, DocumentEncodingPolicy.Gb18030);
        Assert.AreEqual(DocumentEncodingPolicy.Gb18030.Id, reopened.EncodingPolicyId);
        Assert.AreEqual("中文\n", reopened.Markdown);
    }

    [TestMethod]
    public async Task SaveAsync_RejectsExternalModificationWithoutOverwritingIt()
    {
        var path = Path.Combine(_testDirectory, "conflict.md");
        await File.WriteAllTextAsync(path, "opened", new UTF8Encoding(false));
        var document = await _service.OpenAsync(path);
        await File.WriteAllTextAsync(path, "external", new UTF8Encoding(false));

        await Assert.ThrowsExactlyAsync<ExternalDocumentChangedException>(
            () => _service.SaveAsync(document, "editor", 2, path));

        Assert.AreEqual("external", await File.ReadAllTextAsync(path));
    }

    [TestMethod]
    public async Task SaveAsync_ForceOverwriteRequiresExplicitFlagAndRefreshesFingerprint()
    {
        var path = Path.Combine(_testDirectory, "force.md");
        await File.WriteAllTextAsync(path, "opened", new UTF8Encoding(false));
        var document = await _service.OpenAsync(path);
        await File.WriteAllTextAsync(path, "external", new UTF8Encoding(false));

        await _service.SaveAsync(document, "editor", 3, path, forceOverwrite: true);

        Assert.AreEqual("editor", await File.ReadAllTextAsync(path));
        Assert.IsFalse(document.IsDirty);
        Assert.AreEqual(3, document.Revision);
        Assert.IsFalse(await _service.HasExternalChangeAsync(document));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsReadOnlyTargetAndPreservesContent()
    {
        var path = Path.Combine(_testDirectory, "readonly.md");
        await File.WriteAllTextAsync(path, "original", new UTF8Encoding(false));
        var document = await _service.OpenAsync(path);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

        try
        {
            var exception = await Assert.ThrowsExactlyAsync<DocumentSaveException>(
                () => _service.SaveAsync(document, "changed", 1, path));

            Assert.IsInstanceOfType<UnauthorizedAccessException>(exception.InnerException);
            Assert.AreEqual("original", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [TestMethod]
    public async Task HasExternalChangeAsync_UsesContentInsteadOfTimestampOnly()
    {
        var path = Path.Combine(_testDirectory, "watch.md");
        await File.WriteAllTextAsync(path, "same", new UTF8Encoding(false));
        var document = await _service.OpenAsync(path);
        var originalWriteTime = File.GetLastWriteTimeUtc(path);

        await File.WriteAllTextAsync(path, "diff", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, originalWriteTime);

        Assert.IsTrue(await _service.HasExternalChangeAsync(document));
    }
}
