using System.Text.Json;
using System.Xml;

namespace MarkLeaf.Editor;

public sealed record MermaidImageExportPayload(byte[] Data, string FileExtension, string MediaType, int Width, int Height)
{
    public static MermaidImageExportPayload Parse(JsonElement payload)
    {
        try
        {
            if (payload.TryGetProperty("error", out var error)) throw new InvalidDataException(error.GetString());
            var encoded = payload.GetProperty("base64").GetString()!;
            var ext = payload.GetProperty("fileExtension").GetString();
            var mime = payload.GetProperty("mediaType").GetString();
            var width = payload.GetProperty("width").GetInt32();
            var height = payload.GetProperty("height").GetInt32();
            if (encoded.Length >= 16 * 1024 * 1024 - 1024 || width <= 0 || height <= 0
                || !((ext == "png" && mime == "image/png") || (ext == "svg" && mime == "image/svg+xml"))) throw new InvalidDataException("Invalid image export metadata.");
            var bytes = Convert.FromBase64String(encoded);
            if (ext == "png")
            {
                if (bytes.Length < 24 || !bytes.AsSpan().StartsWith(new byte[] { 137,80,78,71,13,10,26,10 })) throw new InvalidDataException("Invalid PNG.");
            }
            else
            {
                using var xml = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                if (xml.MoveToContent() != XmlNodeType.Element || xml.LocalName != "svg") throw new InvalidDataException("Invalid SVG.");
                while (xml.Read()) { }
            }
            return new(bytes, ext!, mime!, width, height);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or KeyNotFoundException or XmlException or OverflowException or ArgumentNullException)
        { throw new InvalidDataException("Invalid image export payload.", exception); }
    }
}
