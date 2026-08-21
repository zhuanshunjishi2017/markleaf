using System.Text;
using MarkLeaf.Services;

namespace MarkLeaf.Documents;

public sealed class MarkdownDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? FilePath { get; set; }

    public NewDocumentKind Kind { get; set; } = NewDocumentKind.Markdown;

    public string Markdown { get; set; } = string.Empty;

    public Encoding Encoding { get; set; } = new UTF8Encoding(false, true);

    public bool HasBom { get; set; }

    public string NewLine { get; set; } = Environment.NewLine;

    public bool IsDirty { get; set; }

    public bool IsReadOnly { get; set; }

    public long Revision { get; set; }

    public DateTimeOffset? LastKnownWriteTime { get; set; }

    public FileFingerprint? LastKnownFingerprint { get; set; }

    public string DisplayName => FilePath is null ? Loc.Get("common.unnamed") : Path.GetFileName(FilePath);
}

public sealed record FileFingerprint(long Length, DateTimeOffset LastWriteTime, string Sha256)
{
    public bool HasSameContent(FileFingerprint other)
    {
        return Length == other.Length
            && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
