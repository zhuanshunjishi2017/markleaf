using System.Security.Cryptography;
using System.Security;
using System.Text;

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
        var detected = DocumentEncodingPolicy.Detect(bytes);
        return await OpenAsync(fullPath, bytes, detected.Policy, detected.PreambleLength, cancellationToken);
    }

    public async Task<MarkdownDocument> OpenAsync(
        string path,
        DocumentEncodingPolicy encodingPolicy,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var preambleLength = bytes.AsSpan().StartsWith(DocumentEncodingPolicy.GetPreamble(encodingPolicy))
            ? DocumentEncodingPolicy.GetPreamble(encodingPolicy).Length
            : 0;
        return await OpenAsync(fullPath, bytes, encodingPolicy, preambleLength, cancellationToken);
    }

    private async Task<MarkdownDocument> OpenAsync(
        string fullPath,
        byte[] bytes,
        DocumentEncodingPolicy encodingPolicy,
        int preambleLength,
        CancellationToken cancellationToken)
    {
        var markdown = DocumentEncodingPolicy.Decode(bytes, encodingPolicy);
        var info = new FileInfo(fullPath);

        return new MarkdownDocument
        {
            FilePath = fullPath,
            Kind = NewDocumentKindExtensions.FromExtension(Path.GetExtension(fullPath)),
            Markdown = markdown,
            Encoding = encodingPolicy.CreateEncoding(),
            EncodingPolicyId = encodingPolicy.Id,
            HasBom = encodingPolicy.HasBom || preambleLength > 0,
            NewLine = DetectNewLine(markdown),
            IsReadOnly = info.IsReadOnly,
            LastKnownWriteTime = info.LastWriteTimeUtc,
            LastKnownFingerprint = await CreateFingerprintAsync(fullPath, cancellationToken),
        };
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
        CancellationToken cancellationToken = default,
        DocumentEncodingPolicy? encodingOverride = null)
    {
        // Freeze the write's settings before any await; publish only after atomic replacement.
        var originalPath = document.FilePath;
        var originalEncoding = document.EncodingPolicyId;
        var originalBom = document.HasBom;
        var newLine = document.NewLine;
        var encodingPolicy = encodingOverride ?? DocumentEncodingPolicy.FromId(originalEncoding);
        var normalizedMarkdown = NormalizeNewLines(markdown, newLine);
        var contentBytes = encodingPolicy.CreateEncoding().GetBytes(normalizedMarkdown);
        var preamble = encodingPolicy.HasBom ? DocumentEncodingPolicy.GetPreamble(encodingPolicy) : [];
        var fullPath = Path.GetFullPath(targetPath);
        if ((document.IsReadOnly || document.IsUserReadOnly)
            && DocumentPathIdentity.Equals(document.FilePath, fullPath))
            throw new UnauthorizedAccessException("A read-only source can only be saved as a different file.");
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The target file has no parent directory.");
        Directory.CreateDirectory(directory);

        FileStream? targetLock = null;
        string? temporaryPath = null;
        try
        {
            var targetExists = File.Exists(fullPath);
            if (targetExists)
            {
                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    throw new UnauthorizedAccessException("The target file is read-only.");
                }

                targetLock = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                if (!forceOverwrite
                    && document.FilePath is not null
                    && PathEquals(document.FilePath, fullPath))
                {
                    if (document.LastKnownFingerprint is null)
                    {
                        throw new ExternalDocumentChangedException(fullPath);
                    }

                    var currentFingerprint = await CreateFingerprintAsync(targetLock, fullPath, cancellationToken);
                    if (!document.LastKnownFingerprint.HasSameContent(currentFingerprint))
                    {
                        throw new ExternalDocumentChangedException(fullPath);
                    }
                }
            }
            else if (!forceOverwrite
                && document.FilePath is not null
                && PathEquals(document.FilePath, fullPath)
                && document.LastKnownFingerprint is not null)
            {
                throw new ExternalDocumentChangedException(fullPath);
            }

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
                if (preamble.Length > 0)
                {
                    await temporary.WriteAsync(preamble, cancellationToken);
                }

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
            // A deleted/renamed backing file must not be silently reattached by an older save.
            if (!string.Equals(document.FilePath, originalPath, StringComparison.OrdinalIgnoreCase)) return;
            var metadataChanged = document.NewLine != newLine
                || document.EncodingPolicyId != originalEncoding || document.HasBom != originalBom;
            document.FilePath = fullPath;
            document.Kind = NewDocumentKindExtensions.FromExtension(Path.GetExtension(fullPath));
            if (document.Revision <= revision) document.Markdown = normalizedMarkdown;
            if (!metadataChanged)
            {
                document.Encoding = encodingPolicy.CreateEncoding();
                document.EncodingPolicyId = encodingPolicy.Id;
                document.HasBom = encodingPolicy.HasBom;
            }
            document.Revision = Math.Max(document.Revision, revision);
            document.IsDirty = document.Revision > revision || metadataChanged;
            document.HasUnsavedMetadataChanges = metadataChanged;
            document.IsReadOnly = false;
            document.IsUserReadOnly = false;
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

    public Task ConvertEncodingAsync(MarkdownDocument document, string markdown, long revision,
        DocumentEncodingPolicy target, CancellationToken cancellationToken = default)
    {
        if (document.IsReadOnly || document.IsUserReadOnly)
            throw new UnauthorizedAccessException("Read-only documents cannot change encoding.");
        var path = document.FilePath ?? throw new InvalidOperationException("An unnamed document has no disk encoding to convert.");
        return SaveAsync(document, markdown, revision, path, cancellationToken: cancellationToken,
            encodingOverride: target);
    }

    internal static string DetectNewLine(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    crlf++;
                    index++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[index] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
        {
            return Environment.NewLine;
        }

        var newlineKinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (newlineKinds > 1)
        {
            return "Mixed";
        }

        return crlf > 0 ? "\r\n" : lf > 0 ? "\n" : "\r";
    }

    internal static string NormalizeNewLines(string text, string newLine)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", newLine, StringComparison.Ordinal);
    }

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
