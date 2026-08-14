using System.Diagnostics;
using MarkLeaf.Services;

namespace MarkLeaf.UI;

internal sealed partial class MainForm
{
    private void StartNewWindow(string? documentPath = null)
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
            Process.Start(startInfo);
            SetStatus(documentPath is null ? Loc.Get("status.newWindowOpened") : Loc.Get("status.documentOpenedInNewWindow"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not start a new MarkLeaf window.", exception);
            ShowMessage(this, Loc.Get("dialog.cannotOpenNewWindow") + "\r\n\r\n" + exception.Message, "MarkLeaf",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
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
