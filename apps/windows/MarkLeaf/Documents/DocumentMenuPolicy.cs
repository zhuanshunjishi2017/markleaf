namespace MarkLeaf.Documents;

/// <summary>Shared eligibility for menu state and document operations.</summary>
public static class DocumentMenuPolicy
{
    public static bool CanSave(bool available, bool ready, bool dirty, bool hasPath, bool readOnly) =>
        available && ready && !readOnly && (dirty || !hasPath);

    public static bool CanSaveAs(bool available, bool ready) => available && ready;

    public static MarkdownDocument? FindOpenDocument(IEnumerable<MarkdownDocument> documents, string path) =>
        documents.FirstOrDefault(document => DocumentPathIdentity.Equals(document.FilePath, path));

    public static IReadOnlyList<MarkdownDocument> SaveAllTargets(IEnumerable<MarkdownDocument> documents) =>
        documents.Where(document => document.IsDirty && !document.IsReadOnly && !document.IsUserReadOnly).ToArray();
}
