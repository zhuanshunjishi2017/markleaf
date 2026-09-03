namespace MarkLeaf.Commands;

/// <summary>
/// 可自定义快捷键的命令目录：仅包含原生菜单里带默认快捷键的命令
/// （与「快捷键」窗口一致）。默认值沿用原 CommandCatalog.Shortcuts，
/// 并补齐 ToggleUnderline / PromoteHeading / DemoteHeading（菜单此前已显示快捷键但未注册）。
/// </summary>
public static class ShortcutCatalog
{
    public readonly record struct Entry(AppCommand Command, string DescriptionKey, Keys DefaultShortcut);

    public static readonly Entry[] Entries =
    [
        new(AppCommand.NewDocument, "shortcut.new", Keys.Control | Keys.N),
        new(AppCommand.OpenDocument, "shortcut.open", Keys.Control | Keys.O),
        new(AppCommand.SaveDocument, "shortcut.save", Keys.Control | Keys.S),
        new(AppCommand.SaveDocumentAs, "shortcut.saveAs", Keys.Control | Keys.Shift | Keys.S),
        new(AppCommand.Print, "shortcut.print", Keys.Control | Keys.P),
        new(AppCommand.Undo, "shortcut.undo", Keys.Control | Keys.Z),
        new(AppCommand.Redo, "shortcut.redo", Keys.Control | Keys.Y),
        new(AppCommand.Cut, "shortcut.cut", Keys.Control | Keys.X),
        new(AppCommand.Copy, "shortcut.copy", Keys.Control | Keys.C),
        new(AppCommand.Paste, "shortcut.paste", Keys.Control | Keys.V),
        new(AppCommand.PastePlainText, "shortcut.pastePlainText", Keys.Control | Keys.Shift | Keys.V),
        new(AppCommand.Find, "shortcut.find", Keys.Control | Keys.F),
        new(AppCommand.Replace, "shortcut.replace", Keys.Control | Keys.H),
        new(AppCommand.SelectAll, "shortcut.selectAll", Keys.Control | Keys.A),
        new(AppCommand.SetHeading1, "shortcut.heading1", Keys.Control | Keys.D1),
        new(AppCommand.SetHeading2, "shortcut.heading2", Keys.Control | Keys.D2),
        new(AppCommand.SetHeading3, "shortcut.heading3", Keys.Control | Keys.D3),
        new(AppCommand.SetHeading4, "shortcut.heading4", Keys.Control | Keys.D4),
        new(AppCommand.SetHeading5, "shortcut.heading5", Keys.Control | Keys.D5),
        new(AppCommand.SetHeading6, "shortcut.heading6", Keys.Control | Keys.D6),
        new(AppCommand.ToggleBold, "shortcut.toggleBold", Keys.Control | Keys.B),
        new(AppCommand.ToggleItalic, "shortcut.toggleItalic", Keys.Control | Keys.I),
        new(AppCommand.ToggleUnderline, "shortcut.toggleUnderline", Keys.Control | Keys.U),
        new(AppCommand.FormatPainter, "shortcut.formatPainter", Keys.Control | Keys.Shift | Keys.C),
        new(AppCommand.SetParagraph, "shortcut.setParagraph", Keys.Control | Keys.D0),
        new(AppCommand.ToggleQuote, "shortcut.quote", Keys.Control | Keys.Shift | Keys.Q),
        new(AppCommand.InsertMathBlock, "shortcut.insertMathBlock", Keys.Control | Keys.Shift | Keys.M),
        new(AppCommand.ToggleCodeBlock, "shortcut.codeBlock", Keys.Control | Keys.Shift | Keys.K),
        new(AppCommand.NewWindow, "shortcut.newWindow", Keys.Control | Keys.Shift | Keys.N),
        new(AppCommand.OpenFolder, "shortcut.openFolder", Keys.Control | Keys.Shift | Keys.O),
        new(AppCommand.ToggleHighlight, "shortcut.highlight", Keys.Control | Keys.Shift | Keys.H),
        new(AppCommand.ToggleInlineCode, "shortcut.inlineCode", Keys.Control | Keys.Oemtilde),
        new(AppCommand.InsertLink, "shortcut.insertLink", Keys.Control | Keys.L),
        new(AppCommand.ToggleStrike, "shortcut.toggleStrike", Keys.Control | Keys.D),
        new(AppCommand.ClearFormat, "shortcut.clearFormat", Keys.Control | Keys.Oem5),
        new(AppCommand.InsertMathInline, "shortcut.insertMathInline", Keys.Control | Keys.M),
        new(AppCommand.ToggleEditorFocusMode, "shortcut.editorFocusMode", Keys.F8),
        new(AppCommand.ToggleEditorTypewriterMode, "shortcut.editorTypewriterMode", Keys.F9),
        new(AppCommand.ToggleSourceMode, "shortcut.sourceMode", Keys.Control | Keys.Oem2),
        new(AppCommand.InsertHorizontalRule, "shortcut.horizontalRule", Keys.Control | Keys.Shift | Keys.L),
        new(AppCommand.ToggleSidebar, "shortcut.toggleSidebar", Keys.Control | Keys.Alt | Keys.Z),
        new(AppCommand.ShowStatusBar, "shortcut.showStatusBar", Keys.Control | Keys.Alt | Keys.X),
        new(AppCommand.ZoomIn, "shortcut.zoomIn", Keys.Control | Keys.Alt | Keys.Oemplus),
        new(AppCommand.ZoomOut, "shortcut.zoomOut", Keys.Control | Keys.Alt | Keys.OemMinus),
        new(AppCommand.ZoomReset, "shortcut.zoomReset", Keys.Control | Keys.Alt | Keys.R),
        new(AppCommand.CloseFolder, "shortcut.closeFolder", Keys.Control | Keys.Q),
        new(AppCommand.ToggleBulletList, "shortcut.bulletList", Keys.Control | Keys.Shift | Keys.Oem6),
        new(AppCommand.ToggleOrderedList, "shortcut.orderedList", Keys.Control | Keys.Shift | Keys.Oem4),
        new(AppCommand.ToggleTaskList, "shortcut.taskList", Keys.Control | Keys.Shift | Keys.T),
        new(AppCommand.IncreaseListIndent, "shortcut.increaseIndent", Keys.Control | Keys.Oem6),
        new(AppCommand.DecreaseListIndent, "shortcut.decreaseIndent", Keys.Control | Keys.Oem4),
        new(AppCommand.InsertTable, "shortcut.insertTable", Keys.Control | Keys.T),
        new(AppCommand.InsertFootnote, "shortcut.insertFootnote", Keys.Control | Keys.Shift | Keys.F),
        new(AppCommand.PromoteHeading, "shortcut.promoteHeading", Keys.Control | Keys.OemPeriod),
        new(AppCommand.DemoteHeading, "shortcut.demoteHeading", Keys.Control | Keys.Oemcomma),
        new(AppCommand.ToggleFocusMode, "shortcut.toggleFocusMode", Keys.Shift | Keys.F11),
        new(AppCommand.ToggleEditorFullScreen, "shortcut.toggleEditorFullScreen", Keys.F11),
        new(AppCommand.SwitchDocumentTab1, "shortcut.switchDocumentTab1", Keys.Alt | Keys.D1),
        new(AppCommand.SwitchDocumentTab2, "shortcut.switchDocumentTab2", Keys.Alt | Keys.D2),
        new(AppCommand.SwitchDocumentTab3, "shortcut.switchDocumentTab3", Keys.Alt | Keys.D3),
        new(AppCommand.SwitchDocumentTab4, "shortcut.switchDocumentTab4", Keys.Alt | Keys.D4),
        new(AppCommand.SwitchDocumentTab5, "shortcut.switchDocumentTab5", Keys.Alt | Keys.D5),
        new(AppCommand.SwitchDocumentTab6, "shortcut.switchDocumentTab6", Keys.Alt | Keys.D6),
        new(AppCommand.SwitchDocumentTab7, "shortcut.switchDocumentTab7", Keys.Alt | Keys.D7),
        new(AppCommand.SwitchDocumentTab8, "shortcut.switchDocumentTab8", Keys.Alt | Keys.D8),
        new(AppCommand.SwitchDocumentTab9, "shortcut.switchDocumentTab9", Keys.Alt | Keys.D9),
        new(AppCommand.SwitchToNextDocumentTab, "shortcut.switchToNextDocumentTab", Keys.Control | Keys.Tab),
        new(AppCommand.CloseCurrentDocumentTab, "shortcut.closeCurrentDocumentTab", Keys.Control | Keys.W),
        new(AppCommand.CloseOtherDocumentTabs, "shortcut.closeOtherDocumentTabs", Keys.Control | Keys.Shift | Keys.W),
    ];

    public static Entry? Find(AppCommand command)
    {
        foreach (var entry in Entries)
        {
            if (entry.Command == command)
            {
                return entry;
            }
        }

        return null;
    }
}
