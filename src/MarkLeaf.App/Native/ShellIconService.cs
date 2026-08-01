using System.Runtime.InteropServices;

namespace MarkLeaf.Native;

internal sealed class ShellIconService : IDisposable
{
    private readonly Dictionary<string, int> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageList CreateImageList(int dpi)
    {
        Dispose();
        var size = GetIconSize(dpi);
        Images = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(size, size),
        };
        return Images;
    }

    public ImageList? Images { get; private set; }

    public int GetImageIndex(string path, bool isDirectory)
    {
        if (Images is null)
        {
            return -1;
        }

        var key = isDirectory ? "<folder>" : Path.GetExtension(path).ToLowerInvariant();
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Normal;
        var flags = NativeMethods.ShgfiIcon | NativeMethods.ShgfiUseFileAttributes
            | (Images.ImageSize.Width <= 16 ? NativeMethods.ShgfiSmallIcon : NativeMethods.ShgfiLargeIcon);
        var lookupPath = isDirectory ? path : string.IsNullOrEmpty(key) ? path : "placeholder" + key;
        var result = NativeMethods.SHGetFileInfo(
            lookupPath,
            (uint)attributes,
            out var info,
            (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(),
            flags);
        if (result == 0 || info.Icon == 0)
        {
            return -1;
        }

        try
        {
            using var icon = Icon.FromHandle(info.Icon);
            using var bitmap = icon.ToBitmap();
            Images.Images.Add((Bitmap)bitmap.Clone());
            var index = Images.Images.Count - 1;
            _cache[key] = index;
            return index;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.Icon);
        }
    }

    public void Dispose()
    {
        Images?.Dispose();
        Images = null;
        _cache.Clear();
    }

    internal static int GetIconSize(int dpi)
    {
        return dpi switch
        {
            >= 192 => 32,
            >= 144 => 24,
            >= 120 => 20,
            _ => 16,
        };
    }
}
