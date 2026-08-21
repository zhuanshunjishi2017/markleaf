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
        new(AppCommand.NewPlainTextDocument, "shortcut.newText", Keys.Control | Keys.Alt | Keys.N),
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
        new(AppCommand.InsertLink, "shortcut.insertLink", Keys.Control | Keys.K),
        new(AppCommand.PromoteHeading, "shortcut.promoteHeading", Keys.Control | Keys.OemPeriod),
        new(AppCommand.DemoteHeading, "shortcut.demoteHeading", Keys.Control | Keys.Oemcomma),
        new(AppCommand.ToggleFocusMode, "shortcut.toggleFocusMode", Keys.F11),
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
