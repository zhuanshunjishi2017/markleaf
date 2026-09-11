using System.Globalization;
using System.Text;
using MarkLeaf.Documents;
using MarkLeaf.Services.Logging;

namespace MarkLeaf.Services.Recovery;

internal sealed class RecoveryService : IDisposable
{
    private readonly string _recoveryDirectory;
    private readonly IAppLogger _logger;
    private readonly int _ownerProcessId = Environment.ProcessId;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private bool _disposed;
    public event EventHandler<DateTimeOffset>? SnapshotSaved;

    public RecoveryService(string recoveryDirectory, IAppLogger logger)
    {
        _recoveryDirectory = recoveryDirectory;
        _logger = logger;
    }
    public string GetRecoveryPath(Guid documentId) => Path.Combine(_recoveryDirectory,
        DocumentCoreRuntime.Call<string>("recoveryFileName", new { owner = _ownerProcessId, documentId = documentId.ToString() }));

    public async Task WriteSnapshotAsync(RecoverySnapshot snapshot)
    {
        await _writes.WaitAsync().ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (_disposed) return;
            var content = DocumentCoreRuntime.Call<string>("serializeRecovery", new {
                documentId = snapshot.DocumentId.ToString(), documentPath = snapshot.DocumentPath,
                markdown = snapshot.Markdown, revision = snapshot.Revision.ToString(CultureInfo.InvariantCulture),
                timestamp = snapshot.Timestamp.ToString("O"), displayName = snapshot.DisplayName
            });
            Directory.CreateDirectory(_recoveryDirectory);
            var path = GetRecoveryPath(snapshot.DocumentId);
            temporary = path + $".{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false, true)).ConfigureAwait(false);
            File.Move(temporary, path, true);
            temporary = null;
            _logger.Info($"Recovery snapshot saved: {snapshot.DisplayName}.");
            SnapshotSaved?.Invoke(this, snapshot.Timestamp);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Recovery snapshot write failed for {snapshot.DisplayName}: {error.Message}");
        }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch (IOException error) { _logger.Warning(error.Message); } }
            _writes.Release();
        }
    }

    public void Delete(Guid documentId) => DeleteMatching(documentId.ToString(), null);
    public void DeleteOwnFiles() => DeleteMatching(null, _ownerProcessId);
    private void DeleteMatching(string? documentId, int? owner)
    {
        _writes.Wait();
        try
        {
            if (!Directory.Exists(_recoveryDirectory)) return;
            foreach (var path in Directory.EnumerateFiles(_recoveryDirectory))
                if (DocumentCoreRuntime.Call<bool>("ownsRecoveryFile", new { name = Path.GetFileName(path), documentId, owner })) File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.Warning($"Recovery cleanup failed: {error.Message}"); }
        finally { _writes.Release(); }
    }

    public static IReadOnlyList<RecoverySnapshot> GetPendingRecoveries(string recoveryDirectory, IAppLogger logger)
    {
        if (!Directory.Exists(recoveryDirectory)) return [];
        var records = new List<KernelRecovery>();
        foreach (var path in Directory.EnumerateFiles(recoveryDirectory))
        {
            if (!path.EndsWith(".recovery.json", StringComparison.Ordinal) && !path.EndsWith(".meta", StringComparison.Ordinal)) continue;
            try
            {
                if (!DocumentCoreRuntime.Call<bool>("ownsRecoveryFile", new { name = Path.GetFileName(path) })) continue;
                var content = File.ReadAllText(path, new UTF8Encoding(false, true));
                var legacyMarkdown = path.EndsWith(".meta", StringComparison.Ordinal) ? File.ReadAllText(path[..^5], new UTF8Encoding(false, true)) : null;
                var value = DocumentCoreRuntime.Call<KernelRecovery>("parseRecovery", new { content, legacyMarkdown });
                records.Add(value);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or OverflowException)
            {
                logger.Warning($"Recovery snapshot read failed for {Path.GetFileName(path)}: {error.Message}");
            }
        }
        return DocumentCoreRuntime.Call<KernelRecovery[]>("selectRecoveries", new { records }).Select(value => new RecoverySnapshot(
            Guid.Parse(value.DocumentId), value.DocumentPath, value.Markdown, long.Parse(value.Revision, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(value.Timestamp, CultureInfo.InvariantCulture), value.DisplayName)).ToArray();
    }
    public void Dispose() { _disposed = true; }
}
