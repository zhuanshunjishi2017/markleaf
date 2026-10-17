using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native;
using MarkLeaf.Native.ModernMenus;
using MarkLeaf.Services.Settings;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class ObjectMenuRoutingTests
{
    [TestMethod]
    public void AlertHasAnExplicitUnwrapAction()
    {
        using var service = Service();
        var model = service.BuildEditorMenuModel(EditorCommandStatus.Empty with {
            Actions = new Dictionary<string, CommandState> { ["cancelAlert"] = new(true) } });
        Assert.IsTrue(Commands(model).Any(command => EditorCommandBindings.TryGetCommand(command, out var name) && name == "cancelAlert"));
    }
    [TestMethod]
    [DataRow(AppCommand.ViewMathSource, "viewMathSource")]
    [DataRow(AppCommand.ViewMermaidSource, "viewMermaidSource")]
    [DataRow(AppCommand.SaveMermaidAs, "saveMermaidAs")]
    [DataRow(AppCommand.OpenLink, "openLink")]
    [DataRow(AppCommand.CopyLinkAddress, "copyLinkAddress")]
    [DataRow(AppCommand.EditLink, "editLink")]
    [DataRow(AppCommand.RemoveLink, "removeLink")]
    [DataRow(AppCommand.GoToFootnoteDefinition, "goToFootnoteDefinition")]
    [DataRow(AppCommand.DeleteFootnoteReference, "deleteFootnoteReference")]
    public void ApprovedActionHasAnEffectiveBinding(AppCommand command, string expected)
    {
        Assert.IsTrue(EditorCommandBindings.TryGetAction(command, out var actual));
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RenderedMermaidHasSaveAndCorrectSourceAction(bool readOnly)
    {
        using var service = Service();
        var commands = Commands(service.BuildEditorMenuModel(EditorCommandStatus.Empty with {
            MermaidSelected = true, MermaidRendered = true, ReadOnly = readOnly }));
        Assert.Contains(AppCommand.SaveMermaidAs, commands);
        Assert.Contains(readOnly ? AppCommand.ViewMermaidSource : AppCommand.EditMermaid, commands);
        if (readOnly) Assert.DoesNotContain(AppCommand.EditMermaid, commands);
    }

    [TestMethod]
    public void ReadOnlyMathCanViewItsSource()
    {
        using var service = Service();
        Assert.Contains(AppCommand.ViewMathSource, Commands(service.BuildEditorMenuModel(
            EditorCommandStatus.Empty with { MathBlock = true, ReadOnly = true })));
    }

    [TestMethod]
    public void LinkActionsAppendWithoutReplacingTheTableMenu()
    {
        using var service = Service();
        var commands = Commands(service.BuildEditorMenuModel(EditorCommandStatus.Empty with {
            InTable = true, LinkHref = "https://example.com" }));
        foreach (var action in new[] { AppCommand.AddTableRowBefore, AppCommand.OpenLink,
            AppCommand.CopyLinkAddress, AppCommand.EditLink, AppCommand.RemoveLink }) Assert.Contains(action, commands);
    }

    [TestMethod]
    public void ReadOnlyFootnoteReferenceRetainsNavigationButNotDeletion()
    {
        using var service = Service();
        var commands = Commands(service.BuildEditorMenuModel(EditorCommandStatus.Empty with {
            FootnoteReferenceLabel = "n", ReadOnly = true }));
        Assert.Contains(AppCommand.GoToFootnoteDefinition, commands);
        Assert.DoesNotContain(AppCommand.DeleteFootnoteReference, commands);
    }

    private static AppCommand[] Commands(EditorMenuModel model) => Flatten(model.Secondary)
        .Where(entry => entry.Command.HasValue).Select(entry => entry.Command!.Value).ToArray();
    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> items) =>
        items.SelectMany(entry => new[] { entry }.Concat(Flatten(entry.Children)));
    private static NativeMenuService Service()
    {
        LocTestHelper.EnsureInitialized();
        var shortcuts = new ShortcutManager(new ShortcutSettings());
        return new(new CommandRouter(shortcuts, _ => new(true), _ => { }), shortcuts,
            () => [], () => [], () => "serif", () => 100, () => "light", () => false,
            () => true, () => false, () => "zh-CN",
            () => Array.Empty<NativeMenuService.DocumentTabMenuEntry>(), () => -1);
    }
}
