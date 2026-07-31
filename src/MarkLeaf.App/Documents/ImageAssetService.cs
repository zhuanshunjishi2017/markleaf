using System.Text.RegularExpressions;

namespace MarkLeaf.Documents;

public sealed partial class ImageAssetService
{
    private const long MaximumImageBytes = 50L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };
    private readonly string _draftRoot;

    public ImageAssetService(string draftRoot)
    {
        _draftRoot = Path.GetFullPath(draftRoot);
    }

    public string GetAssetDirectory(MarkdownDocument document)
    {
        return document.FilePath is null
            ? Path.Combine(_draftRoot, document.Id.ToString("N"))
            : GetDocumentAssetDirectory(document.FilePath);
    }

    public static string GetDocumentAssetDirectory(string documentPath)
    {
        var fullPath = Path.GetFullPath(documentPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException("The document has no parent directory.");
        var name = Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(fullPath);
        }

        return Path.Combine(directory, name + ".assets");
    }

    public async Task<ImportedImage> ImportFileAsync(
        MarkdownDocument document,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var info = new FileInfo(fullSourcePath);
        if (!info.Exists || !AllowedExtensions.Contains(info.Extension) || info.Length > MaximumImageBytes)
        {
            throw new InvalidDataException("The selected file is not a supported image or exceeds 50 MiB.");
        }

        await using (var source = info.OpenRead())
        {
            await ValidateImageSignatureAsync(source, info.Extension, cancellationToken);
        }

        var targetDirectory = GetAssetDirectory(document);
        Directory.CreateDirectory(targetDirectory);
        var targetFileName = CreateUniqueFileName(targetDirectory, SanitizeBaseName(info.Name), info.Extension);
        var targetPath = Path.Combine(targetDirectory, targetFileName);
        await CopyFileAsync(fullSourcePath, targetPath, cancellationToken);
        return CreateImportedImage(document, targetFileName, targetPath);
    }

    public async Task<ImportedImage> ImportBytesAsync(
        MarkdownDocument document,
        ReadOnlyMemory<byte> bytes,
        string extension,
        CancellationToken cancellationToken = default)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (!AllowedExtensions.Contains(normalizedExtension) || bytes.Length == 0 || bytes.Length > MaximumImageBytes)
        {
            throw new InvalidDataException("The clipboard image is not supported or exceeds 50 MiB.");
        }
        ValidateImageSignature(bytes.Span, normalizedExtension);

        var targetDirectory = GetAssetDirectory(document);
        Directory.CreateDirectory(targetDirectory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var targetFileName = CreateUniqueFileName(targetDirectory, $"image-{timestamp}", normalizedExtension);
        var targetPath = Path.Combine(targetDirectory, targetFileName);
        try
        {
            await File.WriteAllBytesAsync(targetPath, bytes.ToArray(), cancellationToken);
            return CreateImportedImage(document, targetFileName, targetPath);
        }
        catch
        {
            TryDeleteFile(targetPath);
            throw;
        }
    }

    public async Task<AssetMigration> PrepareMigrationAsync(
        MarkdownDocument document,
        string targetDocumentPath,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        var sourceDirectory = GetAssetDirectory(document);
        var targetDirectory = GetDocumentAssetDirectory(targetDocumentPath);
        if (PathEquals(sourceDirectory, targetDirectory) || !Directory.Exists(sourceDirectory))
        {
            return new AssetMigration(markdown, targetDirectory, [], new Dictionary<string, string>());
        }

        var copiedFiles = new List<string>();
        try
        {
            Directory.CreateDirectory(targetDirectory);
            var pathMappings = new Dictionary<string, string>(StringComparer.Ordinal);
            var rewrittenMarkdown = markdown;
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(sourcePath);
                if (!AllowedExtensions.Contains(extension))
                {
                    continue;
                }

                await using (var source = File.OpenRead(sourcePath))
                {
                    await ValidateImageSignatureAsync(source, extension, cancellationToken);
                }

                var originalName = Path.GetFileName(sourcePath);
                var targetName = CreateUniqueFileName(
                    targetDirectory,
                    Path.GetFileNameWithoutExtension(originalName),
                    extension);
                var targetPath = Path.Combine(targetDirectory, targetName);
                await CopyFileAsync(sourcePath, targetPath, cancellationToken);
                copiedFiles.Add(targetPath);

                var sourceReference = document.FilePath is null
                    ? originalName
                    : $"{Path.GetFileName(sourceDirectory)}/{originalName}";
                var targetReference = $"{Path.GetFileName(targetDirectory)}/{targetName}";
                rewrittenMarkdown = ReplaceImageReference(rewrittenMarkdown, sourceReference, targetReference);
                pathMappings[sourceReference.Replace('\\', '/')] = targetReference.Replace('\\', '/');
            }

            return new AssetMigration(rewrittenMarkdown, targetDirectory, copiedFiles, pathMappings);
        }
        catch
        {
            RollbackMigration(new AssetMigration(markdown, targetDirectory, copiedFiles, new Dictionary<string, string>()));
            throw;
        }
    }

    public IReadOnlyList<string> FindUnreferencedAssets(MarkdownDocument document, string markdown)
    {
        var assetDirectory = GetAssetDirectory(document);
        if (!Directory.Exists(assetDirectory))
        {
            return [];
        }

        var referencedNames = ExtractManagedImageFileNames(document, markdown);
        return Directory.EnumerateFiles(assetDirectory)
            .Where(path => AllowedExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !referencedNames.Contains(Path.GetFileName(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> DeleteUnreferencedAssets(MarkdownDocument document, string markdown)
    {
        var assets = FindUnreferencedAssets(document, markdown);
        foreach (var path in assets)
        {
            File.Delete(path);
        }

        return assets;
    }

    public static string GetVirtualImageUrl(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException("The image path is invalid.");
        }

        return "https://assets.local/" + Uri.EscapeDataString(fileName);
    }

    public static void RollbackMigration(AssetMigration migration)
    {
        foreach (var path in migration.CopiedFiles)
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
    }

    private static ImportedImage CreateImportedImage(
        MarkdownDocument document,
        string fileName,
        string targetPath)
    {
        var relativePath = document.FilePath is null
            ? fileName
            : $"{Path.GetFileName(GetDocumentAssetDirectory(document.FilePath))}/{fileName}";
        return new ImportedImage(relativePath.Replace('\\', '/'), GetVirtualImageUrl(fileName), targetPath);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var target = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await source.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
            target.Flush(flushToDisk: true);
        }
        catch
        {
            TryDeleteFile(targetPath);
            throw;
        }
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

    private static HashSet<string> ExtractManagedImageFileNames(MarkdownDocument document, string markdown)
    {
        var assetDirectoryName = document.FilePath is null
            ? null
            : Path.GetFileName(GetDocumentAssetDirectory(document.FilePath));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MarkdownImageReference().Matches(markdown))
        {
            var rawPath = Uri.UnescapeDataString(match.Groups["path"].Value.Trim());
            var normalized = rawPath.Replace('\\', '/');
            if (Uri.TryCreate(normalized, UriKind.Absolute, out _))
            {
                continue;
            }

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (document.FilePath is null && segments.Length == 1
                || assetDirectoryName is not null && segments.Length == 2
                    && string.Equals(segments[0], assetDirectoryName, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(segments[^1]);
            }
        }

        return names;
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

    private static string SanitizeBaseName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var sanitized = InvalidFileNameCharacters().Replace(baseName, "-").Trim(' ', '.', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "image" : sanitized;
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

    private static string ReplaceImageReference(string markdown, string oldReference, string newReference)
    {
        return markdown.Replace($"]({oldReference})", $"]({newReference})", StringComparison.Ordinal)
            .Replace($"](<{oldReference}>)", $"](<{newReference}>)", StringComparison.Ordinal)
            .Replace(oldReference.Replace(" ", "%20", StringComparison.Ordinal),
                newReference.Replace(" ", "%20", StringComparison.Ordinal),
                StringComparison.Ordinal);
    }

    private static bool PathEquals(string first, string second)
    {
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("""[<>:"/\\|?*\x00-\x1F]+""")]
    private static partial Regex InvalidFileNameCharacters();

    [GeneratedRegex("""!\[[^\]]*\]\(\s*(?:<(?<path>[^>]+)>|(?<path>[^\s)]+))""")]
    private static partial Regex MarkdownImageReference();
}

public sealed record ImportedImage(string RelativePath, string VirtualUrl, string PhysicalPath);

public sealed record AssetMigration(
    string Markdown,
    string TargetDirectory,
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyDictionary<string, string> PathMappings);
