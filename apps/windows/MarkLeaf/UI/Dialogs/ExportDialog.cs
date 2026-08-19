using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkLeaf.UI.Dialogs;

internal enum ExportDialogMode
{
    Pdf,
    Html,
}

internal sealed class ExportDialog : Form
{
    private readonly PreferencesTabBar _tabBar = new(["PDF", "HTML"], ["\uEA90", "\uE943"]);

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
    { Text = Loc.Get("export.customMargin"), FlatStyle = FlatStyle.System };

    private readonly ComboBox _pdfHeaderPreset = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _pdfFooterPreset = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _customPdfHeaderFooterButton = new()
    { Text = Loc.Get("export.customHeaderFooter"), FlatStyle = FlatStyle.System };

    private string _pdfHeaderCustom = "";

    private string _pdfFooterCustom = "";

    private readonly TextBox _htmlHeader = new()
    { Multiline = true, AcceptsReturn = true };

    private readonly TextBox _htmlFooter = new()
    { Multiline = true, AcceptsReturn = true };

    private readonly ComboBox _pdfStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _htmlStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _pdfColorScheme = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _htmlColorScheme = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _exportButton = new()
    { Text = Loc.Get("export.ok"), FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    private readonly Button _previewButton = new()
    { Text = Loc.Get("export.preview"), FlatStyle = FlatStyle.System };

    private readonly string _defaultFileName;

    private readonly Func<ExportOptions, Task<byte[]>> _generatePdfAsync;

    private readonly Func<ExportOptions, Task<string>> _generateHtmlAsync;

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

    private Control[] _tabContents = [];

    private static readonly (string LabelKey, float Top, float Bottom, float Left, float Right)[] MarginPresets =
    [
        ("export.marginNormal", 25.4f, 25.4f, 31.7f, 31.7f),
        ("export.marginNarrow", 12.7f, 12.7f, 12.7f, 12.7f),
        ("export.marginWide", 50.8f, 50.8f, 50.8f, 50.8f),
        ("export.marginCustom", 16f, 16f, 16f, 16f),
    ];

    private const string HeaderFooterNone = "none";
    private const string HeaderFooterTitleLeft = "title-left";
    private const string HeaderFooterPageCenter = "page-center";
    private const string HeaderFooterPageRight = "page-right";
    private const string HeaderFooterPageTotalCenter = "page-total-center";
    private const string HeaderFooterCustom = "custom";

    private static readonly (string Id, string LabelKey)[] HeaderFooterPresets =
    [
        (HeaderFooterNone, "export.headerFooterNone"),
        (HeaderFooterTitleLeft, "export.headerFooterTitleLeft"),
        (HeaderFooterPageCenter, "export.headerFooterPageCenter"),
        (HeaderFooterPageRight, "export.headerFooterPageRight"),
        (HeaderFooterPageTotalCenter, "export.headerFooterPageTotalCenter"),
        (HeaderFooterCustom, "export.headerFooterCustom"),
    ];

    private readonly IReadOnlyList<(string Id, string DisplayName)> _styles;

    public ExportDialog(
        string documentFileName,
        string defaultFileName,
        string currentStyle,
        IReadOnlyList<(string Id, string DisplayName)> styles,
        string webView2UserDataDirectory,
        Func<ExportOptions, Task<byte[]>> generatePdfAsync,
        Func<ExportOptions, Task<string>> generateHtmlAsync,
        ExportDialogMode initialMode = ExportDialogMode.Pdf,
        ExportSettings? savedSettings = null)
    {
        _styles = styles;
        _defaultFileName = defaultFileName;
        _webView2UserDataDirectory = webView2UserDataDirectory;
        _generatePdfAsync = generatePdfAsync;
        _generateHtmlAsync = generateHtmlAsync;
        var initialStyleIndex = Math.Max(0, IndexOfStyle(currentStyle));
        Text = Loc.Format("export.title", documentFileName);
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
        BuildHtmlTab(initialStyleIndex);
        PopulateColorSchemes();
        ApplySavedSettings(savedSettings);

        _tabBar.Margin = Padding.Empty;
        _tabContents = [BuildPdfContent(), BuildHtmlContent()];
        _contentPanel.Controls.Add(_tabContents[0]);
        _tabBar.TabChanged += async (_, index) =>
        {
            SwitchTabPage(index);
            await RefreshPreviewAsync();
        };
        _tabBar.SelectedIndex = initialMode == ExportDialogMode.Html ? 1 : 0;

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
            ColumnCount = 2,
            RowCount = 3,
        };
        layout.Paint += PaintLeftPanelBottomBorderExtension;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(_tabBar, 0, 0);
        layout.Controls.Add(_contentPanel, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        var rightBorder = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = GetLeftPanelBorderColor(),
        };
        layout.Controls.Add(rightBorder, 1, 0);
        layout.SetRowSpan(rightBorder, 3);

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
            _tabBar.ApplyThemeColors(ColorThemeService.GetActiveColors());
            if (ColorThemeService.IsActiveThemeDark())
            {
                DarkModeService.ApplyDialogDarkMode(this, SystemColors.Control, SystemColors.ControlText);
                DarkModeService.SetWindowDarkTitleBar(this);
                ForceComboDark(_pageSize);
                ForceComboDark(_marginPreset);
                ForceComboDark(_pdfHeaderPreset);
                ForceComboDark(_pdfFooterPreset);
                ForceComboDark(_pdfStyle);
                ForceComboDark(_pdfColorScheme);
                ForceComboDark(_htmlStyle);
                ForceComboDark(_htmlColorScheme);
            }
        };
        Shown += async (_, _) =>
        {
            await InitializePreviewAsync();
            await RefreshPreviewAsync();
        };
    }

    private static Color GetLeftPanelBorderColor()
    {
        var colors = ColorThemeService.GetActiveColors();
        return colors.TryGetValue("bg-selected-hover", out var bgSelectedHover)
            ? bgSelectedHover
            : SystemColors.ControlLight;
    }

    private static void PaintLeftPanelBottomBorderExtension(object? sender, PaintEventArgs eventArgs)
    {
        if (sender is not Control control)
        {
            return;
        }

        var bottomInset = control.Padding.Bottom;
        if (bottomInset <= 0)
        {
            return;
        }

        using var pen = new Pen(GetLeftPanelBorderColor(), 1);
        var x = control.ClientSize.Width - 1;
        var y1 = Math.Max(0, control.ClientSize.Height - bottomInset);
        eventArgs.Graphics.DrawLine(pen, x, y1, x, control.ClientSize.Height);
    }

    private void PopulateColorSchemes()
    {
        var activeThemeIndex = 0;
        var allThemes = ColorThemeService.All;
        for (var i = 0; i < allThemes.Count; i++)
        {
            var displayName = allThemes[i].DisplayName;
            _pdfColorScheme.Items.Add(displayName);
            _htmlColorScheme.Items.Add(displayName);
            if (string.Equals(allThemes[i].Id, ColorThemeService.ActiveThemeId, StringComparison.Ordinal))
                activeThemeIndex = i;
        }
        _pdfColorScheme.SelectedIndex = activeThemeIndex;
        _htmlColorScheme.SelectedIndex = activeThemeIndex;
    }

    private void ApplySavedSettings(ExportSettings? savedSettings)
    {
        if (savedSettings is null)
        {
            return;
        }

        SelectComboItem(_pageSize, NormalizePaperSize(savedSettings.PaperSize));
        _landscape.Checked = savedSettings.Landscape;
        _portrait.Checked = !savedSettings.Landscape;
        ApplySavedMargins(
            NormalizeMargin(savedSettings.MarginTop, 25.4f),
            NormalizeMargin(savedSettings.MarginBottom, 25.4f),
            NormalizeMargin(savedSettings.MarginLeft, 31.7f),
            NormalizeMargin(savedSettings.MarginRight, 31.7f));

        _pdfHeaderCustom = savedSettings.PdfHeaderCustom ?? "";
        _pdfFooterCustom = savedSettings.PdfFooterCustom ?? "";
        SelectHeaderFooterPreset(_pdfHeaderPreset, savedSettings.PdfHeaderPreset, _pdfHeaderCustom);
        SelectHeaderFooterPreset(_pdfFooterPreset, savedSettings.PdfFooterPreset, _pdfFooterCustom);

        _htmlHeader.Text = savedSettings.HtmlHeader ?? "";
        _htmlFooter.Text = savedSettings.HtmlFooter ?? "";
    }

    private void ApplySavedMargins(float top, float bottom, float left, float right)
    {
        _marginTop = top;
        _marginBottom = bottom;
        _marginLeft = left;
        _marginRight = right;

        var presetIndex = IndexOfMarginPreset(top, bottom, left, right);
        _marginPreset.SelectedIndex = presetIndex;
        UpdateMarginLabel();
    }

    private static int IndexOfMarginPreset(float top, float bottom, float left, float right)
    {
        for (var i = 0; i < MarginPresets.Length - 1; i++)
        {
            var p = MarginPresets[i];
            if (SameMargin(top, p.Top)
                && SameMargin(bottom, p.Bottom)
                && SameMargin(left, p.Left)
                && SameMargin(right, p.Right))
            {
                return i;
            }
        }

        return 3;
    }

    private static bool SameMargin(float left, float right) => Math.Abs(left - right) < 0.05f;

    private static float NormalizeMargin(float margin, float fallback) =>
        float.IsFinite(margin) && margin >= 0f && margin <= 1000f ? margin : fallback;

    private static string NormalizePaperSize(string? paperSize)
    {
        string[] valid = ["A4", "A3", "A5", "Letter", "Legal", "B4", "B5"];
        return valid.Contains(paperSize, StringComparer.Ordinal) ? paperSize! : "A4";
    }

    private static void SelectComboItem(ComboBox combo, string value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is string item && string.Equals(item, value, StringComparison.Ordinal))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static int IndexOfColorTheme(string? colorThemeId)
    {
        if (!string.IsNullOrWhiteSpace(colorThemeId))
        {
            for (var i = 0; i < ColorThemeService.All.Count; i++)
            {
                if (string.Equals(ColorThemeService.All[i].Id, colorThemeId, StringComparison.Ordinal))
                    return i;
            }
        }

        for (var i = 0; i < ColorThemeService.All.Count; i++)
        {
            if (string.Equals(ColorThemeService.All[i].Id, ColorThemeService.ActiveThemeId, StringComparison.Ordinal))
                return i;
        }

        return 0;
    }

    private void SwitchTabPage(int index)
    {
        if (index < 0 || index >= _tabContents.Length) return;
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(_tabContents[index]);
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
        var isPdf = _tabBar.SelectedIndex == 0;
        var colorThemeId = GetSelectedColorThemeId(isPdf ? _pdfColorScheme : _htmlColorScheme);
        var pdfHeader = ResolvePdfHeaderFooter(_pdfHeaderPreset, _pdfHeaderCustom);
        var pdfFooter = ResolvePdfHeaderFooter(_pdfFooterPreset, _pdfFooterCustom);
        return new ExportOptions(
            Format: isPdf ? "pdf" : "html",
            PaperSize: (string)_pageSize.SelectedItem!,
            Landscape: _landscape.Checked,
            MarginTop: _marginTop,
            MarginBottom: _marginBottom,
            MarginLeft: _marginLeft,
            MarginRight: _marginRight,
            HtmlHeader: isPdf ? "" : _htmlHeader.Text,
            HtmlFooter: isPdf ? "" : _htmlFooter.Text,
            PdfHeaderText: isPdf ? pdfHeader.Text : "",
            PdfHeaderAlignment: isPdf ? pdfHeader.Alignment : "",
            PdfFooterText: isPdf ? pdfFooter.Text : "",
            PdfFooterAlignment: isPdf ? pdfFooter.Alignment : "",
            PdfHeaderPreset: GetSelectedHeaderFooterPreset(_pdfHeaderPreset),
            PdfFooterPreset: GetSelectedHeaderFooterPreset(_pdfFooterPreset),
            PdfHeaderCustom: _pdfHeaderCustom,
            PdfFooterCustom: _pdfFooterCustom,
            Style: MapExportStyle((string)(isPdf ? _pdfStyle : _htmlStyle).SelectedItem!),
            ColorScheme: colorThemeId,
            OutputPath: _outputPath ?? "");
    }

    private static string GetSelectedColorThemeId(ComboBox combo)
    {
        return combo.SelectedIndex >= 0 && combo.SelectedIndex < ColorThemeService.All.Count
            ? ColorThemeService.All[combo.SelectedIndex].Id
            : ColorThemeService.ActiveThemeId;
    }

    private void OnExportClick(object? sender, EventArgs eventArgs)
    {
        var isPdf = _tabBar.SelectedIndex == 0;
        var extension = isPdf ? "pdf" : "html";
        var filter = isPdf ? $"{Loc.Get("export.pdf")}|*.pdf" : $"{Loc.Get("export.html")}|*.html";
        using var dialog = new SaveFileDialog
        {
            Filter = filter,
            RestoreDirectory = true,
            FileName = $"{_defaultFileName}.{extension}",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _outputPath = dialog.FileName;

        var previewPath = isPdf && _previewDir is not null ? Path.Combine(_previewDir, "preview.pdf") : null;
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
            // Preview initialization failures should not block export.
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
            if (_tabBar.SelectedIndex == 0)
            {
                var pdfBytes = await _generatePdfAsync(BuildOptions());
                if (pdfBytes.Length == 0) return;

                var pdfPath = Path.Combine(_previewDir, "preview.pdf");
                await File.WriteAllBytesAsync(pdfPath, pdfBytes);
                _previewView.CoreWebView2.Navigate(
                    $"https://preview.local/preview.pdf?t={Guid.NewGuid():N}#toolbar=0&navpanes=0");
                return;
            }

            var html = await _generateHtmlAsync(BuildOptions());
            if (string.IsNullOrEmpty(html)) return;

            var htmlPath = Path.Combine(_previewDir, "preview.html");
            await File.WriteAllTextAsync(htmlPath, html, System.Text.Encoding.UTF8);
            _previewView.CoreWebView2.Navigate($"https://preview.local/preview.html?t={Guid.NewGuid():N}");
        }
        catch
        {
            // Preview generation failures should not block export.
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

        foreach (var (labelKey, _, _, _, _) in MarginPresets)
            _marginPreset.Items.Add(Loc.Get(labelKey));
        _marginPreset.SelectedIndex = 0;
        _marginPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_marginPreset.SelectedIndex != 3) ApplyMargins(_marginPreset.SelectedIndex);
        };
        _customMarginButton.Click += (_, _) => OpenCustomMarginDialog();
        ApplyMargins(0);

        InitHeaderFooterPreset(_pdfHeaderPreset);
        InitHeaderFooterPreset(_pdfFooterPreset);
        _customPdfHeaderFooterButton.Click += (_, _) => OpenPdfHeaderFooterDialog();

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

        panel.Controls.Add(CategoryLabel(Loc.Get("export.headerFooter.label")), 0, 4);
        panel.Controls.Add(BuildPdfHeaderFooterSection(), 1, 4);

        panel.Controls.Add(CategoryGap(), 0, 5);
        panel.Controls.Add(CategoryGap(), 1, 5);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.style.label")), 0, 6);
        panel.Controls.Add(_pdfStyle, 1, 6);

        panel.Controls.Add(CategoryGap(), 0, 7);
        panel.Controls.Add(CategoryGap(), 1, 7);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.colorScheme.label")), 0, 8);
        panel.Controls.Add(_pdfColorScheme, 1, 8);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 9);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 9);

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

    private Control BuildPdfHeaderFooterSection()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(NewInlineLabel(Loc.Get("export.header")), 0, 0);
        grid.Controls.Add(_pdfHeaderPreset, 0, 1);
        grid.Controls.Add(NewInlineLabel(Loc.Get("export.footer")), 0, 2);
        grid.Controls.Add(_pdfFooterPreset, 0, 3);
        grid.Controls.Add(_customPdfHeaderFooterButton, 0, 4);

        return grid;
    }

    private void BuildHtmlTab(int styleIndex)
    {
        _htmlHeader.PlaceholderText = Loc.Get("export.headerPlaceholder");
        _htmlFooter.PlaceholderText = Loc.Get("export.footerPlaceholder");
        InitCombo(_htmlStyle, StyleDisplayNames(), styleIndex);
    }

    private Control BuildHtmlContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, this.ScaleForDpi(11), 0, this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(CategoryLabel(Loc.Get("export.htmlHeader")), 0, 0);
        panel.Controls.Add(_htmlHeader, 1, 0);

        panel.Controls.Add(CategoryGap(), 0, 1);
        panel.Controls.Add(CategoryGap(), 1, 1);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.htmlFooter")), 0, 2);
        panel.Controls.Add(_htmlFooter, 1, 2);

        panel.Controls.Add(CategoryGap(), 0, 3);
        panel.Controls.Add(CategoryGap(), 1, 3);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.style.label")), 0, 4);
        panel.Controls.Add(_htmlStyle, 1, 4);

        panel.Controls.Add(CategoryGap(), 0, 5);
        panel.Controls.Add(CategoryGap(), 1, 5);

        panel.Controls.Add(CategoryLabel(Loc.Get("export.colorScheme.label")), 0, 6);
        panel.Controls.Add(_htmlColorScheme, 1, 6);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 7);

        return panel;
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
        _customPdfHeaderFooterButton.Width = _customMarginButton.Width;
        _customPdfHeaderFooterButton.Height = this.ScaleForDpi(24);
        _marginLabel.Width = this.ScaleForDpi(160);
        _marginLabel.Height = this.ScaleForDpi(36);

        var htmlW = this.ScaleForDpi(150);
        var htmlH = this.ScaleForDpi(60);
        _htmlHeader.Width = htmlW;
        _htmlHeader.Height = htmlH;
        _htmlFooter.Width = htmlW;
        _htmlFooter.Height = htmlH;

        var comboW = this.ScaleForDpi(120);
        _pdfStyle.Width = comboW;
        _htmlStyle.Width = comboW;
        _pdfColorScheme.Width = comboW;
        _htmlColorScheme.Width = comboW;
        _pdfHeaderPreset.Width = comboW;
        _pdfFooterPreset.Width = comboW;

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
        var oldTop = _marginTop;
        var oldBottom = _marginBottom;
        var oldLeft = _marginLeft;
        var oldRight = _marginRight;
        using var dialog = new MarginDialog(_marginTop, _marginBottom, _marginLeft, _marginRight);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _marginTop = dialog.MarginTop;
        _marginBottom = dialog.MarginBottom;
        _marginLeft = dialog.MarginLeft;
        _marginRight = dialog.MarginRight;
        if (!SameMargin(oldTop, _marginTop)
            || !SameMargin(oldBottom, _marginBottom)
            || !SameMargin(oldLeft, _marginLeft)
            || !SameMargin(oldRight, _marginRight))
        {
            _marginPreset.SelectedIndex = 3;
        }
        UpdateMarginLabel();
    }

    private void OpenPdfHeaderFooterDialog()
    {
        using var dialog = new PdfHeaderFooterDialog(_pdfHeaderCustom, _pdfFooterCustom);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _pdfHeaderCustom = dialog.HeaderText;
        _pdfFooterCustom = dialog.FooterText;
        if (!string.IsNullOrWhiteSpace(_pdfHeaderCustom))
            SelectHeaderFooterPresetById(_pdfHeaderPreset, HeaderFooterCustom);
        if (!string.IsNullOrWhiteSpace(_pdfFooterCustom))
            SelectHeaderFooterPresetById(_pdfFooterPreset, HeaderFooterCustom);
    }

    private static void InitHeaderFooterPreset(ComboBox combo)
    {
        combo.Items.Clear();
        combo.Items.AddRange(HeaderFooterPresets.Select(p => Loc.Get(p.LabelKey)).ToArray());
        combo.SelectedIndex = 0;
    }

    private static void SelectHeaderFooterPreset(ComboBox combo, string? preset, string? customText)
    {
        var id = NormalizeHeaderFooterPreset(preset);
        if (!string.IsNullOrWhiteSpace(customText) && id == HeaderFooterNone)
        {
            id = HeaderFooterCustom;
        }
        SelectHeaderFooterPresetById(combo, id);
    }

    private static void SelectHeaderFooterPresetById(ComboBox combo, string preset)
    {
        var id = NormalizeHeaderFooterPreset(preset);
        for (var i = 0; i < HeaderFooterPresets.Length; i++)
        {
            if (HeaderFooterPresets[i].Id == id)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetSelectedHeaderFooterPreset(ComboBox combo) =>
        combo.SelectedIndex >= 0 && combo.SelectedIndex < HeaderFooterPresets.Length
            ? HeaderFooterPresets[combo.SelectedIndex].Id
            : HeaderFooterNone;

    private static string NormalizeHeaderFooterPreset(string? preset) =>
        HeaderFooterPresets.Any(p => p.Id == preset) ? preset! : HeaderFooterNone;

    private static (string Text, string Alignment) ResolvePdfHeaderFooter(ComboBox combo, string customText)
    {
        return GetSelectedHeaderFooterPreset(combo) switch
        {
            HeaderFooterTitleLeft => ("{document-title}", "left"),
            HeaderFooterPageCenter => ("{page}", "center"),
            HeaderFooterPageRight => ("{page}", "right"),
            HeaderFooterPageTotalCenter => (Loc.Get("export.headerFooterPageTotalTemplate"), "center"),
            HeaderFooterCustom => (customText, "center"),
            _ => ("", ""),
        };
    }

    private static Label NewInlineLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 6, 3),
        };
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
    string PdfHeaderText,
    string PdfHeaderAlignment,
    string PdfFooterText,
    string PdfFooterAlignment,
    string PdfHeaderPreset,
    string PdfFooterPreset,
    string PdfHeaderCustom,
    string PdfFooterCustom,
    string Style,
    string ColorScheme,
    string OutputPath);
