using System.Reflection;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ReadOnlyOpenConfirmationContractTests
{
    [TestMethod]
    public void ExistingReadOnlyOpenUsesExplicitConvertAndCancelActions()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var expectedSource = Path.Combine("apps", "windows", "MarkLeaf", "UI", "MainForm.Documents.cs");
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, expectedSource)))
        {
            directory = directory.Parent;
        }

        Assert.IsNotNull(directory, "repository root not found");
        var uiDirectory = Path.Combine(directory.FullName, "apps", "windows", "MarkLeaf", "UI");
        var hostSource = File.ReadAllText(Path.Combine(uiDirectory, "MainForm.Documents.cs"));
        var dialogSource = File.ReadAllText(Path.Combine(uiDirectory, "Dialogs", "ConvertToReadOnlyDialog.cs"));

        Assert.Contains("ConvertExistingDocumentToReadOnly", hostSource, StringComparison.Ordinal);
        Assert.Contains("Dialogs.ConvertToReadOnlyDialog", hostSource, StringComparison.Ordinal);
        Assert.Contains("Text = MarkLeaf.Services.Loc.Get(\"document.convertToReadOnlyConfirm\")", dialogSource, StringComparison.Ordinal);
        Assert.Contains("DialogResult = DialogResult.OK", dialogSource, StringComparison.Ordinal);
        Assert.Contains("DialogResult = DialogResult.Cancel", dialogSource, StringComparison.Ordinal);
    }
}
