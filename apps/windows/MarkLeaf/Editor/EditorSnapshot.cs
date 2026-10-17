namespace MarkLeaf.Editor;

public sealed record EditorSnapshot(
    string Markdown,
    long Revision,
    double ScrollTop = 0,
    ReadingAnchor? ReadingAnchor = null,
    string? SavedMarkdown = null,
    bool SourceMode = false);
