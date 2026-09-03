using System.Diagnostics;
using System.Globalization;
using MarkLeaf.Services;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private bool StartNewWindow(
        string? documentPath = null,
        string? documentStatePath = null,
        Point? location = null,
        Size? size = null)
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException(Loc.Get("dialog.cannotFindExecutable"));
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
            };
            if (!string.IsNullOrWhiteSpace(documentPath))
            {
                startInfo.ArgumentList.Add("--open-document");
                startInfo.ArgumentList.Add(Path.GetFullPath(documentPath));
            }
            if (!string.IsNullOrWhiteSpace(documentStatePath))
            {
                startInfo.ArgumentList.Add("--open-document-state");
                startInfo.ArgumentList.Add(Path.GetFullPath(documentStatePath));
            }
            if (location is { } windowLocation)
            {
                startInfo.ArgumentList.Add("--window-left");
                startInfo.ArgumentList.Add(windowLocation.X.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("--window-top");
                startInfo.ArgumentList.Add(windowLocation.Y.ToString(CultureInfo.InvariantCulture));
            }
            if (size is { } windowSize)
            {
                startInfo.ArgumentList.Add("--window-width");
                startInfo.ArgumentList.Add(windowSize.Width.ToString(CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("--window-height");
                startInfo.ArgumentList.Add(windowSize.Height.ToString(CultureInfo.InvariantCulture));
            }
            Process.Start(startInfo);
            SetStatus(documentPath is null ? Loc.Get("status.newWindowOpened") : Loc.Get("status.documentOpenedInNewWindow"));
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not start a new MarkLeaf window.", exception);
            ShowMessage(this, Loc.Get("dialog.cannotOpenNewWindow") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static void SetSplitterDistanceSafely(
        SplitContainer split,
        int desiredPanelWidth,
        FixedPanel fixedPanel)
    {
        var minimum = split.Panel1MinSize;
        var maximum = Math.Max(minimum, split.Width - split.Panel2MinSize - split.SplitterWidth);
        var distance = fixedPanel == FixedPanel.Panel1
            ? desiredPanelWidth
            : split.Width - desiredPanelWidth - split.SplitterWidth;
        split.SplitterDistance = Math.Clamp(distance, minimum, maximum);
    }
}
