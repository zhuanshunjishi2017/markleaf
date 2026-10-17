using MarkLeaf.Services;
using System.Net;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ImageOriginalExportServiceTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jX1sAAAAASUVORK5CYII=");

    [TestMethod]
    public async Task ReadLocalReturnsIdenticalBytes()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, Png);
            var result = await new ImageOriginalExportService().ReadAsync(path, null, CancellationToken.None);
            CollectionAssert.AreEqual(Png, result.Data);
            Assert.AreEqual("png", result.FileExtension);
            Assert.AreEqual("image/png", result.MediaType);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task DownloadNoExtensionUsesImageSignatureAndRejectsHtml()
    {
        using var client = new HttpClient(new Handler(Png));
        var result = await new ImageOriginalExportService(client).ReadAsync("https://image.test/resource", null, CancellationToken.None);
        CollectionAssert.AreEqual(Png, result.Data);
        Assert.AreEqual("png", result.FileExtension);
        using var html = new HttpClient(new Handler("<html>error</html>"u8.ToArray()));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new ImageOriginalExportService(html).ReadAsync("https://image.test/image.png", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task CancelledReadDoesNotTouchExistingTarget()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new Handler(Png));
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ImageOriginalExportService(client).ReadAsync("https://image.test/image", null, cancellation.Token));
    }

    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
