using System.Text.Json;
using MarkLeaf.Services.Logging;

namespace MarkLeaf.Services.Recovery;

internal sealed class RecoveryService : IDisposable
{
    private readonly string _recoveryDirectory;
    private readonly IAppLogger _logger;
    private readonly int _ownerProcessId;
    private bool _disposed;

    public event EventHandler<DateTimeOffset>? SnapshotSaved;

    public RecoveryService(string recoveryDirectory, IAppLogger logger)
    {
        _recoveryDirectory = recoveryDirectory;
        _logger = logger;
        _ownerProcessId = Environment.ProcessId;
    }

    public string GetRecoveryPath(Guid documentId)
    {
        return Path.Combine(_recoveryDirectory, $"doc-{_ownerProcessId}-{documentId:N}.md");
    }

    public async Task WriteSnapshotAsync(RecoverySnapshot snapshot)
    {
        if (_disposed) return;
        try
        {
            Directory.CreateDirectory(_recoveryDirectory);
            var path = GetRecoveryPath(snapshot.DocumentId);
            var tempPath = path + $".{Guid.NewGuid():N}.tmp";
            var metaPath = path + ".meta";
            var metaTempPath = metaPath + $".{Guid.NewGuid():N}.tmp";

            await File.WriteAllTextAsync(tempPath, snapshot.Markdown, System.Text.Encoding.UTF8);
            File.Move(tempPath, path, true);

            var meta = JsonSerializer.Serialize(new
            {
                documentId = snapshot.DocumentId.ToString(),
                documentPath = snapshot.DocumentPath,
                revision = snapshot.Revision,
                timestamp = snapshot.Timestamp.ToString("O"),
                displayName = snapshot.DisplayName,
            });
            await File.WriteAllTextAsync(metaTempPath, meta, System.Text.Encoding.UTF8);
            File.Move(metaTempPath, metaPath, true);

            _logger.Info($"Recovery snapshot saved: {snapshot.DisplayName}.");
            SnapshotSaved?.Invoke(this, snapshot.Timestamp);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Recovery snapshot write failed for {snapshot.DisplayName}: {exception.Message}");
            CleanupTempFiles(snapshot.DocumentId);
        }
    }

    public void Delete(Guid documentId)
    {
        try
        {
            var path = GetRecoveryPath(documentId);
            var metaPath = path + ".meta";
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not delete recovery for {documentId}: {exception.Message}");
        }
    }

    public void DeleteOwnFiles()
    {
        try
        {
            var prefix = $"doc-{_ownerProcessId}-";
            if (Directory.Exists(_recoveryDirectory))
            {
                foreach (var file in Directory.GetFiles(_recoveryDirectory, $"{prefix}*"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not clean own recovery files: {exception.Message}");
        }
    }

    public static IReadOnlyList<RecoverySnapshot> GetPendingRecoveries(
        string recoveryDirectory, IAppLogger logger)
    {
        var result = new List<RecoverySnapshot>();
        logger.Info($"Scanning for recoveries in: {recoveryDirectory}");

        if (!Directory.Exists(recoveryDirectory))
        {
            logger.Info("Recovery directory does not exist.");
            return result;
        }

        var metaFiles = Directory.GetFiles(recoveryDirectory, "doc-*.meta");
        logger.Info($"Found {metaFiles.Length} recovery meta file(s).");

        foreach (var metaFile in metaFiles)
        {
            var fileName = Path.GetFileName(metaFile);
            try
            {
                var json = File.ReadAllText(metaFile, System.Text.Encoding.UTF8);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!TryGetPropertyEither(root, "documentId", "DocumentId", out var idEl)
                    || !idEl.TryGetGuid(out var id))
                {
                    logger.Warning($"Recovery meta missing valid documentId: {fileName}");
                    continue;
                }

                var dataPath = metaFile.EndsWith(".meta", StringComparison.Ordinal)
                    ? metaFile[..^5]
                    : metaFile;
                if (!File.Exists(dataPath))
                {
                    logger.Warning($"Recovery data file missing: {Path.GetFileName(dataPath)}");
                    continue;
                }
                if (new FileInfo(dataPath).Length == 0)
                {
                    logger.Warning($"Recovery data file is empty: {Path.GetFileName(dataPath)}");
                    continue;
                }

                TryGetPropertyEither(root, "documentPath", "DocumentPath", out var dpEl);
                TryGetPropertyEither(root, "revision", "Revision", out var revEl);
                TryGetPropertyEither(root, "timestamp", "Timestamp", out var tsEl);
                TryGetPropertyEither(root, "displayName", "DisplayName", out var dnEl);

                result.Add(new RecoverySnapshot(
                    id,
                    dpEl.ValueKind == JsonValueKind.String ? dpEl.GetString() : null,
                    File.ReadAllText(dataPath, System.Text.Encoding.UTF8),
                    revEl.TryGetInt64(out var r) ? r : 0,
                    tsEl.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(tsEl.GetString(), out var dt) ? dt : DateTimeOffset.UtcNow,
                    dnEl.ValueKind == JsonValueKind.String ? dnEl.GetString() : null));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException
                or KeyNotFoundException)
            {
                logger.Warning($"Skipped unreadable recovery meta: {fileName}; {exception.Message}");
            }
        }

        logger.Info($"Recovery scan complete: {result.Count} valid snapshot(s) found.");
        return result;
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static bool TryGetPropertyEither(
        JsonElement root, string camel, string pascal, out JsonElement value)
    {
        return root.TryGetProperty(camel, out value)
            || root.TryGetProperty(pascal, out value);
    }

    private void CleanupTempFiles(Guid documentId)
    {
        try
        {
            foreach (var file in Directory.GetFiles(_recoveryDirectory, $"doc-{_ownerProcessId}-{documentId:N}.*.tmp"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }
}
