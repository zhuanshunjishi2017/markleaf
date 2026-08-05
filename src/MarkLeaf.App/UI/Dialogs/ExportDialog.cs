namespace MarkLeaf.UI.Dialogs;

internal sealed class ExportDialog : Form
{
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private readonly ComboBox _pageSize = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };

    private readonly RadioButton _portrait = new()
    { Text = "纵向(&P)", AutoSize = true, Padding = new Padding(0, 0, 24, 0), FlatStyle = FlatStyle.System };

    private readonly RadioButton _landscape = new()
    { Text = "横向(&L)", AutoSize = true, FlatStyle = FlatStyle.System };

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

    private readonly Button _exportButton = new()
    { Text = "导出", Width = 150, Height = 45, FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = "取消", Width = 150, Height = 45, FlatStyle = FlatStyle.System };

    private readonly string _defaultFileName;

    private string? _outputPath;

    private static readonly (string Label, float Top, float Bottom, float Left, float Right)[] MarginPresets =
    [
        ("普通", 25.4f, 25.4f, 31.7f, 31.7f),
        ("窄", 12.7f, 12.7f, 12.7f, 12.7f),
        ("宽", 50.8f, 50.8f, 50.8f, 50.8f),
        ("自定义", 16f, 16f, 16f, 16f),
    ];

    public ExportDialog(string documentFileName, string defaultFileName, string currentStyle)
    {
        _defaultFileName = defaultFileName;
        var initialStyleIndex = currentStyle switch
        {
            "sans" => 1,
            "print" => 2,
            "retro-print" => 3,
            _ => 0,
        };
        Text = $"导出 - {documentFileName}";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(800, 700);

        BuildPdfTab(initialStyleIndex);
        BuildHtmlTab(initialStyleIndex);
        _tabs.TabPages.Add(CreateTab("PDF", BuildPdfContent()));
        _tabs.TabPages.Add(CreateTab("HTML", BuildHtmlContent()));

        _exportButton.Click += OnExportClick;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = 45,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 16, 0, 5),
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_exportButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _tabs.Dock = DockStyle.Fill;
        layout.Controls.Add(_tabs, 0, 0);
        layout.Controls.Add(buttons, 0, 1);

        Controls.Add(layout);

        AcceptButton = _exportButton;
        CancelButton = _cancelButton;
    }

    public ExportOptions? Options
    {
        get
        {
            if (_outputPath is null) return null;
            var isPdf = _tabs.SelectedIndex == 0;
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
                OutputPath: _outputPath);
        }
    }

    private void OnExportClick(object? sender, EventArgs eventArgs)
    {
        var isPdf = _tabs.SelectedIndex == 0;
        var extension = isPdf ? "pdf" : "html";
        var filter = isPdf ? "PDF 文件|*.pdf" : "HTML 文件|*.html";

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

        InitCombo(_pdfStyle, ["默认(衬线字体)", "默认(无衬线字体)", "印刷物(现代)", "印刷物(复古)"], styleIndex);
    }

    private Control BuildPdfContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16, 12, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++)
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(BuildPdfMiscSection(), 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(BuildMarginSection(), 0, 2);
        panel.Controls.Add(Gap(), 0, 3);

        var styleRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        styleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        styleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(styleRow, 0, "排版样式(&Y)：", _pdfStyle);
        panel.Controls.Add(styleRow, 0, 4);

        return panel;
    }

    private Control BuildPdfMiscSection()
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, 0, "纸张大小(&S)：", _pageSize);

        var ori = new FlowLayoutPanel { AutoSize = true };
        ori.Controls.Add(_portrait);
        ori.Controls.Add(_landscape);
        AddRow(grid, 1, "方向：", ori);

        return grid;
    }

    private Control BuildMarginSection()
    {
        var container = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(container, 0, "页边距(&M)：", _marginPreset);

        var tbRow = new FlowLayoutPanel { AutoSize = true };
        tbRow.Controls.Add(new Label { Text = "上(&T)：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        tbRow.Controls.Add(_marginTop);
        tbRow.Controls.Add(new Label { Text = "  下(&B)：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        tbRow.Controls.Add(_marginBottom);

        var lrRow = new FlowLayoutPanel { AutoSize = true };
        lrRow.Controls.Add(new Label { Text = "左(&L)：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        lrRow.Controls.Add(_marginLeft);
        lrRow.Controls.Add(new Label { Text = "  右(&R)：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        lrRow.Controls.Add(_marginRight);

        var mg = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        mg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mg.Controls.Add(tbRow, 0, 0);
        mg.Controls.Add(lrRow, 0, 1);

        container.Controls.Add(mg, 1, 1);
        return container;
    }

    private void BuildHtmlTab(int styleIndex)
    {
        _htmlHeader.PlaceholderText = "例如：<header>...</header>";
        _htmlFooter.PlaceholderText = "例如：<footer>...</footer>";

        InitCombo(_htmlStyle, ["默认(衬线字体)", "默认(无衬线字体)", "印刷物(现代)", "印刷物(复古)"], styleIndex);
    }

    private Control BuildHtmlContent()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(16, 12, 0, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 6; i++)
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(LabeledControl("页头(&H)：", _htmlHeader), 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(LabeledControl("页脚(&F)：", _htmlFooter), 0, 2);
        panel.Controls.Add(Gap(), 0, 3);

        var bottomGrid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        bottomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(bottomGrid, 0, "排版样式(&Y)：", _htmlStyle);
        panel.Controls.Add(bottomGrid, 0, 4);

        return panel;
    }

    private static string MapExportStyle(string label) => label switch
    {
        "默认(无衬线字体)" => "sans",
        "印刷物(现代)" => "print",
        "印刷物(复古)" => "retro-print",
        _ => "serif",
    };

    private static TabPage CreateTab(string text, Control content)
    {
        var page = new TabPage(text) { UseVisualStyleBackColor = true, Padding = new Padding(8), AutoScroll = true };
        page.Controls.Add(content);
        return page;
    }

    private static Control Gap() => new Panel { Height = 25, Dock = DockStyle.None };

    private static Control LabeledControl(string label, Control control)
    {
        var g = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(g, 0, label, control);
        return g;
    }

    private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        var lbl = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
        table.Controls.Add(lbl, 0, row);
        table.Controls.Add(control, 1, row);
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
    string OutputPath);
