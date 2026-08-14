using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkLeaf.Editor;

public static class EditorProtocol
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 1024 * 1024;

    private static readonly HashSet<string> AllowedEditorMessageTypes =
    [
        "ready",
        "documentLoaded",
        "commandResult",
        "dirtyChanged",
        "snapshot",
        "selectionChanged",
        "commandStateChanged",
        "editorStatusChanged",
        "contextMenuRequested",
        "blockMenuRequested",
        "mathEditRequested",
        "outlineChanged",
        "outlineSelectionChanged",
        "requestSave",
        "openLink",
        "dropFiles",
        "pasteImage",
        "findResult",
        "selectionExport",
        "exportContent",
        "zoomWheel",
        "error",
    ];

    private static readonly HashSet<string> AllowedHostMessageTypes =
    [
        "loadDocument",
        "requestSnapshot",
        "command",
        "applyStyles",
        "localizeFindBar",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeHostMessage(
        string type,
        Guid documentId,
        long revision,
        object? payload = null,
        string? requestId = null)
    {
        if (!AllowedHostMessageTypes.Contains(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported host message type.");
        }

        return JsonSerializer.Serialize(
            new HostMessage(
                Version,
                type,
                requestId,
                documentId.ToString(),
                revision,
                payload),
            JsonOptions);
    }

    public static bool TryDeserializeEditorMessage(
        string json,
        out EditorMessage? message,
        out string? error)
    {
        message = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Message is empty.";
            return false;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumMessageBytes)
        {
            error = "Message exceeds the size limit.";
            return false;
        }

        try
        {
            message = JsonSerializer.Deserialize<EditorMessage>(json, JsonOptions);
            if (message is null)
            {
                error = "Message is null.";
                return false;
            }

            if (message.ProtocolVersion != Version)
            {
                error = "Unsupported protocol version.";
                message = null;
                return false;
            }

            if (!AllowedEditorMessageTypes.Contains(message.Type))
            {
                error = "Unsupported editor message type.";
                message = null;
                return false;
            }

            if (!Guid.TryParse(message.DocumentId, out _))
            {
                error = "Document ID is invalid.";
                message = null;
                return false;
            }

            if (message.Revision < 0)
            {
                error = "Revision cannot be negative.";
                message = null;
                return false;
            }

            if (!HasValidPayload(message))
            {
                error = "Message payload is invalid.";
                message = null;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            error = "Message JSON is invalid.";
            return false;
        }
    }

    private static bool HasValidPayload(EditorMessage message)
    {
        var payload = message.Payload;
        return message.Type switch
        {
            "ready" or "documentLoaded" => IsMissingOrObject(payload),
            "dirtyChanged" => HasProperty(payload, "dirty", JsonValueKind.True, JsonValueKind.False),
            "snapshot" => HasProperty(payload, "markdown", JsonValueKind.String),
            "selectionChanged" => HasIntegerProperty(payload, "from") && HasIntegerProperty(payload, "to"),
            "commandStateChanged" => HasCommandStatePayload(payload),
            "editorStatusChanged" => HasEditorStatusPayload(payload),
            "contextMenuRequested" => HasNonNegativeNumber(payload, "clientX")
                && HasNonNegativeNumber(payload, "clientY")
                && HasOptionalNonNegativeNumber(payload, "menuHeight"),
            "blockMenuRequested" => HasNonNegativeNumber(payload, "clientX")
                && HasNonNegativeNumber(payload, "clientY")
                && HasNonNegativeInteger(payload, "position"),
            "mathEditRequested" => IsMissingOrObject(payload),
            "outlineChanged" => HasOutlinePayload(payload),
            "outlineSelectionChanged" => HasNullableNonNegativeInteger(payload, "position"),
            "findResult" => HasFindResultPayload(payload),
            "selectionExport" => HasSelectionExportPayload(payload),
            "exportContent" => HasProperty(payload, "html", JsonValueKind.String),
            "zoomWheel" => HasNonZeroNumber(payload, "deltaY"),
            "openLink" => HasAllowedUrl(payload),
            "dropFiles" => HasBoundedCount(payload)
                && HasNonNegativeNumber(payload, "clientX")
                && HasNonNegativeNumber(payload, "clientY"),
            "commandResult" => HasProperty(payload, "success", JsonValueKind.True, JsonValueKind.False),
            "pasteImage" => IsMissingOrObject(payload),
            "error" => HasProperty(payload, "message", JsonValueKind.String),
            _ => payload.ValueKind == JsonValueKind.Object,
        };
    }

    private static bool IsMissingOrObject(JsonElement payload)
    {
        return payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object;
    }

    private static bool HasProperty(JsonElement payload, string name, params JsonValueKind[] allowedKinds)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && allowedKinds.Contains(value.ValueKind);
    }

    private static bool HasIntegerProperty(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            && number >= 0;
    }

    private static bool HasCommandStatePayload(JsonElement payload)
    {
        return HasBooleanProperty(payload, "canUndo")
            && HasBooleanProperty(payload, "canRedo")
            && HasBooleanProperty(payload, "hasSelection")
            && HasBooleanProperty(payload, "paragraph")
            && HasNullableHeadingLevel(payload)
            && HasBooleanProperty(payload, "bold")
            && HasBooleanProperty(payload, "italic")
            && HasBooleanProperty(payload, "link")
            && HasBooleanProperty(payload, "blockquote")
            && HasBooleanProperty(payload, "codeBlock")
            && HasBooleanProperty(payload, "bulletList")
            && HasBooleanProperty(payload, "orderedList")
            && HasBooleanProperty(payload, "taskList")
            && HasBooleanProperty(payload, "inTable")
            && HasNullableEnum(payload, "tableAlign", "left", "center", "right")
            && HasBooleanProperty(payload, "imageSelected")
            && HasBooleanProperty(payload, "mathInline")
            && HasBooleanProperty(payload, "mathBlock")
            && HasBooleanProperty(payload, "sourceMode")
            && HasNullableString(payload, "mathLatex");
    }

    private static bool HasFindResultPayload(JsonElement payload)
    {
        return HasNonNegativeInteger(payload, "current")
            && HasNonNegativeInteger(payload, "total")
            && (!payload.TryGetProperty("replaced", out var replaced)
                || replaced.ValueKind == JsonValueKind.Number && replaced.TryGetInt32(out var count) && count >= 0);
    }

    private static bool HasSelectionExportPayload(JsonElement payload)
    {
        return HasProperty(payload, "text", JsonValueKind.String)
            && HasProperty(payload, "markdown", JsonValueKind.String)
            && HasProperty(payload, "html", JsonValueKind.String);
    }

    private static bool HasEditorStatusPayload(JsonElement payload)
    {
        return HasNonNegativeInteger(payload, "characterCount")
            && HasNonNegativeInteger(payload, "selectedCharacterCount")
            && HasAllowedBlockType(payload)
            && HasPositiveInteger(payload, "line")
            && HasPositiveInteger(payload, "column");
    }

    private static bool HasOutlinePayload(JsonElement payload)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("headings", out var headings)
            && headings.ValueKind == JsonValueKind.Array
            && headings.GetArrayLength() <= 10000
            && headings.EnumerateArray().All(heading =>
                heading.ValueKind == JsonValueKind.Object
                && heading.TryGetProperty("level", out var level)
                && level.TryGetInt32(out var levelValue)
                && levelValue is >= 1 and <= 6
                && heading.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
                && (text.GetString()?.Length ?? 0) <= 1000
                && heading.TryGetProperty("position", out var position)
                && position.TryGetInt32(out var positionValue)
                && positionValue >= 0);
    }

    private static bool HasAllowedBlockType(JsonElement payload)
    {
        if (!HasProperty(payload, "blockType", JsonValueKind.String))
        {
            return false;
        }

        return payload.GetProperty("blockType").GetString() is
            "paragraph" or "heading1" or "heading2" or "heading3" or "heading4" or "heading5" or "heading6"
            or "blockquote" or "codeBlock" or "bulletList" or "orderedList" or "taskList" or "table" or "image";
    }

    private static bool HasNonNegativeInteger(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            && number >= 0;
    }

    private static bool HasNullableNonNegativeInteger(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && (value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number)
                && number >= 0);
    }

    private static bool HasPositiveInteger(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            && number > 0;
    }

    private static bool HasAllowedUrl(JsonElement payload)
    {
        if (!HasProperty(payload, "url", JsonValueKind.String))
        {
            return false;
        }

        var value = payload.GetProperty("url").GetString();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" or "mailto";
    }

    private static bool HasStringArray(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var values)
            && values.ValueKind == JsonValueKind.Array
            && values.GetArrayLength() is > 0 and <= 32
            && values.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String);
    }

    private static bool HasBoundedCount(JsonElement payload)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("count", out var count)
            && count.ValueKind == JsonValueKind.Number
            && count.TryGetInt32(out var value)
            && value is > 0 and <= 32;
    }

    private static bool HasNonNegativeNumber(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
            && number >= 0;
    }

    private static bool HasOptionalNonNegativeNumber(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
            && number >= 0;
    }

    private static bool HasNonZeroNumber(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
            && double.IsFinite(number)
            && number != 0;
    }

    private static bool HasBooleanProperty(JsonElement payload, string name)
    {
        return HasProperty(payload, name, JsonValueKind.True, JsonValueKind.False);
    }

    private static bool HasNullableString(JsonElement payload, string name)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.String;
    }

    private static bool HasNullableHeadingLevel(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("headingLevel", out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var level)
            && level is >= 1 and <= 6;
    }

    private static bool HasNullableEnum(JsonElement payload, string name, params string[] allowedValues)
    {
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.String
            && allowedValues.Contains(value.GetString(), StringComparer.Ordinal);
    }

    private sealed record HostMessage(
        int ProtocolVersion,
        string Type,
        string? RequestId,
        string DocumentId,
        long Revision,
        object? Payload);
}
