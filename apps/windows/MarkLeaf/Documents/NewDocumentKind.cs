namespace MarkLeaf.Documents;

public enum NewDocumentKind
{
    Markdown,
    PlainText,
}

public static class NewDocumentKindExtensions
{
    public static string FileExtension(this NewDocumentKind kind)
        => kind == NewDocumentKind.PlainText ? "txt" : "md";

    public static string EditorDocumentType(this NewDocumentKind kind)
        => kind == NewDocumentKind.PlainText ? "plainText" : "markdown";

    public static string DefaultFileName(this NewDocumentKind kind)
        => kind == NewDocumentKind.PlainText ? "未命名.txt" : "未命名.md";

    public static NewDocumentKind FromExtension(string? extension)
        => string.Equals(extension?.TrimStart('.'), "txt", StringComparison.OrdinalIgnoreCase)
            ? NewDocumentKind.PlainText
            : NewDocumentKind.Markdown;
}
