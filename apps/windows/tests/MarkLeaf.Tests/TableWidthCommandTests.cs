using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class TableWidthCommandTests
{
    [TestMethod]
    [DataRow("FitTableContents", "fitTableContents")]
    [DataRow("DistributeTableColumns", "distributeTableColumns")]
    [DataRow("ResetTableWidths", "resetTableWidths")]
    public void TableWidthActionsUseSharedAvailabilityAndArePresentInContextMenu(string name, string identifier)
    {
        Assert.IsTrue(Enum.TryParse<AppCommand>(name, out var command));
        Assert.IsTrue(EditorCommandBindings.TryGetAction(command, out var action));
        Assert.AreEqual(identifier, action);
        var context = new CommandContext(DocumentAvailable: true, EditorReady: true, SelectionScope: "caret",
            EditorActions: new Dictionary<string, CommandState> { [identifier] = new(true) });
        Assert.IsTrue(CommandStateResolver.Resolve(command, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(command, context with { SelectionScope = "mixed" }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(command, context with { EditorActions = new Dictionary<string, CommandState> { [identifier] = new(false) } }).IsEnabled);
        var model = new EditorMenuModelBuilder(_ => new CommandState(true), _ => "")
            .Build(EditorCommandStatus.Empty with { InTable = true });
        Assert.IsTrue(Flatten(model.Secondary).Any(item => item.Command == command));
        var readOnly = new EditorMenuModelBuilder(_ => new CommandState(false), _ => "")
            .Build(EditorCommandStatus.Empty with { InTable = true, ReadOnly = true });
        Assert.IsFalse(Flatten(readOnly.Secondary).Any(item => item.Command == command));
    }
    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children)) yield return child;
        }
    }
}
