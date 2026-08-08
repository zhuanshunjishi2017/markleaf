using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class ExportDialog : Form
{
    private readonly PreferencesTabBar _tabBar;
    private readonly Panel _contentPanel = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Margin = Padding.Empty,
        Padding = new Padding(40, 10, 40, 10),
        BackColor = SystemColors.ControlLightLight,
    };
    private Control[] _tabContents = [];

    private readonly ComboBox _pageSize = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };

    private readonly RadioButton _portrait = new()
    { Text = Loc.Get("export.portrait"), AutoSize = true, Padding = new Padding(0, 0, 24, 0), FlatStyle = FlatStyle.System };

    private readonly RadioButton _landscape = new()
    { Text = Loc.Get("export.landscape"), AutoSize = true, FlatStyle = FlatStyle.System };

    private readonly ComboBox _marginPreset = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };

    private readonly NumericUpDown _marginTop = new()
    { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1, Width = 80, Enabled = false };

    private readonly NumericUpDown _marginBottom = new()
    { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1, Width = 80, Enabled = false };

    private readonly NumericUpDown _marginLeft = new()
    { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1, Width = 80, Enabled = false };

    private readonly NumericUpDown _marginRight = new()
    { Minimum = 0, Maximum = 100, DecimalPlaces = 1, Increment = 1, Width = 80, Enabled = false };

    private readonly TextBox _htmlHeader = new()
    { Multiline = true, AcceptsReturn = true, Height = 105, Width = 650 };

    private readonly TextBox _htmlFooter = new()
    { Multiline = true, AcceptsReturn = true, Height = 105, Width = 650 };

    private readonly ComboBox _pdfStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

    private readonly ComboBox _htmlStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

    private readonly ComboBox _pdfColorScheme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

    private readonly ComboBox _htmlColorScheme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };

    private readonly Button _exportButton = new()
    { Text = Loc.Get("export.ok"), Width = 150, Height = 45, FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), Width = 150, Height = 45, FlatStyle = FlatStyle.System };

    private readonly string _defaultFileName;

    private string? _outputPath;

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
        IReadOnlyList<(string Id, string DisplayName)> styles)
    {
        _styles = styles;
        _defaultFileName = defaultFileName;
        var initialStyleIndex = Math.Max(0, IndexOfStyle(currentStyle));
        Text = Loc.Format("export.title", documentFileName);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(800, 750);

        _tabBar = new PreferencesTabBar(["PDF", "HTML"], ["", ""]);

        BuildPdfTab(initialStyleIndex);
        BuildHtmlTab(initialStyleIndex);

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

        _tabBar.Margin = Padding.Empty;
        _tabContents = [BuildPdfContent(), BuildHtmlContent()];
        _contentPanel.Controls.Add(_tabContents[0]);
        _tabBar.TabChanged += (_, index) => SwitchTabPage(index);

        _exportButton.Click += OnExportClick;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 45,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(40, 20, 40, 0),
            BackColor = SystemColors.ControlLightLight,
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_exportButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, 40),
            BackColor = SystemColors.ControlLightLight,
            ColumnCount = 1,
            RowCount = 3,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(_tabBar, 0, 0);
        layout.Controls.Add(_contentPanel, 0, 1);
        layout.Controls.Add(buttons, 0, 2);

        Controls.Add(layout);

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
                ForceComboDark(_pdfStyle);
            }
        };
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

    public ExportOptions? Options
    {
        get
        {
            if (_outputPath is null) return null;
            var isPdf = _tabBar.SelectedIndex == 0;
            var schemeCombo = isPdf ? _pdfColorScheme : _htmlColorScheme;
            var colorThemeId = schemeCombo.SelectedIndex >= 0
                && schemeCombo.SelectedIndex < ColorThemeService.All.Count
                    ? ColorThemeService.All[schemeCombo.SelectedIndex].Id
                    : ColorThemeService.ActiveThemeId;
            return new ExportOptions(
                Format: isPdf ? "pdf" : "html",
                PaperSize: (string)_pageSize.SelectedItem!,
                Landscape: _landscape.Checked,
                MarginTop: (float)_marginTop.Value,
                MarginBottom: (float)_marginBottom.Value,
                MarginLeft: (float)_marginLeft.Value,
                MarginRight: (float)_marginRight.Value,
                HtmlHeader: _htmlHeader.Text,
                HtmlFooter: _htmlFooter.Text,
                Style: MapExportStyle((string)(isPdf ? _pdfStyle : _htmlStyle).SelectedItem!),
                ColorScheme: colorThemeId,
                OutputPath: _outputPath);
        }
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
        DialogResult = DialogResult.OK;
        Close();
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
            _marginTop.Enabled = _marginBottom.Enabled = _marginLeft.Enabled = _marginRight.Enabled = custom;
            if (!custom) ApplyMargins(_marginPreset.SelectedIndex);
        };
        ApplyMargins(0);

        InitCombo(_pdfStyle, StyleDisplayNames(), styleIndex);
    }

    private Control BuildPdfContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(0, 20, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(CategoryLabel(Loc.Get("export.paper.label")), 0, 0);
        panel.SetColumnSpan(BuildPdfMiscSection(), 2);
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
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(new Label { Text = Loc.Get("export.paperSize"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        grid.Controls.Add(_pageSize, 1, 0);

        var ori = new FlowLayoutPanel { AutoSize = true };
        ori.Controls.Add(_portrait);
        ori.Controls.Add(_landscape);
        grid.Controls.Add(new Label { Text = Loc.Get("export.orientation"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        grid.Controls.Add(ori, 1, 1);

        return grid;
    }

    private Control BuildMarginSection()
    {
        var tbRow = new FlowLayoutPanel { AutoSize = true };
        tbRow.Controls.Add(new Label { Text = Loc.Get("export.marginTop"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        tbRow.Controls.Add(_marginTop);
        tbRow.Controls.Add(new Label { Text = "  " + Loc.Get("export.marginBottom"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        tbRow.Controls.Add(_marginBottom);

        var lrRow = new FlowLayoutPanel { AutoSize = true };
        lrRow.Controls.Add(new Label { Text = Loc.Get("export.marginLeft"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        lrRow.Controls.Add(_marginLeft);
        lrRow.Controls.Add(new Label { Text = "  " + Loc.Get("export.marginRight"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        lrRow.Controls.Add(_marginRight);

        var mg = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        mg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mg.Controls.Add(_marginPreset, 0, 0);
        mg.Controls.Add(tbRow, 0, 1);
        mg.Controls.Add(lrRow, 0, 2);

        return mg;
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
            Padding = new Padding(0, 20, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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

    private static Label CategoryLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Bold),
            Margin = new Padding(20, 10, 0, 0),

        };
    }

    private static Control CategoryGap() => new Panel { Height = 20, Dock = DockStyle.None };

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

    private void ApplyMargins(int presetIndex)
    {
        var p = MarginPresets[presetIndex];
        _marginTop.Value = (decimal)p.Top;
        _marginBottom.Value = (decimal)p.Bottom;
        _marginLeft.Value = (decimal)p.Left;
        _marginRight.Value = (decimal)p.Right;
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
