using MarkLeaf.Commands;
using MarkLeaf.Editor;
using MarkLeaf.Native.ModernMenus;

namespace MarkLeaf.Tests;

[TestClass]
public sealed class EditorMenuModelTests
{
    private static EditorMenuModel Build(EditorCommandStatus? status = null) =>
        new EditorMenuModelBuilder(_ => new CommandState(true, true), _ => "Ctrl+K").Build(status ?? EditorCommandStatus.Empty);

    [TestMethod]
    public void NormalContextHasSevenUniquePrimaryCommandsAndNoSecondaryDuplicates()
    {
        var model = Build();
        CollectionAssert.AreEqual(new[] { AppCommand.ToggleBold, AppCommand.ToggleItalic, AppCommand.ToggleUnderline,
            AppCommand.ToggleStrike, AppCommand.ToggleHighlight, AppCommand.FormatPainter, AppCommand.ClearFormat },
            model.Primary.Select(x => x.Command!.Value).ToArray());
        var secondary = Flatten(model.Secondary).Where(x => x.Command.HasValue).Select(x => x.Command!.Value).ToArray();
        Assert.IsFalse(secondary.Intersect(model.Primary.Select(x => x.Command!.Value)).Any());
        Assert.Contains(AppCommand.FormatPainterApply, secondary);
        Assert.Contains(AppCommand.SetHeading6, secondary);
        Assert.IsTrue(model.Primary.All(x => x.State.IsChecked && x.Shortcut == "Ctrl+K"));
    }

    [TestMethod]
    public void SpecialContextsPreserveTheirCommands()
    {
        AssertContains(EditorCommandStatus.Empty with { InTable = true }, AppCommand.AddTableRowBefore, AppCommand.AlignTableRight);
        AssertContains(EditorCommandStatus.Empty with { MathBlock = true }, AppCommand.EditMath, AppCommand.SetMathNumber);
        AssertContains(EditorCommandStatus.Empty with { MathInline = true }, AppCommand.ConvertMath);
        AssertContains(EditorCommandStatus.Empty with { MermaidSelected = true }, AppCommand.EditMermaid);
        AssertContains(EditorCommandStatus.Empty with { ImageSelected = true }, AppCommand.ResizeImage90, AppCommand.SaveImageAs);
        AssertContains(EditorCommandStatus.Empty with { FootnoteDefinitionLabel = "note" }, AppCommand.DeleteFootnote);
        AssertContains(EditorCommandStatus.Empty with { FrontMatter = true }, AppCommand.ExitCode);
        var code = EditorCommandStatus.Empty with { CodeBlock = true, Actions = new Dictionary<string, CommandState> { ["formatCodeBlock"] = new(true) } };
        AssertContains(code, AppCommand.DeclareCodeLanguage, AppCommand.FormatCodeBlock);
        Assert.DoesNotContain(AppCommand.FormatCodeBlock, Flatten(Build(code with { Actions = new Dictionary<string, CommandState>() }).Secondary).Select(x => x.Command).ToArray());
    }

    [TestMethod]
    public void SourceReadOnlyAndExpandedSourceNeverOfferPrimaryFormatting()
    {
        foreach (var status in new[] { EditorCommandStatus.Empty with { SourceMode = true },
            EditorCommandStatus.Empty with { ReadOnly = true }, EditorCommandStatus.Empty with { ExpandedSource = true } })
        {
            var model = Build(status);
            Assert.IsEmpty(model.Primary);
            Assert.Contains(AppCommand.SelectAll, Flatten(model.Secondary).Select(x => x.Command!.GetValueOrDefault()).ToArray());
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InlineCommonActionsReplacesQuickRowWithOneTextGroup(bool inTable)
    {
        var model = new EditorMenuModelBuilder(command => new CommandState(command != AppCommand.ToggleItalic, true),
            _ => "Ctrl+K").Build(EditorCommandStatus.Empty with { InTable = inTable }, inlineCommonActions: true);
        Assert.IsEmpty(model.Primary, "The checked preference must hide all quick buttons.");
        var commands = Flatten(model.Secondary).Where(x => x.Command.HasValue).ToArray();
        foreach (var command in new[] { AppCommand.ToggleBold, AppCommand.ToggleItalic, AppCommand.ToggleUnderline,
            AppCommand.ToggleStrike, AppCommand.ToggleHighlight, AppCommand.FormatPainter, AppCommand.FormatPainterApply, AppCommand.ClearFormat })
            Assert.HasCount(1, commands.Where(x => x.Command == command).ToArray());
        var format = model.Secondary.Single(x => x.Children.Any(child => child.Command == AppCommand.ToggleBold));
        Assert.HasCount(5, format.Children);
        var italic = format.Children.Single(x => x.Command == AppCommand.ToggleItalic);
        Assert.IsFalse(italic.State.IsEnabled);
        Assert.IsTrue(italic.State.IsChecked);
        Assert.AreEqual("Ctrl+K", italic.Shortcut);
        Assert.AreNotEqual(EditorMenuEntryKind.Separator, model.Secondary[0].Kind);
        Assert.AreNotEqual(EditorMenuEntryKind.Separator, model.Secondary[^1].Kind);
        if (inTable) Assert.Contains(AppCommand.AddTableRowBefore, commands.Select(x => x.Command!.Value).ToArray());
    }

    [TestMethod]
    public void InlineCommonActionsDoesNotAddFormattingToSourceOrReadOnlyMenus()
    {
        foreach (var status in new[] { EditorCommandStatus.Empty with { SourceMode = true },
            EditorCommandStatus.Empty with { ReadOnly = true }, EditorCommandStatus.Empty with { ExpandedSource = true } })
        {
            var model = new EditorMenuModelBuilder(_ => new CommandState(true), _ => "").Build(status, inlineCommonActions: true);
            Assert.IsEmpty(model.Primary);
            Assert.DoesNotContain((AppCommand?)AppCommand.ToggleBold, Flatten(model.Secondary).Select(x => x.Command).ToArray());
        }
    }

    private static void AssertContains(EditorCommandStatus status, params AppCommand[] commands)
    {
        var all = Flatten(Build(status).Secondary).Select(x => x.Command).ToArray();
        foreach (var command in commands) Assert.Contains((AppCommand?)command, all);
    }

    private static IEnumerable<EditorMenuEntry> Flatten(IEnumerable<EditorMenuEntry> entries) =>
        entries.SelectMany(x => new[] { x }.Concat(Flatten(x.Children)));
}
