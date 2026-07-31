namespace MarkLeaf.Documents;

public sealed class ExternalDocumentChangedException : IOException
{
    public ExternalDocumentChangedException(string path)
        : base($"The file changed outside MarkLeaf: {path}")
    {
        FilePath = path;
    }

    public string FilePath { get; }
}

public sealed class DocumentSaveException : IOException
{
    public DocumentSaveException(string message, string? recoveryFilePath, Exception innerException)
        : base(message, innerException)
    {
        RecoveryFilePath = recoveryFilePath;
    }

    public string? RecoveryFilePath { get; }
}
