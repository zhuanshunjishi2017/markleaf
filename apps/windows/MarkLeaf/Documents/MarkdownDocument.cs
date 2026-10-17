using System.Text;
using MarkLeaf.Services;

namespace MarkLeaf.Documents;
public sealed class MarkdownDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? FilePath { get; set; }

    public NewDocumentKind Kind { get; set; } = NewDocumentKind.Markdown;

    public string Markdown { get; set; } = string.Empty;
    // Null means no trustworthy saved baseline is available (e.g. recovered content).
    public string? SavedMarkdown { get; set; }
    public bool SourceMode { get; set; }

    public void ApplySnapshot(MarkLeaf.Editor.EditorSnapshot snapshot)
    {
        Markdown = snapshot.Markdown;
        Revision = Math.Max(Revision, snapshot.Revision);
        ScrollTop = snapshot.ScrollTop;
        ReadingAnchor = snapshot.ReadingAnchor;
        SavedMarkdown = snapshot.SavedMarkdown;
        SourceMode = snapshot.SourceMode;
    }

    public Encoding Encoding { get; set; } = new UTF8Encoding(false, true);

    public string EncodingPolicyId { get; set; } = DocumentEncodingPolicy.Utf8.Id;

    public bool HasBom { get; set; }

    public string NewLine { get; set; } = Environment.NewLine;

    public bool IsDirty { get; set; }

    // Non-text changes (deleted backing file, newline/encoding) remain dirty
    // even when the editor reports that text equals its saved baseline.
    public bool HasUnsavedMetadataChanges { get; set; }

    public bool IsReadOnly { get; set; }

    /// <summary>
    /// 用户通过视图菜单手动开启的只读模式。与 <see cref="IsReadOnly"/>（文件
    /// 权限或"以只读方式打开"决定的持久只读）分开记录，标签页切换时跟随文档。
    /// </summary>
    public bool IsUserReadOnly { get; set; }

    /// <summary>
    /// 与 packages/editor-core/src/reading-anchor.ts 对齐的稳定阅读锚点，
    /// 在标签页切换/关闭时随快照捕获，重新激活时经 loadDocument 恢复。
    /// </summary>
    public MarkLeaf.Editor.ReadingAnchor? ReadingAnchor { get; set; }

    // Visual and source editors use different position coordinate systems.
    public int? VisualSelectionFrom { get; set; }

    public int? VisualSelectionTo { get; set; }

    public int? SourceSelectionFrom { get; set; }

    public int? SourceSelectionTo { get; set; }

    // Scroll offset of the document's active editor view, restored when the tab is activated again.
    public double ScrollTop { get; set; }

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
