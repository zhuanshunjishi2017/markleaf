using System.Security.Cryptography;
using System.Security;

namespace MarkLeaf.Documents;

public sealed class DocumentFileService
{
    public MarkdownDocument CreateNew(
        string? newLine = null,
        NewDocumentKind kind = NewDocumentKind.Markdown,
        DocumentEncodingPolicy? encodingPolicy = null)
    {
        encodingPolicy ??= DocumentEncodingPolicy.Utf8;
        return new MarkdownDocument
        {
            Kind = kind,
            Encoding = encodingPolicy.CreateEncoding(),
            EncodingPolicyId = encodingPolicy.Id,
            HasBom = encodingPolicy.HasBom,
            NewLine = newLine ?? Environment.NewLine,
        };
    }

    public async Task<MarkdownDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return await OpenBytesAsync(fullPath, bytes, null, cancellationToken);
    }

    public async Task<MarkdownDocument> OpenAsync(string path, DocumentEncodingPolicy encodingPolicy, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return await OpenBytesAsync(fullPath, bytes, encodingPolicy.Id, cancellationToken);
    }

    private Task<MarkdownDocument> OpenBytesAsync(string fullPath, byte[] bytes, string? encoding, CancellationToken cancellationToken)
    {
        var content = DocumentCoreRuntime.Call<KernelDocument>("read", new { bytes = DocumentCoreRuntime.Bytes(bytes), encoding });
        var policy = DocumentEncodingPolicy.FromId(content.Encoding.Id);
        var info = new FileInfo(fullPath);
        return Task.FromResult(new MarkdownDocument
        {
            FilePath = fullPath,
            Kind = NewDocumentKindExtensions.FromExtension(Path.GetExtension(fullPath)),
            Markdown = content.Text,
            Encoding = policy.CreateEncoding(),
            EncodingPolicyId = policy.Id,
            HasBom = policy.HasBom,
            NewLine = NewLineValue(content.NewLine),
            IsReadOnly = info.IsReadOnly,
            LastKnownWriteTime = info.LastWriteTimeUtc,
            LastKnownFingerprint = new FileFingerprint(bytes.Length, info.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(bytes))),
        });
    }

    public async Task<bool> HasExternalChangeAsync(
        MarkdownDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document.FilePath is null || document.LastKnownFingerprint is null)
        {
            return false;
        }

        if (!File.Exists(document.FilePath))
        {
            return true;
        }

        var current = await CreateFingerprintAsync(document.FilePath, cancellationToken);
        return !document.LastKnownFingerprint.HasSameContent(current);
    }

    public async Task SaveAsync(
        MarkdownDocument document,
        string markdown,
        long revision,
        string targetPath,
        bool forceOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The target file has no parent directory.");
        Directory.CreateDirectory(directory);

        FileStream? targetLock = null;
        string? temporaryPath = null;
        try
        {
            var targetExists = File.Exists(fullPath);
            var sameTarget = document.FilePath is not null && PathEquals(document.FilePath, fullPath);
            var targetReadOnly = false;
            var contentChanged = false;
            if (targetExists)
            {
                var attributes = File.GetAttributes(fullPath);
                targetReadOnly = (attributes & FileAttributes.ReadOnly) != 0;

                targetLock = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                if (sameTarget && !forceOverwrite && document.LastKnownFingerprint is not null)
                {
                    var current = await CreateFingerprintAsync(targetLock, fullPath, cancellationToken);
                    contentChanged = !document.LastKnownFingerprint.HasSameContent(current);
                }
            }
            try
            {
                DocumentCoreRuntime.Call<bool>("validateSave", new { sameTarget, forceOverwrite, targetExists,
                    acceptedVersion = document.LastKnownFingerprint is not null, contentChanged, readOnly = targetReadOnly });
            }
            catch (DocumentKernelException error) when (error.Code == "external_change") { throw new ExternalDocumentChangedException(fullPath); }
            var prepared = DocumentCoreRuntime.Call<KernelPreparedSave>("prepareSave", new { text = markdown, encoding = document.EncodingPolicyId, newLine = document.NewLine });
            var normalizedMarkdown = prepared.Text;
            var encodingPolicy = DocumentEncodingPolicy.FromId(prepared.Encoding.Id);
            var contentBytes = prepared.Bytes.Select(value => checked((byte)value)).ToArray();
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.markleaf-{Guid.NewGuid():N}.tmp");

            await using (var temporary = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await temporary.WriteAsync(contentBytes, cancellationToken);
                await temporary.FlushAsync(cancellationToken);
                temporary.Flush(flushToDisk: true);
            }

            if (targetExists)
            {
                File.Replace(temporaryPath, fullPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }

            temporaryPath = null;
            var savedFingerprint = await CreateFingerprintAsync(fullPath, cancellationToken);
            document.FilePath = fullPath;
            document.Kind = NewDocumentKindExtensions.FromExtension(Path.GetExtension(fullPath));
            if (document.Revision <= revision) document.Markdown = normalizedMarkdown;
            document.Encoding = encodingPolicy.CreateEncoding();
            document.EncodingPolicyId = encodingPolicy.Id;
            document.HasBom = encodingPolicy.HasBom;
            document.Revision = Math.Max(document.Revision, revision);
            document.IsDirty = DocumentCoreRuntime.Call<bool>("dirtyAfterSave", new { savedRevision = revision.ToString(), currentRevision = document.Revision.ToString() });
            document.IsReadOnly = false;
            document.LastKnownWriteTime = savedFingerprint.LastWriteTime;
            document.LastKnownFingerprint = savedFingerprint;
        }
        catch (ExternalDocumentChangedException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new DocumentSaveException(
                "The document could not be saved safely.",
                temporaryPath is not null && File.Exists(temporaryPath) ? temporaryPath : null,
                exception);
        }
        finally
        {
            await (targetLock?.DisposeAsync() ?? ValueTask.CompletedTask);
        }
    }

    internal static string DetectNewLine(string text) => NewLineValue(DocumentCoreRuntime.Call<string>("newLine", new { text }));
    private static string NewLineValue(string style) => style switch { "CRLF" => "\r\n", "CR" => "\r", "Mixed" => "Mixed", _ => "\n" };
    internal static string NormalizeNewLines(string text, string newLine) => DocumentCoreRuntime.Call<string>("normalizeNewLines", new { text, style = newLine });

    private static async Task<FileFingerprint> CreateFingerprintAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await CreateFingerprintAsync(stream, path, cancellationToken);
    }

    private static async Task<FileFingerprint> CreateFingerprintAsync(
        FileStream stream,
        string path,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var info = new FileInfo(path);
        return new FileFingerprint(stream.Length, info.LastWriteTimeUtc, Convert.ToHexString(hash));
    }

    private static bool PathEquals(string first, string second)
    {
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    }

}
