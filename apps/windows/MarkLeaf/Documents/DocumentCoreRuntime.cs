using System.Text;
using System.Text.Json;
using Jint;

namespace MarkLeaf.Documents;

/// <summary>Loads the shared DOM-free bundle and supplies only OS codec primitives.</summary>
internal static class DocumentCoreRuntime
{
    private static readonly object Gate = new();
    private static Engine? _engine;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static T Call<T>(string method, object? payload = null)
    {
        lock (Gate)
        {
            if (_engine is null)
            {
                var engine = new Engine();
                engine.SetValue("__markleafDocumentCodec", new Func<string, string>(ConvertCodec));
                engine.Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DocumentCore", "document-kernel.cjs")));
                _engine = engine;
            }
            var request = JsonSerializer.Serialize(new { method, payload = payload ?? new { } }, JsonOptions);
            var api = _engine.GetValue("MarkLeafDocumentCore").AsObject();
            var result = _engine.Invoke(api.Get("invoke"), request).AsString();
            using var envelope = JsonDocument.Parse(result);
            if (!envelope.RootElement.GetProperty("ok").GetBoolean())
            {
                var error = envelope.RootElement.GetProperty("error");
                throw new DocumentKernelException(error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
            }
            return envelope.RootElement.GetProperty("value").Deserialize<T>(JsonOptions)!;
        }
    }

    private static string ConvertCodec(string request)
    {
        using var input = JsonDocument.Parse(request);
        var root = input.RootElement;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var page = root.GetProperty("codec").GetString() switch
        {
            "utf-8" => 65001, "utf-16le" => 1200, "utf-16be" => 1201,
            "gb18030" => 54936, "gbk" => 936, "gb2312" => 20936,
            "big5" => 950, "shift_jis" => 932, "us-ascii" => 20127,
            _ => throw new InvalidOperationException("Unknown codec requested by document kernel")
        };
        var codec = Encoding.GetEncoding(page, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            if (root.GetProperty("operation").GetString() == "decode")
                return JsonSerializer.Serialize(codec.GetString(root.GetProperty("bytes").EnumerateArray().Select(x => x.GetByte()).ToArray()));
            // JSON byte[] is base64 in .NET; the shared boundary explicitly carries byte values.
            return JsonSerializer.Serialize(codec.GetBytes(root.GetProperty("text").GetString()!).Select(x => (int)x).ToArray());
        }
        catch (Exception error) when (error is EncoderFallbackException or DecoderFallbackException) { return "null"; }
    }

    public static int[] Bytes(byte[] bytes) => bytes.Select(value => (int)value).ToArray();
}

internal sealed class DocumentKernelException(string code, string message) : IOException(message)
{
    public string Code { get; } = code;
}
internal sealed record KernelEncoding(string Id, string Label, string Codec, int CodePage, int[] Bom);
internal sealed record KernelDocument(string Text, KernelEncoding Encoding, string NewLine);
internal sealed record KernelPreparedSave(string Text, int[] Bytes, KernelEncoding Encoding);
internal sealed record KernelSearchMatch(bool Matched, bool IsContentMatch, string Snippet);
internal sealed record KernelRecovery(string DocumentId, string? DocumentPath, string Markdown, string Revision, string Timestamp, string? DisplayName, string? Encoding, string? NewLine);
