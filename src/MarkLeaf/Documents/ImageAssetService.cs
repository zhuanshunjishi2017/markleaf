using System.Text.RegularExpressions;

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
        foreach (Match match in MarkdownImageReference().Matches(markdown))
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

    public string NormalizeLocalImagePaths(string markdown, string? documentPath)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in MarkdownImageReference().Matches(markdown))
        {
            var reference = match.Groups["path"].Value.Trim();
            var resolvedPath = TryResolveLocalImagePath(reference, documentPath);
            if (resolvedPath is not null)
            {
                replacements.TryAdd(reference, ToMarkdownPath(resolvedPath));
            }
        }

        return ReplaceImagePaths(markdown, replacements);
    }

    public static string ReplaceImagePaths(string markdown, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return markdown;
        }

        return MarkdownImageReference().Replace(markdown, match =>
        {
            var pathGroup = match.Groups["path"];
            if (!replacements.TryGetValue(pathGroup.Value.Trim(), out var replacement))
            {
                return match.Value;
            }

            var relativeStart = pathGroup.Index - match.Index;
            return match.Value[..relativeStart] + replacement + match.Value[(relativeStart + pathGroup.Length)..];
        });
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

    public static string ToMarkdownPath(string path)
    {
        var fullPath = Path.GetFullPath(path).Replace('\\', '/');
        return string.Join(
            "/",
            fullPath.Split('/').Select(segment => segment.EndsWith(':')
                ? segment
                : Uri.EscapeDataString(segment)));
    }

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

    [GeneratedRegex("""!\[[^\]]*\]\(\s*(?:<(?<path>[^>]+)>|(?<path>[^\s)]+))""")]
    private static partial Regex MarkdownImageReference();
}

public sealed record ImportedImage(string MarkdownPath, string PhysicalPath);

public sealed record MissingImage(string Reference, string ResolvedPath, string FileName);
