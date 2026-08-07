using System.Net.Http;
using MarkLeaf.Documents;
using MarkLeaf.UI.Dialogs;

namespace MarkLeaf.UI;

partial class MainForm
{
    private async Task InsertImageFromUrlAsync()
    {
        if (_document is null || _editorHost?.IsDocumentLoaded != true)
        {
            return;
        }

        using var dialog = new ImageUrlDialog();
        if (ShowModal(() => dialog.ShowDialog(this)) != DialogResult.OK)
        {
            return;
        }

        var url = dialog.ImageUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var target = GetDocumentAssetsDirectory() ?? GetDefaultImageDirectory();
            var imported = await _imageAssetService.DownloadImageAsync(url, target);
            await InsertImportedImageAsync(imported, dialog.AltText);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException
            or IOException or TaskCanceledException)
        {
            SetStatus($"Failed to download image: {exception.Message}");
        }
    }
}
