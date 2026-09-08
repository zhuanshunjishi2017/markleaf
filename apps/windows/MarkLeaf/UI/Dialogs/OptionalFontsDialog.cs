using System.IO.Compression;
using System.Net.Http.Headers;
using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class OptionalFontsDialog : Form
{
    private sealed record FontFile(string Name, string FileName);
    private sealed record FontPack(string StyleId, string Name, string Asset, FontFile[] Files);
    private static readonly FontPack[] Packs =
    [
        new("latex", "Computer Modern", "MarkLeaf-fontpack-computer-modern.zip",
        [
            new("CMU Bright Italic", "cmunbi.ttf"), new("CMU Bright Oblique", "cmunbl.ttf"),
            new("CMU Bright SemiBold Oblique", "cmunbmo.ttf"), new("CMU Bright SemiBold", "cmunbmr.ttf"),
            new("CMU Bright Oblique", "cmunbso.ttf"), new("CMU Bright", "cmunbsr.ttf"),
            new("CMU Bright Typewriter Light", "cmunbtl.ttf"), new("CMU Bright Typewriter Oblique", "cmunbto.ttf"),
            new("CMU Bright Bold Extended", "cmunbx.ttf"), new("CMU Classical Serif Italic", "cmunci.ttf"),
            new("CMU Serif Italic", "cmunit.ttf"), new("CMU Serif Bold Italic", "cmunobi.ttf"),
            new("CMU Serif Bold Extended", "cmunobx.ttf"), new("CMU Serif Roman", "cmunorm.ttf"),
            new("CMU Serif Oblique", "cmunoti.ttf"), new("CMU Serif Roman", "cmunrm.ttf"),
            new("CMU Sans Serif Italic", "cmunsi.ttf"), new("CMU Sans Serif Oblique", "cmunsl.ttf"),
            new("CMU Sans Serif Bold Oblique", "cmunso.ttf"), new("CMU Sans Serif", "cmunss.ttf"),
            new("CMU Sans Serif Demi Condensed", "cmunssdc.ttf"), new("CMU Sans Serif Bold Extended", "cmunsx.ttf"),
            new("CMU Typewriter Text Bold", "cmuntb.ttf"), new("CMU Typewriter Text Italic", "cmunti.ttf"),
            new("CMU Typewriter Text", "cmuntt.ttf"), new("CMU Typewriter Text Bold Extended", "cmuntx.ttf"),
            new("CMU Serif Upright Italic", "cmunui.ttf"), new("CMU Typewriter Text Variable Width Italic", "cmunvi.ttf"),
            new("CMU Typewriter Text Variable Width", "cmunvt.ttf"),
        ]),
        new("notebook", "霞鹜文楷", "MarkLeaf-fontpack-lxgw-wenkai.zip",
        [
            new("霞鹜文楷 Light", "LXGWWenKai-Light.ttf"), new("霞鹜文楷 Medium", "LXGWWenKai-Medium.ttf"),
            new("霞鹜文楷 Regular", "LXGWWenKai-Regular.ttf"),
        ]),
        new("retro-print", "汇文/朝华字体", "MarkLeaf-fontpack-old-typeface.zip",
        [
            new("朝华标题 A", "朝華標題A.ttf"), new("朝华打字机", "朝華打字機.ttf"),
            new("汇文仿宋 V1.002", "汇文仿宋V1.002.TTF"), new("汇文港黑 V1.001", "汇文港黑V1.001.TTF"),
            new("汇文明朝体", "汇文明朝体.OTF"), new("汇文正楷 V1.001", "汇文正楷V1.001.TTF"),
            new("京华老宋体 V3.0", "京華老宋体V3.0.TTF"),
        ]),
    ];

    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _selectedButton;
    private readonly Button _allButton;

    public OptionalFontsDialog()
    {
        Text = Loc.Get("dialog.optionalFontsTitle");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(this.ScaleForDpi(650), this.ScaleForDpi(520));

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToResizeColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add("theme", Loc.Get("dialog.optionalFontsTheme"));
        _grid.Columns.Add("font", Loc.Get("dialog.optionalFontsName"));
        _grid.Columns.Add("installed", Loc.Get("dialog.optionalFontsInstalled"));
        _grid.Columns["theme"]!.FillWeight = 33F;
        _grid.Columns["font"]!.FillWeight = 100F;
        _grid.Columns["installed"]!.FillWeight = 167F;
        ReloadRows();

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = SystemColors.GrayText;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _selectedButton = new Button { Text = Loc.Get("dialog.optionalFontsInstallSelected"), AutoSize = true };
        _allButton = new Button { Text = Loc.Get("dialog.optionalFontsInstallAll"), AutoSize = true };
        _selectedButton.Click += async (_, _) => await InstallAsync(false);
        _allButton.Click += async (_, _) => await InstallAsync(true);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.Add(_selectedButton); buttons.Controls.Add(_allButton);
        var close = new Button { Text = Loc.Get("common.close"), AutoSize = true };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(this.ScaleForDpi(9)), RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(24)));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(42)));
        layout.Controls.Add(_grid, 0, 0); layout.Controls.Add(_status, 0, 1); layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
    }

    private void ReloadRows()
    {
        _grid.Rows.Clear();
        foreach (var pack in Packs)
        {
            foreach (var font in pack.Files)
            {
                var path = FindInstalledFontPath(font.FileName);
                var index = _grid.Rows.Add(GetStyleDisplayName(pack.StyleId), $"{font.Name} ({font.FileName})", path is null
                    ? Loc.Get("dialog.optionalFontsNo")
                    : Loc.Format("dialog.optionalFontsInstalledAt", path));
                _grid.Rows[index].Tag = pack;
            }
        }
    }

    private static string GetStyleDisplayName(string styleId) =>
        StyleService.TryGetStyle(styleId)?.DisplayName ?? styleId;

    internal static IReadOnlySet<string> GetStylesWithMissingFonts()
    {
        return Packs
            .Where(pack => pack.Files.Any(font => FindInstalledFontPath(font.FileName) is null))
            .Select(pack => pack.StyleId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? FindInstalledFontPath(string fileName)
    {
        var userPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts", fileName);
        if (File.Exists(userPath)) return userPath;
        var systemPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", fileName);
        if (File.Exists(systemPath)) return systemPath;

        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (key is null) continue;
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not string value || !string.Equals(Path.GetFileName(value), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                var path = Path.IsPathRooted(value) ? value : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", value);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    private async Task InstallAsync(bool all)
    {
        _selectedButton.Enabled = _allButton.Enabled = false;
        try
        {
            var targets = all ? Packs : (_grid.CurrentRow?.Tag is FontPack selectedPack ? [selectedPack] : []);
            for (var packIndex = 0; packIndex < targets.Length; packIndex++)
            {
                var pack = targets[packIndex];
                _status.Text = Loc.Format("dialog.optionalFontsDownloadingProgress", packIndex + 1, targets.Length, pack.Name);
                await InstallPackAsync(pack, (fontIndex, fontCount, fontName) =>
                {
                    _status.Text = Loc.Format("dialog.optionalFontsInstallingProgress", fontIndex, fontCount, fontName);
                    _status.Refresh();
                });
            }
            ReloadRows();
            _status.Text = Loc.Get("dialog.optionalFontsComplete");
            MessageBox.Show(
                this,
                Loc.Get("dialog.optionalFontsRestartRequired"),
                Loc.Get("dialog.optionalFontsTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _selectedButton.Enabled = _allButton.Enabled = true; }
    }

    private static async Task InstallPackAsync(FontPack pack, Action<int, int, string> reportFontProgress)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MarkLeaf", "1.7.2"));
        var url = $"https://github.com/zhuanshunjishi2017/markleaf/releases/download/1.7.2/{Uri.EscapeDataString(pack.Asset)}";
        var bytes = await client.GetByteArrayAsync(url);
        var root = Path.Combine(Path.GetTempPath(), "MarkLeaf", "optional-fonts", Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, pack.Asset);
        await File.WriteAllBytesAsync(zip, bytes);
        ZipFile.ExtractToDirectory(zip, root);
        var userFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        Directory.CreateDirectory(userFonts);
        var fontFiles = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        for (var index = 0; index < fontFiles.Length; index++)
        {
            var file = fontFiles[index];
            reportFontProgress(index + 1, fontFiles.Length, Path.GetFileName(file));
            var target = Path.Combine(userFonts, Path.GetFileName(file));
            File.Copy(file, target, true);
            NativeMethods.AddFontResourceEx(target, 0, IntPtr.Zero);
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
            key?.SetValue(Path.GetFileNameWithoutExtension(file) + " (TrueType)", target);
        }
        NativeMethods.PostMessage(0xffff, 0x001d, 0, 0);
    }
}
