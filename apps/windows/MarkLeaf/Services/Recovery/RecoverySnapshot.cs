namespace MarkLeaf.Services.Recovery;

internal sealed record RecoverySnapshot(
    Guid DocumentId,
    string? DocumentPath,
    string Markdown,
    long Revision,
    DateTimeOffset Timestamp,
    string? DisplayName)
{
    public static RecoverySnapshot FromDocument(
        Documents.MarkdownDocument document,
        string markdown)
    {
        return new RecoverySnapshot(
            document.Id,
            document.FilePath,
            markdown,
            document.Revision,
            DateTimeOffset.UtcNow,
            document.DisplayName);
    }
}
