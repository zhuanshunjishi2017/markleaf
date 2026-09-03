using System.Net.Http;
using System.Text.RegularExpressions;
using MarkLeaf.Services;

namespace MarkLeaf.Documents;

public sealed partial class ImageAssetService
{
    private const long MaximumImageBytes = 50L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };
    private readonly string _defaultDirectory;

    public ImageAssetService(string defaultDirectory)
    {
        _defaultDirectory = Path.GetFullPath(defaultDirectory);
    }

    public async Task<ImportedImage> ImportFileAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        await ValidateImageFileAsync(fullSourcePath, cancellationToken);
        return new ImportedImage(ToMarkdownPath(fullSourcePath), fullSourcePath);
    }

    public Task<ImportedImage> ImportBytesAsync(
        ReadOnlyMemory<byte> bytes,
        string extension,
        CancellationToken cancellationToken = default)
        => ImportBytesAsync(bytes, extension, _defaultDirectory, cancellationToken);

    public async Task<ImportedImage> ImportBytesAsync(
        ReadOnlyMemory<byte> bytes,
        string extension,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (!AllowedExtensions.Contains(normalizedExtension) || bytes.Length == 0 || bytes.Length > MaximumImageBytes)
        {
            throw new InvalidDataException("The image data is not supported or exceeds 50 MiB.");
        }
        ValidateImageSignature(bytes.Span, normalizedExtension);

        Directory.CreateDirectory(targetDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = CreateUniqueFileName(targetDirectory, $"clipboard-{timestamp}", normalizedExtension);
        var targetPath = Path.Combine(targetDirectory, fileName);
        try
        {
            await File.WriteAllBytesAsync(targetPath, bytes.ToArray(), cancellationToken);
            return new ImportedImage(ToMarkdownPath(targetPath), targetPath);
        }
        catch
        {
            TryDeleteFile(targetPath);
            throw;
        }
    }

    public async Task<ImportedImage> CopyFileIntoAsync(
        string sourcePath,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        await ValidateImageFileAsync(fullSourcePath, cancellationToken);
        Directory.CreateDirectory(targetDirectory);
        var baseName = Path.GetFileNameWithoutExtension(fullSourcePath);
        var extension = Path.GetExtension(fullSourcePath);
        var fileName = CreateUniqueFileName(targetDirectory, baseName, extension);
        var targetPath = Path.Combine(targetDirectory, fileName);
        await using var source = new FileStream(
            fullSourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous);
        await source.CopyToAsync(target, cancellationToken);
        return new ImportedImage(ToMarkdownPath(targetPath), targetPath);
    }

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    static ImageAssetService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MarkLeaf/1.0");
    }

    /// <summary>
    /// 从互联网 URL 下载图片，验证后保存到指定目录。
    /// </summary>
    public async Task<ImportedImage> DownloadImageAsync(
        string url,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(Loc.Get("image.urlOnly"), nameof(url));
        }

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var extension = ContentTypeToExtension(contentType)
            ?? Path.GetExtension(uri.AbsolutePath)
            ?? ".png";

        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidDataException(Loc.Format("image.unsupportedFormat", extension, string.Join("、", AllowedExtensions)));
        }

        var contentLength = response.Content.Headers.ContentLength ?? 0;
        if (contentLength > MaximumImageBytes)
        {
            throw new InvalidDataException(Loc.Get("image.tooLarge"));
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[contentLength > 0 ? (int)Math.Min(contentLength, MaximumImageBytes + 1) : 64 * 1024];
        using var memory = new MemoryStream();
        long totalRead = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalRead += read;
            if (totalRead > MaximumImageBytes)
            {
                throw new InvalidDataException(Loc.Get("image.tooLarge"));
            }
            memory.Write(buffer, 0, read);
        }

        var bytes = memory.ToArray();
        if (bytes.Length == 0)
        {
            throw new InvalidDataException(Loc.Get("image.downloadEmpty"));
        }

        ValidateImageSignature(bytes, extension);

        Directory.CreateDirectory(targetDirectory);
        var baseName = SanitizeFileName(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "image-online";
        }
        var fileName = CreateUniqueFileName(targetDirectory, baseName, extension);
        var targetPath = Path.Combine(targetDirectory, fileName);
        try
        {
            await File.WriteAllBytesAsync(targetPath, bytes, cancellationToken);
            return new ImportedImage(ToMarkdownPath(targetPath), targetPath);
        }
        catch
        {
            TryDeleteFile(targetPath);
            throw;
        }
    }

    private static string? ContentTypeToExtension(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        return contentType.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/svg+xml" => null, // SVG not supported
            _ => null,
        };
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "image";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Where(c => !invalid.Contains(c)).Take(64).ToArray();
        return chars.Length > 0 ? new string(chars) : "image";
    }

    public static bool DirectoryContainsImages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        foreach (var extension in AllowedExtensions)
        {
            if (Directory.EnumerateFiles(directory, $"*{extension}").Any())
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyList<MissingImage> FindMissingImages(string markdown, string? documentPath)
    {
        var missing = new Dictionary<string, MissingImage>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in MarkdownImageMatches(markdown))
        {
            var reference = match.Groups["path"].Value.Trim();
            var resolvedPath = TryResolveLocalImagePath(reference, documentPath);
            if (resolvedPath is null || File.Exists(resolvedPath))
            {
                continue;
            }

            missing.TryAdd(
                reference,
                new MissingImage(reference, resolvedPath, Path.GetFileName(resolvedPath)));
        }

        return missing.Values
            .OrderBy(image => image.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public string NormalizeLocalImagePaths(string markdown, string? documentPath, bool useRelativePaths = false, bool prefixDotSlash = false)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var match in MarkdownImageMatches(markdown))
        {
            var reference = match.Groups["path"].Value.Trim();
            var resolvedPath = TryResolveLocalImagePath(reference, documentPath);
            if (resolvedPath is null) continue;

            var normalized = useRelativePaths
                ? ToRelativeMarkdownPath(resolvedPath, documentPath, prefixDotSlash) ?? ToMarkdownPath(resolvedPath)
                : ToMarkdownPath(resolvedPath);
            replacements.TryAdd(reference, normalized);
        }

        return ReplaceImagePaths(markdown, replacements);
    }

    public static string ReplaceImagePaths(string markdown, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return markdown;
        }

        var codeRanges = FindMarkdownCodeRanges(markdown);
        return MarkdownImageReference().Replace(markdown, match =>
        {
            if (IsInsideRange(match.Index, codeRanges))
            {
                return match.Value;
            }

            var pathGroup = match.Groups["path"];
            if (!replacements.TryGetValue(pathGroup.Value.Trim(), out var replacement))
            {
                return match.Value;
            }

            var relativeStart = pathGroup.Index - match.Index;
            return match.Value[..relativeStart] + replacement + match.Value[(relativeStart + pathGroup.Length)..];
        });
    }

    private static IEnumerable<Match> MarkdownImageMatches(string markdown)
    {
        var codeRanges = FindMarkdownCodeRanges(markdown);
        return MarkdownImageReference()
            .Matches(markdown)
            .Cast<Match>()
            .Where(match => !IsInsideRange(match.Index, codeRanges));
    }

    private static IReadOnlyList<(int Start, int End)> FindMarkdownCodeRanges(string markdown)
    {
        var fencedCodeRanges = FindFencedCodeRanges(markdown);
        var ranges = new List<(int Start, int End)>(fencedCodeRanges);
        var index = 0;

        while (index < markdown.Length)
        {
            var fencedRange = RangeContainingOrAfter(index, fencedCodeRanges);
            if (fencedRange is { } fence && index >= fence.Start)
            {
                index = fence.End;
                continue;
            }

            if (markdown[index] != '`')
            {
                index++;
                continue;
            }

            var openerStart = index;
            var delimiterLength = CountRun(markdown, index, '`');
            index += delimiterLength;

            while (index < markdown.Length)
            {
                fencedRange = RangeContainingOrAfter(index, fencedCodeRanges);
                if (fencedRange is { } nextFence && index >= nextFence.Start)
                {
                    index = nextFence.End;
                    break;
                }

                var closingStart = markdown.IndexOf('`', index);
                if (closingStart < 0 || fencedRange is { } upcomingFence && closingStart >= upcomingFence.Start)
                {
                    index = fencedRange?.Start ?? markdown.Length;
                    break;
                }

                var closingLength = CountRun(markdown, closingStart, '`');
                if (closingLength == delimiterLength)
                {
                    var end = closingStart + closingLength;
                    ranges.Add((openerStart, end));
                    index = end;
                    break;
                }

                index = closingStart + closingLength;
            }
        }

        return ranges.OrderBy(range => range.Start).ToArray();
    }

    private static IReadOnlyList<(int Start, int End)> FindFencedCodeRanges(string markdown)
    {
        var ranges = new List<(int Start, int End)>();
        var lineStart = 0;
        var fenceStart = -1;
        var fenceCharacter = '\0';
        var fenceLength = 0;

        while (lineStart < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = markdown.Length;
            var contentEnd = lineEnd > lineStart && markdown[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
            var line = markdown.AsSpan(lineStart, contentEnd - lineStart);

            var indentation = 0;
            while (indentation < line.Length && indentation < 4 && line[indentation] == ' ')
                indentation++;

            if (indentation <= 3 && indentation < line.Length)
            {
                var marker = line[indentation];
                if (marker is '`' or '~')
                {
                    var markerLength = 1;
                    while (indentation + markerLength < line.Length
                        && line[indentation + markerLength] == marker)
                    {
                        markerLength++;
                    }

                    if (markerLength >= 3)
                    {
                        if (fenceStart < 0)
                        {
                            fenceStart = lineStart;
                            fenceCharacter = marker;
                            fenceLength = markerLength;
                        }
                        else if (marker == fenceCharacter && markerLength >= fenceLength
                            && line[(indentation + markerLength)..].Trim().Length == 0)
                        {
                            ranges.Add((fenceStart, lineEnd < markdown.Length ? lineEnd + 1 : lineEnd));
                            fenceStart = -1;
                            fenceCharacter = '\0';
                            fenceLength = 0;
                        }
                    }
                }
            }

            lineStart = lineEnd < markdown.Length ? lineEnd + 1 : markdown.Length;
        }

        if (fenceStart >= 0)
        {
            ranges.Add((fenceStart, markdown.Length));
        }

        return ranges;
    }

    private static bool IsInsideRange(int index, IReadOnlyList<(int Start, int End)> ranges)
    {
        foreach (var range in ranges)
        {
            if (index < range.Start) return false;
            if (index < range.End) return true;
        }
        return false;
    }

    private static (int Start, int End)? RangeContainingOrAfter(
        int index,
        IReadOnlyList<(int Start, int End)> ranges)
    {
        foreach (var range in ranges)
        {
            if (index < range.End)
                return range;
        }
        return null;
    }

    private static int CountRun(string text, int start, char value)
    {
        var end = start;
        while (end < text.Length && text[end] == value)
            end++;
        return end - start;
    }

    public static string? ResolveLocalImagePath(string reference, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(reference.Trim());
        }
        catch (UriFormatException)
        {
            decoded = reference.Trim();
        }

        if (Uri.TryCreate(decoded, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(uri.LocalPath);
        }

        var platformPath = decoded.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(platformPath))
        {
            return Path.GetFullPath(platformPath);
        }

        if (Uri.TryCreate(decoded, UriKind.Absolute, out _))
        {
            return null;
        }

        var documentDirectory = documentPath is null ? null : Path.GetDirectoryName(Path.GetFullPath(documentPath));
        return documentDirectory is null ? null : Path.GetFullPath(Path.Combine(documentDirectory, platformPath));
    }

    private static string? TryResolveLocalImagePath(string reference, string? documentPath)
    {
        try
        {
            return ResolveLocalImagePath(reference, documentPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static string? ToRelativeMarkdownPath(string absolutePhysicalPath, string? documentPath, bool prefixDotSlash = false)
    {
        if (string.IsNullOrWhiteSpace(documentPath)) return null;
        var docDir = Path.GetDirectoryName(Path.GetFullPath(documentPath));
        if (docDir is null) return null;

        var relative = Path.GetRelativePath(docDir, Path.GetFullPath(absolutePhysicalPath));
        if (Path.IsPathFullyQualified(relative)) return null;
        if (relative == ".") return null;
        if (relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        var markdownPath = EncodeMarkdownPathSpaces(relative.Replace('\\', '/'));
        if (prefixDotSlash) markdownPath = "./" + markdownPath;
        return markdownPath;
    }

    public static string ToMarkdownPath(string path)
    {
        var fullPath = Path.GetFullPath(path).Replace('\\', '/');
        return EncodeMarkdownPathSpaces(fullPath);
    }

    private static string EncodeMarkdownPathSpaces(string path)
        => path.Replace(" ", "%20", StringComparison.Ordinal);

    public static bool IsSupportedImagePath(string path)
    {
        return AllowedExtensions.Contains(Path.GetExtension(path));
    }

    public static async Task ValidateImageFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(Path.GetFullPath(path));
        if (!info.Exists || !AllowedExtensions.Contains(info.Extension) || info.Length > MaximumImageBytes)
        {
            throw new InvalidDataException("The selected file is not a supported image or exceeds 50 MiB.");
        }

        await using var source = info.OpenRead();
        await ValidateImageSignatureAsync(source, info.Extension, cancellationToken);
    }

    private static async Task ValidateImageSignatureAsync(
        Stream stream,
        string extension,
        CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var read = await stream.ReadAsync(header, cancellationToken);
        ValidateImageSignature(header.AsSpan(0, read), extension);
    }

    private static void ValidateImageSignature(ReadOnlySpan<byte> bytes, string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var valid = normalizedExtension switch
        {
            ".png" => bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ".jpg" or ".jpeg" => bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            ".gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            ".webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8)
                && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".bmp" => bytes.StartsWith("BM"u8),
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidDataException("The file contents do not match a supported image format.");
        }
    }

    private static string NormalizeExtension(string extension)
    {
        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    private static string CreateUniqueFileName(string directory, string baseName, string extension)
    {
        var candidate = baseName + extension;
        for (var suffix = 2; File.Exists(Path.Combine(directory, candidate)); suffix++)
        {
            candidate = $"{baseName}-{suffix}{extension}";
        }

        return candidate;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // Accept spaces in the common unbracketed destination as well as in the
    // angle-bracket form. The optional title is matched as a whole so a
    // filename such as "my photo.png" is not truncated at its first space.
    [GeneratedRegex("""!\[[^\]]*\]\(\s*(?:<(?<path>[^>]+)>|(?<path>[^)\r\n]+?))(?:\s+(?:\"[^\"]*\"|'[^']*'|\([^)]*\)))?\s*\)""")]
    private static partial Regex MarkdownImageReference();
}

public sealed record ImportedImage(string MarkdownPath, string PhysicalPath);

public sealed record MissingImage(string Reference, string ResolvedPath, string FileName);
