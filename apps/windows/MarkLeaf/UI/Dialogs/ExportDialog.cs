using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.UI.Dialogs;

internal sealed class ExportDialog : Form
{
    private readonly Panel _contentPanel = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Margin = Padding.Empty,
        BackColor = SystemColors.ControlLightLight,
    };

    private readonly ComboBox _pageSize = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly RadioButton _portrait = new()
    { Text = Loc.Get("export.portrait"), AutoSize = true, Padding = new Padding(0, 0, 24, 0), FlatStyle = FlatStyle.System };

    private readonly RadioButton _landscape = new()
    { Text = Loc.Get("export.landscape"), AutoSize = true, FlatStyle = FlatStyle.System };

    private readonly ComboBox _marginPreset = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private float _marginTop;
    private float _marginBottom;
    private float _marginLeft;
    private float _marginRight;

    private readonly Label _marginLabel = new()
    { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };

    private readonly Button _customMarginButton = new()
    { Text = Loc.Get("export.customMargin"), FlatStyle = FlatStyle.System, Enabled = false };

    private readonly ComboBox _pdfStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _pdfColorScheme = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _exportButton = new()
    { Text = Loc.Get("export.ok"), FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    private readonly Button _previewButton = new()
    { Text = Loc.Get("export.preview"), FlatStyle = FlatStyle.System };

    private readonly string _defaultFileName;

    private readonly Func<ExportOptions, Task<byte[]>> _generatePdfAsync;

    private readonly string _webView2UserDataDirectory;

    private readonly WebView2 _previewView = new()
    { Dock = DockStyle.Fill, BackColor = Color.White };

    private readonly Label _loadingLabel = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = Loc.Get("export.previewLoading"),
        BackColor = Color.White,
        ForeColor = SystemColors.GrayText,
    };

    private string? _previewDir;

    private bool _previewInitialized;

    private string? _outputPath;

    private string? _exportedPath;

    private static readonly (string Label, float Top, float Bottom, float Left, float Right)[] MarginPresets =
    [
        (Loc.Get("export.marginNormal"), 25.4f, 25.4f, 31.7f, 31.7f),
        (Loc.Get("export.marginNarrow"), 12.7f, 12.7f, 12.7f, 12.7f),
        (Loc.Get("export.marginWide"), 50.8f, 50.8f, 50.8f, 50.8f),
        (Loc.Get("export.marginCustom"), 16f, 16f, 16f, 16f),
    ];

    private readonly IReadOnlyList<(string Id, string DisplayName)> _styles;

    public ExportDialog(
        string documentFileName,
        string defaultFileName,
        string currentStyle,
        IReadOnlyList<(string Id, string DisplayName)> styles,
        string webView2UserDataDirectory,
        Func<ExportOptions, Task<byte[]>> generatePdfAsync)
    {
        _styles = styles;
        _defaultFileName = defaultFileName;
        _webView2UserDataDirectory = webView2UserDataDirectory;
        _generatePdfAsync = generatePdfAsync;
        var initialStyleIndex = Math.Max(0, IndexOfStyle(currentStyle));
        Text = Loc.Format("export.pdfTitle", documentFileName);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(this.ScaleForDpi(920), this.ScaleForDpi(720));
        MinimumSize = new Size(this.ScaleForDpi(720), this.ScaleForDpi(560));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "App", "fileicon.ico");
        if (File.Exists(iconPath))
        {
            try { Icon = new Icon(iconPath); }
            catch { /* icon file may be malformed; title bar will use default */ }
        }


        ApplyDpiSizes();

        BuildPdfTab(initialStyleIndex);

        var activeThemeIndex = 0;
        var allThemes = ColorThemeService.All;
        for (var i = 0; i < allThemes.Count; i++)
        {
            var displayName = allThemes[i].DisplayName;
            _pdfColorScheme.Items.Add(displayName);
            if (string.Equals(allThemes[i].Id, ColorThemeService.ActiveThemeId, StringComparison.Ordinal))
                activeThemeIndex = i;
        }
        _pdfColorScheme.SelectedIndex = activeThemeIndex;

        _contentPanel.Controls.Add(BuildPdfContent());

        _exportButton.Click += OnExportClick;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _previewButton.Click += OnPreviewClick;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = this.ScaleForDpi(26),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(this.ScaleForDpi(23), this.ScaleForDpi(11), this.ScaleForDpi(23), 0),
            BackColor = SystemColors.ControlLightLight,
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_exportButton);
        buttons.Controls.Add(_previewButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(23)),
            BackColor = SystemColors.ControlLightLight,
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(_contentPanel, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1,
            Padding = Padding.Empty,
            BackColor = SystemColors.ControlLightLight,
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(300)));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var previewPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        previewPanel.Controls.Add(_previewView);
        previewPanel.Controls.Add(_loadingLabel);
        _loadingLabel.BringToFront();

        mainLayout.Controls.Add(layout, 0, 0);
        mainLayout.Controls.Add(previewPanel, 1, 0);

        Controls.Add(mainLayout);

        AcceptButton = _exportButton;
        CancelButton = _cancelButton;

        Shown += (_, _) =>
        {
            if (ColorThemeService.IsActiveThemeDark())
            {
                DarkModeService.ApplyDialogDarkMode(this, SystemColors.Control, SystemColors.ControlText);
                DarkModeService.SetWindowDarkTitleBar(this);
                ForceComboDark(_pageSize);
                ForceComboDark(_marginPreset);
                ForceComboDark(_pdfStyle);
            }
        };
        Shown += async (_, _) =>
        {
            await InitializePreviewAsync();
            await RefreshPreviewAsync();
        };
    }

    private static void ForceComboDark(ComboBox combo)
    {
        if (!combo.IsHandleCreated) return;
        typeof(Control).GetMethod("RecreateHandle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(combo, null);
    }

    public ExportOptions? Options => _outputPath is null ? null : BuildOptions();

    public string? ExportedPath => _exportedPath;

    private ExportOptions BuildOptions()
    {
        var colorThemeId = _pdfColorScheme.SelectedIndex >= 0
            && _pdfColorScheme.SelectedIndex < ColorThemeService.All.Count
                ? ColorThemeService.All[_pdfColorScheme.SelectedIndex].Id
                : ColorThemeService.ActiveThemeId;
        return new ExportOptions(
            Format: "pdf",
            PaperSize: (string)_pageSize.SelectedItem!,
            Landscape: _landscape.Checked,
            MarginTop: _marginTop,
            MarginBottom: _marginBottom,
            MarginLeft: _marginLeft,
            MarginRight: _marginRight,
            HtmlHeader: "",
            HtmlFooter: "",
            Style: MapExportStyle((string)_pdfStyle.SelectedItem!),
            ColorScheme: colorThemeId,
            OutputPath: _outputPath ?? "");
    }

    private void OnExportClick(object? sender, EventArgs eventArgs)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = $"{Loc.Get("export.pdf")}|*.pdf",
            RestoreDirectory = true,
            FileName = $"{_defaultFileName}.pdf",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _outputPath = dialog.FileName;

        // 预览 PDF 已生成时，直接复制临时文件到目标路径，避免重新生成。
        var previewPath = _previewDir is null ? null : Path.Combine(_previewDir, "preview.pdf");
        if (previewPath is not null && File.Exists(previewPath))
        {
            try
            {
                File.Copy(previewPath, _outputPath, overwrite: true);
                _exportedPath = _outputPath;
            }
            catch
            {
                _exportedPath = null;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task InitializePreviewAsync()
    {
        if (_previewInitialized) return;
        _previewInitialized = true;

        _previewDir = Path.Combine(Path.GetTempPath(), $"markleaf-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_previewDir);

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _webView2UserDataDirectory);
            await _previewView.EnsureCoreWebView2Async(environment);
            _previewView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _previewView.CoreWebView2.NavigationStarting += (_, _) => _loadingLabel.Visible = true;
            _previewView.CoreWebView2.NavigationCompleted += (_, _) => _loadingLabel.Visible = false;
            _previewView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "preview.local", _previewDir, CoreWebView2HostResourceAccessKind.DenyCors);
        }
        catch
        {
            // 预览初始化失败时保持空白预览区，不影响导出。
        }
    }

    private async void OnPreviewClick(object? sender, EventArgs e)
    {
        await RefreshPreviewAsync();
    }

    private async Task RefreshPreviewAsync()
    {
        if (!_previewInitialized || _previewView.CoreWebView2 is null || _previewDir is null)
        {
            return;
        }

        try
        {
            _previewButton.Enabled = false;
            var pdfBytes = await _generatePdfAsync(BuildOptions());
            if (pdfBytes.Length == 0) return;

            var pdfPath = Path.Combine(_previewDir, "preview.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);
            _previewView.CoreWebView2.Navigate(
                $"https://preview.local/preview.pdf?t={Guid.NewGuid():N}#toolbar=0&navpanes=0");
        }
        catch
        {
            // 预览生成失败时静默返回。
        }
        finally
        {
            _previewButton.Enabled = true;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_previewDir is not null)
        {
            try { Directory.Delete(_previewDir, recursive: true); } catch { }
            _previewDir = null;
        }
    }

    private void BuildPdfTab(int styleIndex)
    {
        _pageSize.Items.AddRange(["A4", "A3", "A5", "Letter", "Legal", "B4", "B5"]);
        _pageSize.SelectedIndex = 0;
        _portrait.Checked = true;

        foreach (var (label, _, _, _, _) in MarginPresets)
            _marginPreset.Items.Add(label);
        _marginPreset.SelectedIndex = 0;
        _marginPreset.SelectedIndexChanged += (_, _) =>
        {
            var custom = _marginPreset.SelectedIndex == 3;
            _customMarginButton.Enabled = custom;
            if (!custom) ApplyMargins(_marginPreset.SelectedIndex);
        };
        _customMarginButton.Click += (_, _) => OpenCustomMarginDialog();
        ApplyMargins(0);

        InitCombo(_pdfStyle, StyleDisplayNames(), styleIndex);
    }

    private Control BuildPdfContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, this.ScaleForDpi(11), 0, this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(CategoryLabel(Loc.Get("export.paper.label")), 0, 0);
        panel.Controls.Add(BuildPdfMiscSection(), 1, 0);

        panel.Controls.Add(CategoryGap(), 0, 1);
        panel.Controls.Add(CategoryGap(), 1, 1);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.margins.label")), 0, 2);
        panel.Controls.Add(BuildMarginSection(), 1, 2);

        panel.Controls.Add(CategoryGap(), 0, 3);
        panel.Controls.Add(CategoryGap(), 1, 3);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.style.label")), 0, 4);
        panel.Controls.Add(_pdfStyle, 1, 4);

        panel.Controls.Add(CategoryGap(), 0, 5);
        panel.Controls.Add(CategoryGap(), 1, 5);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.colorScheme.label")), 0, 6);
        panel.Controls.Add(_pdfColorScheme, 1, 6);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 7);

        return panel;
    }

    private Control BuildPdfMiscSection()
    {
        var grid = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(_pageSize, 0, 0);

        var ori = new FlowLayoutPanel { AutoSize = true };
        ori.Controls.Add(_portrait);
        ori.Controls.Add(_landscape);
        grid.Controls.Add(ori, 0, 1);

        return grid;
    }

    private Control BuildMarginSection()
    {
        var mg = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        mg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mg.Controls.Add(_marginPreset, 0, 0);
        mg.Controls.Add(_marginLabel, 0, 1);
        mg.Controls.Add(_customMarginButton, 0, 2);

        return mg;
    }

    private Label CategoryLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Bold),
            Margin = new Padding(this.ScaleForDpi(11), this.ScaleForDpi(6), 0, 0),

        };
    }

    private Control CategoryGap() => new Panel { Height = this.ScaleGapForDpi(), Width = 0, Dock = DockStyle.None };

    private string MapExportStyle(string label)
    {
        foreach (var (id, displayName) in _styles)
        {
            if (displayName == label) return id;
        }

        return "serif";
    }

    private string[] StyleDisplayNames()
    {
        return _styles.Select(s => s.DisplayName).ToArray();
    }

    private int IndexOfStyle(string styleId)
    {
        for (var i = 0; i < _styles.Count; i++)
        {
            if (_styles[i].Id == styleId) return i;
        }

        return 0;
    }

    private static void InitCombo(ComboBox combo, string[] items, int selected)
    {
        combo.Items.Clear();
        combo.Items.AddRange(items);
        combo.SelectedIndex = selected;
    }

    private void ApplyDpiSizes()
    {
        _contentPanel.Padding = new Padding(
            this.ScaleForDpi(23), this.ScaleForDpi(15), this.ScaleForDpi(23), this.ScaleForDpi(6));

        _pageSize.Width = this.ScaleForDpi(109);
        _marginPreset.Width = this.ScaleForDpi(86);

        _customMarginButton.Width = this.ScaleForDpi(120);
        _customMarginButton.Height = this.ScaleForDpi(24);
        _marginLabel.Width = this.ScaleForDpi(160);
        _marginLabel.Height = this.ScaleForDpi(36);

        var comboW = this.ScaleForDpi(120);
        _pdfStyle.Width = comboW;
        _pdfColorScheme.Width = comboW;

        var btnW = this.ScaleForDpi(64);
        var btnH = this.ScaleForDpi(26);
        _exportButton.Width = btnW;
        _exportButton.Height = btnH;
        _cancelButton.Width = btnW;
        _cancelButton.Height = btnH;
        _previewButton.Width = this.ScaleForDpi(96);
        _previewButton.Height = btnH;
    }

    private void ApplyMargins(int presetIndex)
    {
        var p = MarginPresets[presetIndex];
        _marginTop = p.Top;
        _marginBottom = p.Bottom;
        _marginLeft = p.Left;
        _marginRight = p.Right;
        UpdateMarginLabel();
    }

    private void UpdateMarginLabel()
    {
        _marginLabel.Text = Loc.Format(
            "export.marginSummary",
            FormatMillimeters(_marginTop),
            FormatMillimeters(_marginBottom),
            FormatMillimeters(_marginLeft),
            FormatMillimeters(_marginRight));
    }

    private static string FormatMillimeters(float value)
    {
        return value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void OpenCustomMarginDialog()
    {
        using var dialog = new MarginDialog(_marginTop, _marginBottom, _marginLeft, _marginRight);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _marginTop = dialog.MarginTop;
        _marginBottom = dialog.MarginBottom;
        _marginLeft = dialog.MarginLeft;
        _marginRight = dialog.MarginRight;
        UpdateMarginLabel();
    }
}

internal sealed record ExportOptions(
    string Format,
    string PaperSize,
    bool Landscape,
    float MarginTop,
    float MarginBottom,
    float MarginLeft,
    float MarginRight,
    string HtmlHeader,
    string HtmlFooter,
    string Style,
    string ColorScheme,
    string OutputPath);
