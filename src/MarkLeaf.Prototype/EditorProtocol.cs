using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkLeaf.Prototype;

internal static class EditorProtocol
{
    public const int Version = 1;

    public static string Serialize(
        string type,
        Guid documentId,
        long revision,
        object? payload = null,
        string? requestId = null)
    {
        return JsonSerializer.Serialize(new HostMessage(
            Version,
            type,
            requestId,
            documentId.ToString(),
            revision,
            payload), JsonOptions);
    }

    public static EditorMessage? Deserialize(string json)
    {
        return JsonSerializer.Deserialize<EditorMessage>(json, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record HostMessage(
        int ProtocolVersion,
        string Type,
        string? RequestId,
        string DocumentId,
        long Revision,
        object? Payload);
}

internal sealed record EditorMessage(
    int ProtocolVersion,
    string Type,
    string? RequestId,
    string DocumentId,
    long Revision,
    JsonElement Payload);

