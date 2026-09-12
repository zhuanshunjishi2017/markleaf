using MarkLeaf.Commands;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class CommandStateResolverTests
{
    [TestMethod]
    public void Resolve_HostCommandsUseWindowAndDocumentFacts()
    {
        var empty = new CommandContext(DocumentAvailable: false, EditorReady: false);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.NewDocument, empty).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.Exit, empty).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SaveDocument, empty).IsEnabled);
        var ready = empty with { DocumentAvailable = true, EditorReady = true, SidebarVisible = true };
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.SaveDocument, ready).IsEnabled);
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleSidebar, ready));
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleSidebar, ready with { FocusMode = true }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.SwitchToOutline, ready with { IndependentOutlineSidebar = true }).IsEnabled);
    }

    [TestMethod]
    public void Resolve_EditorCommandsConsumeProjectedEnabledAndCheckedState()
    {
        var context = new CommandContext(DocumentAvailable: true, EditorReady: true,
            EditorActions: new Dictionary<string, CommandState>
            {
                ["toggleBold"] = new(true, true),
                ["setHeading2"] = new(false, true),
                ["setLink"] = new(true),
                ["addRowAfter"] = new(true),
            });
        Assert.AreEqual(new CommandState(true, true), CommandStateResolver.Resolve(AppCommand.ToggleBold, context));
        Assert.AreEqual(new CommandState(false, true), CommandStateResolver.Resolve(AppCommand.SetHeading2, context));
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.InsertLink, context).IsEnabled);
        Assert.IsTrue(CommandStateResolver.Resolve(AppCommand.AddTableRowAfter, context).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.DeleteTable, context).IsEnabled);
    }

    [TestMethod]
    public void Resolve_MissingProjectionOrUnloadedDocumentCannotEnableEditorCommands()
    {
        var context = new CommandContext(DocumentAvailable: true, EditorReady: true);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context).IsEnabled);
        context = context with { EditorActions = new Dictionary<string, CommandState> { ["toggleBold"] = new(true) } };
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context with { DocumentAvailable = false }).IsEnabled);
        Assert.IsFalse(CommandStateResolver.Resolve(AppCommand.ToggleBold, context with { EditorReady = false }).IsEnabled);
    }
}
