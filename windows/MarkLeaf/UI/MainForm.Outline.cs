using MarkLeaf.Editor;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private int? _activeOutlinePosition;
    private int? _pendingOutlinePosition;
    private DateTime _pendingOutlineUntilUtc;
    private IReadOnlyList<EditorOutlineItem> _currentOutline = [];
    private bool _outlineSearchActive;

    private void OnEditorOutlineChanged(object? sender, EditorOutline outline)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorOutlineChanged(sender, outline));
            return;
        }

        _currentOutline = outline.Headings;
        if (!_outlineSearchActive)
        {
            _outlineTree.SetItems(outline.Headings);
        }
        _outlineTree.SelectedPosition = _activeOutlinePosition;
    }

    private void OnEditorOutlineSelectionChanged(object? sender, int? position)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnEditorOutlineSelectionChanged(sender, position));
            return;
        }

        if (_pendingOutlinePosition is { } pendingPosition)
        {
            if (position == pendingPosition)
            {
                _pendingOutlinePosition = null;
            }
            else if (DateTime.UtcNow < _pendingOutlineUntilUtc)
            {
                return;
            }
            else
            {
                _pendingOutlinePosition = null;
            }
        }

        _activeOutlinePosition = position;
        _outlineTree.SelectedPosition = position;
    }

    private void ActivateOutlinePosition(int position)
    {
        if (_editorHost?.IsDocumentLoaded == true)
        {
            _pendingOutlinePosition = position;
            _pendingOutlineUntilUtc = DateTime.UtcNow.AddMilliseconds(750);
            _activeOutlinePosition = position;
            _outlineTree.SelectedPosition = position;
            _editorHost.ExecuteCommand("scrollToPosition", position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        ExitOutlineSearch();
    }

    private void ApplyOutlineSearch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            ExitOutlineSearch();
            return;
        }

        _outlineSearchActive = true;
        var query = text.Trim();
        var filtered = _currentOutline
            .Where(item => item.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _outlineTree.SetFlatItems(filtered);
        _outlineTree.SelectedPosition = null;
    }

    private void ExitOutlineSearch()
    {
        if (!_outlineSearchActive)
        {
            return;
        }

        _outlineSearchActive = false;
        _outlineTree.SetItems(_currentOutline);
        _outlineTree.SelectedPosition = _activeOutlinePosition;
    }
}
