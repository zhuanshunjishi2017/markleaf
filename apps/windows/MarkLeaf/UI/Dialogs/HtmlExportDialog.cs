using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;

namespace MarkLeaf.UI.Dialogs;

internal sealed class HtmlExportDialog : Form
{
    private readonly TextBox _htmlHeader = new()
    { Multiline = true, AcceptsReturn = true };

    private readonly TextBox _htmlFooter = new()
    { Multiline = true, AcceptsReturn = true };

    private readonly ComboBox _htmlStyle = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _htmlColorScheme = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _exportButton = new()
    { Text = Loc.Get("export.ok"), FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    private readonly string _defaultFileName;

    private string? _outputPath;

    private readonly IReadOnlyList<(string Id, string DisplayName)> _styles;

    public HtmlExportDialog(
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
        Size = new Size(this.ScaleForDpi(457), this.ScaleForDpi(340));

        _htmlHeader.PlaceholderText = Loc.Get("export.headerPlaceholder");
        _htmlFooter.PlaceholderText = Loc.Get("export.footerPlaceholder");

        InitCombo(_htmlStyle, StyleDisplayNames(), initialStyleIndex);

        var activeThemeIndex = 0;
        var allThemes = ColorThemeService.All;
        for (var i = 0; i < allThemes.Count; i++)
        {
            _htmlColorScheme.Items.Add(allThemes[i].DisplayName);
            if (string.Equals(allThemes[i].Id, ColorThemeService.ActiveThemeId, StringComparison.Ordinal))
                activeThemeIndex = i;
        }
        _htmlColorScheme.SelectedIndex = activeThemeIndex;

        ApplyDpiSizes();

        _exportButton.Click += OnExportClick;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            BackColor = SystemColors.ControlLightLight,
            Margin = new Padding(this.ScaleForDpi(23), this.ScaleForDpi(11), this.ScaleForDpi(23), 0),
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_exportButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = SystemColors.ControlLightLight,
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(23)),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(BuildContent(), 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        Controls.Add(layout);

        AcceptButton = _exportButton;
        CancelButton = _cancelButton;

        Shown += (_, _) =>
        {
            if (ColorThemeService.IsActiveThemeDark())
            {
                DarkModeService.ApplyDialogDarkMode(this, SystemColors.Control, SystemColors.ControlText);
                DarkModeService.SetWindowDarkTitleBar(this);
            }
        };
    }

    public ExportOptions? Options => _outputPath is null ? null : BuildOptions();

    private ExportOptions BuildOptions()
    {
        var colorThemeId = _htmlColorScheme.SelectedIndex >= 0
            && _htmlColorScheme.SelectedIndex < ColorThemeService.All.Count
                ? ColorThemeService.All[_htmlColorScheme.SelectedIndex].Id
                : ColorThemeService.ActiveThemeId;
        return new ExportOptions(
            Format: "html",
            PaperSize: "A4",
            Landscape: false,
            MarginTop: 25.4f,
            MarginBottom: 25.4f,
            MarginLeft: 31.7f,
            MarginRight: 31.7f,
            HtmlHeader: _htmlHeader.Text,
            HtmlFooter: _htmlFooter.Text,
            Style: MapExportStyle((string)_htmlStyle.SelectedItem!),
            ColorScheme: colorThemeId,
            OutputPath: _outputPath ?? "");
    }

    private Control BuildContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = SystemColors.ControlLightLight,
            Padding = new Padding(
                this.ScaleForDpi(23), this.ScaleForDpi(23), this.ScaleForDpi(23), this.ScaleForDpi(6)),
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
        var htmlW = this.ScaleForDpi(371);
        var htmlH = this.ScaleForDpi(60);
        _htmlHeader.Width = htmlW;
        _htmlHeader.Height = htmlH;
        _htmlFooter.Width = htmlW;
        _htmlFooter.Height = htmlH;

        var comboW = this.ScaleForDpi(120);
        _htmlStyle.Width = comboW;
        _htmlColorScheme.Width = comboW;

        var btnW = this.ScaleForDpi(86);
        var btnH = this.ScaleForDpi(26);
        _exportButton.Width = btnW;
        _exportButton.Height = btnH;
        _cancelButton.Width = btnW;
        _cancelButton.Height = btnH;
    }

    private void OnExportClick(object? sender, EventArgs eventArgs)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = $"{Loc.Get("export.html")}|*.html",
            RestoreDirectory = true,
            FileName = $"{_defaultFileName}.html",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _outputPath = dialog.FileName;
        DialogResult = DialogResult.OK;
        Close();
    }
}
