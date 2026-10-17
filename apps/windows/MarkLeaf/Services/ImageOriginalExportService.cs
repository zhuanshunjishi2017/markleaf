using System.Xml;

namespace MarkLeaf.Services;

internal sealed record ImageOriginalExport(byte[] Data, string FileExtension, string MediaType);

internal sealed class ImageOriginalExportService(HttpClient? client = null)
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<ImageOriginalExport> ReadAsync(string source, string? documentPath, CancellationToken cancellationToken)
    {
        byte[] bytes;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            using var response = await (client ?? SharedClient).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme is not ("http" or "https") && response.RequestMessage is not null)
                throw new InvalidDataException("Unsupported redirect.");
            if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("Image too large.");
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (output.Length + count > MaximumBytes) throw new InvalidDataException("Image too large.");
                output.Write(buffer, 0, count);
            }
            bytes = output.ToArray();
        }
        else
        {
            var path = Uri.UnescapeDataString(source);
            if (uri?.IsFile == true) path = uri.LocalPath;
            else if (!Path.IsPathRooted(path) && uri is not null) throw new InvalidDataException("Unsupported image source.");
            if (!Path.IsPathRooted(path)) path = Path.Combine(Path.GetDirectoryName(documentPath) ?? Environment.CurrentDirectory, path);
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Image too large.");
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var kind = Detect(bytes);
        return new(bytes, kind.Extension, kind.Type);
    }

    public static async Task WriteAsync(ImageOriginalExport image, string target, CancellationToken token = default)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, $".markleaf-image-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, image.Data, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static (string Extension, string Type) Detect(byte[] data)
    {
        var bytes = data.AsSpan();
        if (bytes.StartsWith(new byte[] { 137,80,78,71,13,10,26,10 }) && bytes.Length >= 24) return ("png", "image/png");
        if (bytes.StartsWith(new byte[] { 255,216,255 }) && bytes.Length >= 4) return ("jpg", "image/jpeg");
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return ("gif", "image/gif");
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8,4).SequenceEqual("WEBP"u8)) return ("webp", "image/webp");
        if (bytes.StartsWith("BM"u8) && bytes.Length >= 26) return ("bmp", "image/bmp");
        if (bytes.StartsWith(new byte[] { 73,73,42,0 }) || bytes.StartsWith(new byte[] { 77,77,0,42 })) return ("tif", "image/tiff");
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(data), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            if (reader.MoveToContent() == XmlNodeType.Element && reader.LocalName == "svg")
            {
                while (reader.Read()) { }
                return ("svg", "image/svg+xml");
            }
        }
        catch (XmlException) { }
        throw new InvalidDataException("The resource is not a supported image.");
    }
}
