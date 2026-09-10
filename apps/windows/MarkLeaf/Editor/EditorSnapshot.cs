namespace MarkLeaf.Editor;

public sealed record EditorSnapshot(string Markdown, long Revision, double ScrollTop = 0);
