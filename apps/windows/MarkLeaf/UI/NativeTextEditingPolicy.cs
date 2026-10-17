using System.Windows.Forms;

namespace MarkLeaf.UI;

internal enum NativeTextCommand
{
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    PastePlainText,
    SelectAll,
}

/// <summary>
/// Native TextBoxBase controls must keep plain editing commands before app
/// shortcuts so a sidebar search field or rename editor never edits the
/// background Markdown document.
/// </summary>
internal static class NativeTextEditingPolicy
{
    public static NativeTextCommand? MapShortcut(Keys keyData)
    {
        var modifiers = keyData & Keys.Modifiers;
        var keyCode = keyData & Keys.KeyCode;

        if ((modifiers & Keys.Control) == 0)
        {
            return null;
        }

        if ((modifiers & Keys.Shift) != 0)
        {
            return keyCode == Keys.V ? NativeTextCommand.PastePlainText : null;
        }

        return keyCode switch
        {
            Keys.Z => NativeTextCommand.Undo,
            Keys.Y => NativeTextCommand.Redo,
            Keys.X => NativeTextCommand.Cut,
            Keys.C => NativeTextCommand.Copy,
            Keys.V => NativeTextCommand.Paste,
            Keys.A => NativeTextCommand.SelectAll,
            _ => null,
        };
    }

    public static TextBoxBase? FindFocusedText(Control root, Control? focused = null)
    {
        var target = focused ?? root;
        if (focused is TextBoxBase explicitText)
        {
            return explicitText;
        }

        if (target is TextBoxBase text && text.Focused)
        {
            return text;
        }

        foreach (Control child in target.Controls)
        {
            var descendant = FindFocusedText(child, focused: null);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    public static bool Execute(TextBoxBase editor, NativeTextCommand command)
    {
        switch (command)
        {
            case NativeTextCommand.Undo:
                if (!editor.ReadOnly && editor.CanUndo) editor.Undo();
                return true;
            case NativeTextCommand.Redo:
                if (editor is RichTextBox richEditor && !richEditor.ReadOnly && richEditor.CanRedo)
                {
                    richEditor.Redo();
                }
                return true;
            case NativeTextCommand.Cut:
                if (!editor.ReadOnly && editor.SelectionLength > 0) editor.Cut();
                return true;
            case NativeTextCommand.Copy:
                if (editor.SelectionLength > 0) editor.Copy();
                return true;
            case NativeTextCommand.Paste:
                if (!editor.ReadOnly && Clipboard.ContainsText()) editor.Paste();
                return true;
            case NativeTextCommand.PastePlainText:
                if (!editor.ReadOnly && Clipboard.ContainsText())
                {
                    var plainText = Clipboard.GetText(TextDataFormat.Text);
                    if (string.IsNullOrEmpty(plainText))
                    {
                        plainText = Clipboard.GetText(TextDataFormat.UnicodeText);
                    }

                    editor.SelectedText = plainText;
                }
                return true;
            case NativeTextCommand.SelectAll:
                editor.SelectAll();
                return true;
            default:
                return false;
        }
    }
}
