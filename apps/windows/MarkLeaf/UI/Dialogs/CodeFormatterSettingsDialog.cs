using MarkLeaf.Services;
using MarkLeaf.Services.CodeFormatting;
using MarkLeaf.Services.Settings;
using MarkLeaf.UI.Controls;
using System.Diagnostics;

namespace MarkLeaf.UI.Dialogs;

/// <summary>
/// 外部代码格式化器管理对话框：列出工具与探测状态，支持按工具设置自定义
/// 可执行/jar 路径（立即生效），以及 sqlfluff 使用的 SQL 方言。变更通过
/// onChanged 通知宿主持久化并重新下发 setCodeFormatterSettings。
/// </summary>
internal sealed class CodeFormatterSettingsDialog : Form
{
    private readonly CodeFormatterSettings _settings;
    private readonly Action _onChanged;
    private readonly ListView _toolList = new();
    private readonly TextBox _pathTextBox = new();
    private readonly ComboBox _sqlDialectCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _browseButton = new();
    private readonly Button _clearButton = new();
    private readonly LinkLabel _websiteLink = new();
    private readonly Button _probeButton = new();
    private readonly Button _restoreDefaultsButton = new();
    private readonly Label _statusLabel = new();

    public CodeFormatterSettingsDialog(CodeFormatterSettings settings, Action onChanged)
    {
        _settings = settings;
        _onChanged = onChanged;

        Text = Loc.Get("codeFormatter.dialogTitle");
        BackColor = DialogColors.Secondary;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(this.ScaleForDpi(520), this.ScaleForDpi(430));
        Padding = new Padding(this.ScaleForDpi(9));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            // The tool list owns the flexible height; AutoSize here would let
            // ListView's tiny preferred height collapse it and push all unused
            // vertical space into the path row.
            AutoSize = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (var dialect in SqlDialects)
        {
            _sqlDialectCombo.Items.Add(dialect);
        }

        _sqlDialectCombo.SelectedIndex = Math.Max(0, SqlDialects.IndexOf(_settings.SqlDialect));
        _sqlDialectCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_sqlDialectCombo.SelectedItem is { } selected)
            {
                _settings.SqlDialect = (string)selected;
                _onChanged();
            }
        };

        _toolList.Dock = DockStyle.Fill;
        _toolList.View = View.Details;
        _toolList.FullRowSelect = true;
        _toolList.HideSelection = false;
        _toolList.MultiSelect = false;
        _toolList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _toolList.Columns.Add(Loc.Get("codeFormatter.column.tool"), this.ScaleForDpi(150));
        _toolList.Columns.Add(Loc.Get("codeFormatter.column.languages"), this.ScaleForDpi(210));
        _toolList.Columns.Add(Loc.Get("codeFormatter.column.status"), this.ScaleForDpi(130));
        _toolList.SelectedIndexChanged += (_, _) => OnSelectedToolChanged();
        layout.Controls.Add(_toolList, 0, 0);
        layout.SetColumnSpan(_toolList, 2);

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            AutoSize = true,
            Margin = new Padding(0, this.ScaleForDpi(6), 0, 0),
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pathTextBox.Dock = DockStyle.Fill;
        _pathTextBox.ReadOnly = true;
        _pathTextBox.PlaceholderText = Loc.Get("codeFormatter.pathPlaceholder");
        pathRow.Controls.Add(_pathTextBox, 0, 0);
        _browseButton.Text = Loc.Get("codeFormatter.browse");
        _browseButton.AutoSize = true;
        _browseButton.Anchor = AnchorStyles.Left;
        _browseButton.Click += (_, _) => BrowseToolPath();
        pathRow.Controls.Add(_browseButton, 1, 0);
        _clearButton.Text = Loc.Get("codeFormatter.clearPath");
        _clearButton.AutoSize = true;
        _clearButton.Anchor = AnchorStyles.Left;
        _clearButton.Click += (_, _) =>
        {
            if (SelectedTool() is not { } tool)
            {
                return;
            }

            _settings.ToolPaths.Remove(tool.Id);
            _pathTextBox.Text = string.Empty;
            RefreshToolList();
            _onChanged();
        };
        pathRow.Controls.Add(_clearButton, 2, 0);

        // 与 macOS 一致：未安装或自定义路径无效的工具提供官网链接，可直接跳转获取。
        _websiteLink.Text = Loc.Get("codeFormatter.website");
        _websiteLink.AutoSize = true;
        // LinkLabel auto-sizes to text, which is shorter than the neighboring
        // buttons. Keep its minimum height button-sized so the auto-size row
        // vertically centers text and buttons on the same visual baseline.
        _websiteLink.MinimumSize = new Size(0, this.ScaleForDpi(28));
        _websiteLink.Anchor = AnchorStyles.Left;
        _websiteLink.TextAlign = ContentAlignment.MiddleLeft;
        _websiteLink.LinkBehavior = LinkBehavior.HoverUnderline;
        _websiteLink.Padding = new Padding(this.ScaleForDpi(8), 0, 0, 0);
        _websiteLink.Visible = false;
        _websiteLink.Click += (_, _) => OpenSelectedToolWebsite();
        pathRow.Controls.Add(_websiteLink, 3, 0);

        _probeButton.Text = Loc.Get("codeFormatter.probe");
        _probeButton.AutoSize = true;
        _probeButton.Anchor = AnchorStyles.Left;
        _probeButton.Click += async (_, _) => await ProbeSelectedToolAsync();
        pathRow.Controls.Add(_probeButton, 4, 0);

        _restoreDefaultsButton.Text = Loc.Get("codeFormatter.restoreDefaults");
        _restoreDefaultsButton.AutoSize = true;
        _restoreDefaultsButton.Anchor = AnchorStyles.Left;
        _restoreDefaultsButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    Loc.Get("codeFormatter.restoreDefaultsConfirm"),
                    Loc.Get("codeFormatter.restoreDefaultsTitle"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            RestoreDefaults();
        };
        pathRow.Controls.Add(_restoreDefaultsButton, 6, 0);

        _sqlDialectCombo.Anchor = AnchorStyles.Left;
        _sqlDialectCombo.Width = this.ScaleForDpi(128);
        _sqlDialectCombo.AccessibleName = Loc.Get("codeFormatter.sqlDialect");
        _sqlDialectCombo.Visible = false;
        pathRow.Controls.Add(_sqlDialectCombo, 5, 0);
        layout.Controls.Add(pathRow, 0, 1);
        layout.SetColumnSpan(pathRow, 2);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = SystemColors.GrayText;
        _statusLabel.Text = Loc.Get("codeFormatter.hint");
        _statusLabel.Margin = new Padding(0, this.ScaleForDpi(6), 0, 0);
        layout.Controls.Add(_statusLabel, 0, 2);
        layout.SetColumnSpan(_statusLabel, 2);

        Controls.Add(layout);

        RefreshToolList();
    }

    private static readonly List<string> SqlDialects =
    [
        "ansi", "bigquery", "clickhouse", "duckdb", "hive", "materialize", "mysql",
        "oracle", "postgres", "redshift", "snowflake", "sparksql", "sqlite", "trino", "tsql",
    ];

    private ExternalCodeFormatterTool? SelectedTool()
    {
        return _toolList.SelectedItems.Count == 0
            ? null
            : ExternalCodeFormatterCatalog.Tools.FirstOrDefault(tool => tool.Id == (string)_toolList.SelectedItems[0].Tag!);
    }

    internal void RestoreDefaults()
    {
        CodeFormatterSettingsReset.Apply(_settings, AppSettings.CreateDefaults().CodeFormatter);
        _sqlDialectCombo.SelectedIndex = Math.Max(0, SqlDialects.IndexOf(_settings.SqlDialect));
        RefreshToolList();
        OnSelectedToolChanged();
        _onChanged();
    }

    private void RefreshToolList()
    {
        var selectedId = SelectedTool()?.Id;
        _toolList.BeginUpdate();
        _toolList.Items.Clear();
        foreach (var tool in ExternalCodeFormatterCatalog.Tools)
        {
            var custom = _settings.ToolPaths.TryGetValue(tool.Id, out var path) && !string.IsNullOrWhiteSpace(path);
            var availability = ExternalCodeFormatterCatalog.ProbeAvailability(tool, _settings.ToolPaths);
            var status = availability switch
            {
                ExternalCodeFormatterAvailability.Available when custom => Loc.Get("codeFormatter.status.customAvailable"),
                ExternalCodeFormatterAvailability.Available => Loc.Get("codeFormatter.status.available"),
                _ => Loc.Get("codeFormatter.status.notInstalled"),
            };
            var item = new ListViewItem(tool.DisplayName)
            {
                Tag = tool.Id,
            };
            item.SubItems.Add(string.Join(", ", tool.Languages));
            item.SubItems.Add(status);
            _toolList.Items.Add(item);
            if (tool.Id == selectedId)
            {
                item.Selected = true;
            }
        }

        _toolList.EndUpdate();
    }

    private void OnSelectedToolChanged()
    {
        // 探测结果是针对上一个选中工具的一次性反馈，切换选择时还原为默认提示。
        _statusLabel.Text = Loc.Get("codeFormatter.hint");
        if (SelectedTool() is not { } tool)
        {
            _pathTextBox.Text = string.Empty;
            _browseButton.Enabled = _clearButton.Enabled = false;
            _websiteLink.Visible = false;
            _sqlDialectCombo.Visible = false;
            return;
        }

        _browseButton.Enabled = true;
        _clearButton.Enabled = _settings.ToolPaths.ContainsKey(tool.Id);
        // 与 macOS 一致：地址栏展示实际生效的可执行文件位置——设置了自定义路径
        // 用自定义路径，否则显示探测解析到的安装位置；未安装保持占位提示。
        _pathTextBox.Text = _settings.ToolPaths.TryGetValue(tool.Id, out var path)
            && !string.IsNullOrWhiteSpace(path)
            ? path
            : ExternalCodeFormatterCatalog.ResolveExecutable(tool, _settings.ToolPaths)
                ?? string.Empty;
        _websiteLink.Visible = ExternalCodeFormatterCatalog.ProbeAvailability(tool, _settings.ToolPaths)
            != ExternalCodeFormatterAvailability.Available;
        // 没解析到可执行文件的工具没有可探测的对象：隐藏按钮而不是置灰，
        // 引导用户走"浏览"指定路径或官网链接获取。
        _probeButton.Visible = ExternalCodeFormatterCatalog.ResolveExecutable(tool, _settings.ToolPaths) is not null;
        _sqlDialectCombo.Visible = tool.Languages.Contains("sql", StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>仅探测当前选中的格式化器，验证版本命令可运行。</summary>
    private async Task ProbeSelectedToolAsync()
    {
        if (SelectedTool() is not { } tool)
        {
            return;
        }

        var path = ExternalCodeFormatterCatalog.ResolveExecutable(tool, _settings.ToolPaths);
        if (path is null)
        {
            _statusLabel.Text = Loc.Get("codeFormatter.status.notInstalled");
            return;
        }

        _probeButton.Enabled = false;
        _statusLabel.Text = Loc.Get("codeFormatter.probing");
        var result = await Task.Run(() => ExternalCodeFormatterService.Probe(tool, path));
        _statusLabel.Text = result.Success
            ? Loc.Format("codeFormatter.probeSucceeded", result.Detail)
            : Loc.Format("codeFormatter.probeFailed", result.Detail);
        _probeButton.Enabled = true;
    }

    /// <summary>打开当前选中工具的官网（未安装/路径无效时链接可见），与 macOS 契约一致。</summary>
    private void OpenSelectedToolWebsite()
    {
        if (SelectedTool() is not { } tool)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = tool.HomepageUrl, UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开系统浏览器时至少把地址显示出来，便于手动复制。
            _statusLabel.Text = tool.HomepageUrl;
        }
    }

    private void BrowseToolPath()
    {
        if (SelectedTool() is not { } tool)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = Loc.Format("codeFormatter.browseTitle", tool.DisplayName),
            CheckFileExists = true,
            RestoreDirectory = true,
            Filter = tool.Id == "google-java-format"
                ? $"{tool.DisplayName} (*.jar)|*.jar|{Loc.Get("codeFormatter.filterExecutable")} (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|{Loc.Get("codeFormatter.filterAll")} (*.*)|*.*"
                : $"{Loc.Get("codeFormatter.filterExecutable")} (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|{Loc.Get("codeFormatter.filterAll")} (*.*)|*.*",
        };
        if (ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.ToolPaths[tool.Id] = dialog.FileName;
        _pathTextBox.Text = dialog.FileName;
        RefreshToolList();
        _onChanged();
    }
}
