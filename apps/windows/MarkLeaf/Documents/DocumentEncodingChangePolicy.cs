namespace MarkLeaf.Documents;

public enum EncodingChangeAction { None, RejectReadOnly, UpdateNextSave, Prompt }

public static class DocumentEncodingChangePolicy
{
    public static EncodingChangeAction Action(MarkdownDocument document, DocumentEncodingPolicy target)
    {
        if (document.IsReadOnly || document.IsUserReadOnly) return EncodingChangeAction.RejectReadOnly;
        if (document.EncodingPolicyId == target.Id) return EncodingChangeAction.None;
        return document.FilePath is null ? EncodingChangeAction.UpdateNextSave : EncodingChangeAction.Prompt;
    }

    public static bool SetNewLine(MarkdownDocument document, string newLine)
    {
        if (document.IsReadOnly || document.IsUserReadOnly || newLine is not ("\n" or "\r\n")
            || document.NewLine == newLine) return false;
        document.NewLine = newLine;
        document.HasUnsavedMetadataChanges = document.IsDirty = true;
        return true;
    }

    public static void SetNextSaveEncoding(MarkdownDocument document, DocumentEncodingPolicy target)
    {
        if (Action(document, target) != EncodingChangeAction.UpdateNextSave) return;
        document.Encoding = target.CreateEncoding();
        document.EncodingPolicyId = target.Id;
        document.HasBom = target.HasBom;
        document.HasUnsavedMetadataChanges = document.IsDirty = true;
    }
}
