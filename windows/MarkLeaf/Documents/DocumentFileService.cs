using System.Security.Cryptography;
using System.Security;
using System.Text;

namespace MarkLeaf.Documents;

public sealed class DocumentFileService
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(false, false, true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(true, false, true);

    public MarkdownDocument CreateNew(string? newLine = null)
    {
        return new MarkdownDocument
        {
            Encoding = StrictUtf8,
            NewLine = newLine ?? Environment.NewLine,
        };
    }

    public async Task<MarkdownDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        var detected = DetectTextFormat(bytes);
        var markdown = detected.Encoding.GetString(bytes, detected.PreambleLength, bytes.Length - detected.PreambleLength);
        var info = new FileInfo(fullPath);

        return new MarkdownDocument
        {
            FilePath = fullPath,
            Markdown = markdown,
            Encoding = detected.Encoding,
            HasBom = detected.PreambleLength > 0,
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

            var normalizedMarkdown = NormalizeNewLines(markdown, document.NewLine);
            var contentBytes = document.Encoding.GetBytes(normalizedMarkdown);
            var preamble = document.HasBom ? GetPreamble(document.Encoding) : [];
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
            document.FilePath = fullPath;
            document.Markdown = normalizedMarkdown;
            document.Revision = revision;
            document.IsDirty = false;
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

        return crlf >= lf && crlf >= cr ? "\r\n" : lf >= cr ? "\n" : "\r";
    }

    internal static string NormalizeNewLines(string text, string newLine)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", newLine, StringComparison.Ordinal);
    }

    private static DetectedTextFormat DetectTextFormat(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        {
            return new DetectedTextFormat(StrictUtf8, 3);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }))
        {
            return new DetectedTextFormat(StrictUtf16LittleEndian, 2);
        }

        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }))
        {
            return new DetectedTextFormat(StrictUtf16BigEndian, 2);
        }

        if (LooksLikeUtf16(bytes, littleEndian: true))
        {
            return new DetectedTextFormat(StrictUtf16LittleEndian, 0);
        }

        if (LooksLikeUtf16(bytes, littleEndian: false))
        {
            return new DetectedTextFormat(StrictUtf16BigEndian, 0);
        }

        try
        {
            _ = StrictUtf8.GetString(bytes);
            return new DetectedTextFormat(StrictUtf8, 0);
        }
        catch (DecoderFallbackException) when (LooksLikeUtf16(bytes, littleEndian: true))
        {
            return new DetectedTextFormat(StrictUtf16LittleEndian, 0);
        }
        catch (DecoderFallbackException) when (LooksLikeUtf16(bytes, littleEndian: false))
        {
            return new DetectedTextFormat(StrictUtf16BigEndian, 0);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The file is not valid UTF-8 or UTF-16 text.", exception);
        }
    }

    private static bool LooksLikeUtf16(byte[] bytes, bool littleEndian)
    {
        if (bytes.Length < 2 || bytes.Length % 2 != 0)
        {
            return false;
        }

        var expectedNulls = 0;
        var samplePairs = Math.Min(bytes.Length / 2, 512);
        for (var index = 0; index < samplePairs; index++)
        {
            var nullIndex = index * 2 + (littleEndian ? 1 : 0);
            if (bytes[nullIndex] == 0)
            {
                expectedNulls++;
            }
        }

        return expectedNulls >= Math.Max(1, samplePairs / 3);
    }

    private static byte[] GetPreamble(Encoding encoding)
    {
        return encoding.CodePage switch
        {
            65001 => [0xef, 0xbb, 0xbf],
            1200 => [0xff, 0xfe],
            1201 => [0xfe, 0xff],
            _ => encoding.GetPreamble(),
        };
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

    private sealed record DetectedTextFormat(Encoding Encoding, int PreambleLength);
}
