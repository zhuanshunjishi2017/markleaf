using MarkLeaf.Documents;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;

namespace MarkLeaf.UI.Dialogs;

internal sealed class PreferencesDialog : Form
{
    private readonly AppSettings _settings;
    private readonly Action? _onRecover;
    private readonly Action? _onShowShortcuts;
    private readonly Action? _onOpenThemeFolder;
    private readonly Action? _onOpenCacheFolder;
    private readonly Action? _onOpenLogFolder;
    private readonly Action? _onClearLogs;
    private readonly Action? _onOpenSettingsJson;
    private readonly Action? _onClearHistory;
    private readonly Action? _onResetAll;

    private readonly Button _resetAllButton = new()
    { Text = "重置所有设置(&R)...", AutoSize = true, FlatStyle = FlatStyle.System };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private readonly ComboBox _startupAction = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };

    private readonly CheckBox _autoSaveCheck;
    private readonly CheckBox _saveOnSwitchCheck;
    private readonly NumericUpDown _snapshotInterval;
    private readonly Button _recoverButton;

    private readonly CheckBox _recordRecentFilesCheck;
    private readonly CheckBox _recordRecentFoldersCheck;
    private readonly Button _clearHistoryButton = new()
    { Text = "清除历史记录(&C)", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly ComboBox _newLineStyleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };

    private readonly Button _editShortcutsButton;

    private readonly NumericUpDown _visualLineHeight;
    private readonly NumericUpDown _visualFontSize;
    private readonly NumericUpDown _visualMaxWidth;

    private readonly NumericUpDown _sourceFontSize;
    private readonly NumericUpDown _sourceIndentWidth;

    private readonly (string Id, string DisplayName)[] _styleOptions;
    private readonly ComboBox _styleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly (string Id, string DisplayName)[] _themeOptions;
    private readonly ComboBox _themeCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly Button _openThemeFolderButton = new()
    { Text = "打开主题文件夹(&O)...", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly ComboBox _zoomCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly Button _zoomResetButton = new()
    { Text = "重置(&R)", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _restoreZoomCheck;
    private readonly CheckBox _ctrlWheelZoomCheck;
    private readonly CheckBox _topMostCheck;
    private readonly CheckBox _autoHideScrollbarsCheck;

    private readonly ComboBox _languageCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly Button _openCacheFolderButton = new()
    { Text = "打开缓存目录(&C)...", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _openLogFolderButton = new()
    { Text = "打开日志目录(&O)...", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _clearLogsButton = new()
    { Text = "清除日志(&E)", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _openSettingsJsonButton = new()
    { Text = "配置 JSON 文件(&J)...", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _associateMarkdownCheck;
    private readonly CheckBox _associateTextCheck;

    private readonly ComboBox _clipboardImageCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly ComboBox _fileImageCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly TextBox _defaultDirectoryTextBox = new()
    { Width = 320 };
    private readonly Button _browseDirectoryButton = new()
    { Text = "浏览(&B)...", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _useRelativePathsCheck;
    private readonly CheckBox _prefixRelativeWithDotSlashCheck;
    private readonly Button _imageUploadButton = new()
    { Text = "图片上传配置(&U)...", AutoSize = true, FlatStyle = FlatStyle.System };

    private readonly Button _okButton = new()
    { Text = "确定", Width = 150, Height = 45, FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = "取消", Width = 150, Height = 45, FlatStyle = FlatStyle.System };

    private static readonly string[] StartupActionItems =
    [
        "新建文件",
        "打开上次的工作区",
        "打开上次的工作区和文件",
    ];

    // “上传图片”选项暂未实现，从列表中移除，等后续版本再开放。
    private static readonly string[] ClipboardImageHandlingItems =
    [
        "保存到默认目录",
        "复制到./文件名.assets路径",
    ];

    private static readonly string[] FileImageHandlingItems =
    [
        "引用原有位置",
        "复制到./文件名.assets路径",
    ];

    public PreferencesDialog(
        AppSettings settings,
        Action? onRecover = null,
        Action? onShowShortcuts = null,
        Action? onOpenThemeFolder = null,
        Action? onOpenCacheFolder = null,
        Action? onOpenLogFolder = null,
        Action? onClearLogs = null,
        Action? onOpenSettingsJson = null,
        Action? onClearHistory = null,
        Action? onResetAll = null)
    {
        _settings = settings;
        _onRecover = onRecover;
        _onShowShortcuts = onShowShortcuts;
        _onOpenThemeFolder = onOpenThemeFolder;
        _onOpenCacheFolder = onOpenCacheFolder;
        _onOpenLogFolder = onOpenLogFolder;
        _onClearLogs = onClearLogs;
        _onOpenSettingsJson = onOpenSettingsJson;
        _onClearHistory = onClearHistory;
        _onResetAll = onResetAll;

        _languageCombo.Items.Add("简体中文");
        _languageCombo.SelectedIndex = 0;

        _styleOptions = StyleService.GetAllStyles().ToArray();
        foreach (var (_, displayName) in _styleOptions)
            _styleCombo.Items.Add(displayName);
        _themeOptions = ColorThemeService.All
            .Select(t => (t.Id, t.DisplayName)).ToArray();
        foreach (var (_, displayName) in _themeOptions)
            _themeCombo.Items.Add(displayName);
        foreach (var percent in AppearanceSettings.ZoomPercentOptions)
            _zoomCombo.Items.Add($"{percent}%");

        foreach (var item in StartupActionItems)
            _startupAction.Items.Add(item);

        foreach (var item in ClipboardImageHandlingItems)
            _clipboardImageCombo.Items.Add(item);
        foreach (var item in FileImageHandlingItems)
            _fileImageCombo.Items.Add(item);

        // 相对路径引用暂未实现，先禁用，等后续版本再开放。
        _useRelativePathsCheck = new CheckBox
        { Text = "在可用时使用相对路径(&R)", AutoSize = true, FlatStyle = FlatStyle.System, Enabled = false };
        _prefixRelativeWithDotSlashCheck = new CheckBox
        { Text = "相对路径前加\"./\"(&S)", AutoSize = true, FlatStyle = FlatStyle.System, Enabled = false };

        _autoSaveCheck = new CheckBox
        { Text = "自动保存文件(&A)", AutoSize = true, FlatStyle = FlatStyle.System };
        _saveOnSwitchCheck = new CheckBox
        { Text = "切换文档时保存上个文档的修改(&S)", AutoSize = true, FlatStyle = FlatStyle.System };

        _snapshotInterval = new NumericUpDown
        { Minimum = 10, Maximum = 300, Increment = 5, Width = 70 };
        _recoverButton = new Button
        { Text = "恢复未保存的文档(&R)...", AutoSize = true, FlatStyle = FlatStyle.System };

        _recordRecentFilesCheck = new CheckBox
        { Text = "文件(&F)", AutoSize = true, FlatStyle = FlatStyle.System };
        _recordRecentFoldersCheck = new CheckBox
        { Text = "文件夹(&D)", AutoSize = true, FlatStyle = FlatStyle.System };
        _newLineStyleCombo.Items.Add("LF");
        _newLineStyleCombo.Items.Add("CRLF");

        _visualLineHeight = new NumericUpDown
        { Minimum = 1.0m, Maximum = 3.0m, Increment = 0.05m, DecimalPlaces = 2, Width = 70 };
        _visualFontSize = new NumericUpDown
        { Minimum = 12, Maximum = 24, Increment = 1, Width = 70 };
        _visualMaxWidth = new NumericUpDown
        { Minimum = 600, Maximum = 1200, Increment = 20, Width = 70 };

        _sourceFontSize = new NumericUpDown
        { Minimum = 12, Maximum = 24, Increment = 1, Width = 70 };
        _sourceIndentWidth = new NumericUpDown
        { Minimum = 2, Maximum = 8, Increment = 2, Width = 70 };

        _editShortcutsButton = new Button
        { Text = "编辑快捷键(&K)...", AutoSize = true, FlatStyle = FlatStyle.System };

        _restoreZoomCheck = new CheckBox
        { Text = "打开时还原上次的缩放比例(&O)", AutoSize = true, FlatStyle = FlatStyle.System };
        _ctrlWheelZoomCheck = new CheckBox
        { Text = "按住Ctrl时滚动鼠标滚轮以缩放(&W)", AutoSize = true, FlatStyle = FlatStyle.System };
        _topMostCheck = new CheckBox
        { Text = "将窗口置于顶层(&T)", AutoSize = true, FlatStyle = FlatStyle.System };
        _autoHideScrollbarsCheck = new CheckBox
        { Text = "自动隐藏滚动条(&A)", AutoSize = true, FlatStyle = FlatStyle.System };

        _associateMarkdownCheck = new CheckBox
        { Text = "Markdown文件(&M)(.md/.markdown)", AutoSize = true, FlatStyle = FlatStyle.System };
        _associateTextCheck = new CheckBox
        { Text = "纯文本文件(&T)(.txt)", AutoSize = true, FlatStyle = FlatStyle.System };

        LoadSettingsIntoControls();

        Text = "首选项";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(780, 860);

        _tabs.TabPages.Add(CreateTab("文件", BuildFileTab()));
        _tabs.TabPages.Add(CreateTab("外观", BuildAppearanceTab()));
        _tabs.TabPages.Add(CreateTab("编辑", BuildEditorTab()));
        _tabs.TabPages.Add(CreateTab("图片", BuildImagesTab()));
        _tabs.TabPages.Add(CreateTab("通用", BuildGeneralTab()));

        _okButton.Click += OnOkClick;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _recoverButton.Click += (_, _) => _onRecover?.Invoke();
        _editShortcutsButton.Click += (_, _) => _onShowShortcuts?.Invoke();
        _openThemeFolderButton.Click += (_, _) => _onOpenThemeFolder?.Invoke();
        _zoomResetButton.Click += (_, _) => _zoomCombo.SelectedIndex = FindZoomIndex(100);
        _openCacheFolderButton.Click += (_, _) => _onOpenCacheFolder?.Invoke();
        _openLogFolderButton.Click += (_, _) => _onOpenLogFolder?.Invoke();
        _clearLogsButton.Click += (_, _) => _onClearLogs?.Invoke();
        _openSettingsJsonButton.Click += (_, _) => _onOpenSettingsJson?.Invoke();
        _clearHistoryButton.Click += (_, _) => _onClearHistory?.Invoke();
        _resetAllButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    "这将把首选项里的所有设置都重置为默认值。是否继续？",
                    "重置所有设置",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            var defaults = AppSettings.CreateDefaults();
            _settings.SchemaVersion = defaults.SchemaVersion;
            _settings.MainWindow = defaults.MainWindow;
            _settings.Workspace = defaults.Workspace;
            _settings.File = defaults.File;
            _settings.Editor = defaults.Editor;
            _settings.Appearance = defaults.Appearance;
            _settings.General = defaults.General;
            _settings.Image = defaults.Image;
            _settings.MarkdownStyle = defaults.MarkdownStyle;
            _settings.ColorTheme = defaults.ColorTheme;
            LoadSettingsIntoControls();
            _onResetAll?.Invoke();
        };
        _browseDirectoryButton.Click += (_, _) => BrowseDefaultDirectory();
        _imageUploadButton.Click += (_, _) => MessageBox.Show(
            this,
            "图片上传配置功能将在后续版本中提供。",
            "MarkLeaf",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

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
        buttons.Controls.Add(_okButton);

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

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private Control BuildFileTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel("启动操作(&O)："), 0, 0);
        panel.Controls.Add(_startupAction, 1, 0);

        panel.Controls.Add(Gap(20), 0, 1);
        panel.Controls.Add(Gap(20), 1, 1);


        panel.Controls.Add(NewLabel("保存选项(&S)："), 0, 2);
        panel.Controls.Add(BuildSaveOptionsPanel(), 1, 2);

        panel.Controls.Add(Gap(20), 0, 3);
        panel.Controls.Add(Gap(20), 1, 3);

        panel.Controls.Add(NewLabel("换行风格(&N)："), 0, 4);
        panel.Controls.Add(BuildNewLinePanel(), 1, 4);

        panel.Controls.Add(Gap(20), 0, 5);
        panel.Controls.Add(Gap(20), 1, 5);

        panel.Controls.Add(NewLabel("历史记录(&H)："), 0, 6);
        panel.Controls.Add(BuildHistoryPanel(), 1, 6);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 7);

        return panel;
    }

    private Control BuildHistoryPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var checks = new FlowLayoutPanel { AutoSize = true };
        checks.Controls.Add(_recordRecentFilesCheck);
        checks.Controls.Add(_recordRecentFoldersCheck);
        panel.Controls.Add(checks, 0, 0);
        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(_clearHistoryButton, 0, 2);
        return panel;
    }

    private Control BuildNewLinePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_newLineStyleCombo, 0, 0);
        panel.Controls.Add(Gap(6), 0, 1);
        panel.Controls.Add(new Label
        {
            Text = "此设置项仅控制新建文件的换行符，打开的文件将保留其原有换行风格。",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            ForeColor = SystemColors.GrayText,
        }, 0, 2);
        return panel;
    }

    private Control BuildSaveOptionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(_autoSaveCheck, 0, 0);
        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(_saveOnSwitchCheck, 0, 2);
        panel.Controls.Add(Gap(10), 0, 3);

        var intervalRow = new FlowLayoutPanel { AutoSize = true };
        intervalRow.Controls.Add(new Label { Text = "快照保存间隔(&I)：", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        intervalRow.Controls.Add(_snapshotInterval);
        intervalRow.Controls.Add(new Label { Text = " 秒", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        panel.Controls.Add(intervalRow, 0, 4);

        panel.Controls.Add(Gap(10), 0, 5);
        panel.Controls.Add(_recoverButton, 0, 6);

        return panel;
    }

    private Control BuildEditorTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel("可视化："), 0, 0);
        panel.Controls.Add(BuildVisualPanel(), 1, 0);

        panel.Controls.Add(Gap(20), 0, 1);
        panel.Controls.Add(Gap(20), 1, 1);

        panel.Controls.Add(NewLabel("源码模式："), 0, 2);
        panel.Controls.Add(BuildSourcePanel(), 1, 2);

        panel.Controls.Add(Gap(20), 0, 3);
        panel.Controls.Add(Gap(20), 1, 3);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 4);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 4);

        var noteLabel = new Label
        {
            Text = "某些设置可能由当前的排版样式接管，转到“外观”以更改。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
        };
        panel.Controls.Add(noteLabel, 0, 5);
        panel.SetColumnSpan(noteLabel, 2);

        return panel;
    }

    private Control BuildVisualPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(NewLabel("基础行高(&H)："), 0, 0);
        panel.Controls.Add(_visualLineHeight, 1, 0);
        panel.Controls.Add(Gap(10), 0, 1);


        panel.Controls.Add(NewLabel("基础字号(&F)："), 0, 2);
        panel.Controls.Add(_visualFontSize, 1, 2);
        panel.Controls.Add(Gap(10), 0, 3);


        panel.Controls.Add(NewLabel("最大内容宽度(&W)："), 0, 4);
        var widthRow = new FlowLayoutPanel { AutoSize = true };
        widthRow.Controls.Add(_visualMaxWidth);
        widthRow.Controls.Add(new Label { Text = " px", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        panel.Controls.Add(widthRow, 1, 4);

        return panel;
    }

    private Control BuildSourcePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        panel.Controls.Add(NewLabel("基础字号(&F)："), 0, 0);
        panel.Controls.Add(_sourceFontSize, 1, 0);
        panel.Controls.Add(Gap(10), 0, 1);


        panel.Controls.Add(NewLabel("默认缩进宽度(&I)："), 0, 2);
        panel.Controls.Add(_sourceIndentWidth, 1, 2);

        return panel;
    }

    private Control BuildAppearanceTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));


        panel.Controls.Add(NewLabel("排版样式(&Y)："), 0, 0);
        panel.Controls.Add(_styleCombo, 1, 0);
        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(Gap(10), 1, 1);

        panel.Controls.Add(NewLabel("颜色主题(&C)："), 0, 2);
        panel.Controls.Add(_themeCombo, 1, 2);
        panel.Controls.Add(Gap(10), 0, 3);
        panel.Controls.Add(Gap(10), 1, 3);

        panel.Controls.Add(_openThemeFolderButton, 1, 4);
        panel.Controls.Add(Gap(10), 0, 4);


        panel.Controls.Add(Gap(20), 0, 5);
        panel.Controls.Add(Gap(20), 1, 5);

        panel.Controls.Add(NewLabel("缩放视图(&S)："), 0, 6);
        panel.Controls.Add(BuildZoomPanel(), 1, 6);

        panel.Controls.Add(Gap(20), 0, 7);
        panel.Controls.Add(Gap(20), 1, 7);

        panel.Controls.Add(NewLabel("窗口设置(&W)："), 0, 8);
        panel.Controls.Add(BuildWindowPanel(), 1, 8);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 9);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 9);

        return panel;
    }


    private Control BuildZoomPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var zoomRow = new FlowLayoutPanel { AutoSize = true };
        //zoomRow.Controls.Add(NewLabel("设置缩放(&Z)："));
        zoomRow.Controls.Add(_zoomCombo);
        panel.Controls.Add(zoomRow, 0, 0);

        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(_zoomResetButton, 0, 2);
        panel.Controls.Add(Gap(10), 0, 3);
        panel.Controls.Add(_restoreZoomCheck, 0, 4);
        panel.Controls.Add(Gap(10), 0, 5);
        panel.Controls.Add(_ctrlWheelZoomCheck, 0, 6);

        return panel;
    }

    private Control BuildWindowPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_topMostCheck, 0, 0);
        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(_autoHideScrollbarsCheck, 0, 2);
        return panel;
    }

    private Control BuildGeneralTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel("显示语言："), 0, 0);
        panel.Controls.Add(_languageCombo, 1, 0);

        panel.Controls.Add(Gap(20), 0, 1);
        panel.Controls.Add(Gap(20), 1, 1);

        panel.Controls.Add(NewLabel("快捷键："), 0, 2);
        panel.Controls.Add(_editShortcutsButton, 1, 2);

        panel.Controls.Add(Gap(20), 0, 3);
        panel.Controls.Add(Gap(20), 1, 3);

        panel.Controls.Add(NewLabel("文件关联(&F)："), 0, 4);
        panel.Controls.Add(BuildFileAssociationPanel(), 1, 4);

        panel.Controls.Add(Gap(20), 0, 5);
        panel.Controls.Add(Gap(20), 1, 5);

        panel.Controls.Add(NewLabel("储存管理："), 0, 6);
        panel.Controls.Add(BuildStoragePanel(), 1, 6);

        panel.Controls.Add(Gap(20), 0, 7);
        panel.Controls.Add(Gap(20), 1, 7);

        panel.Controls.Add(NewLabel("日志管理："), 0, 8);
        panel.Controls.Add(BuildLogsPanel(), 1, 8);

        panel.Controls.Add(Gap(20), 0, 9);
        panel.Controls.Add(Gap(20), 1, 9);

        panel.Controls.Add(NewLabel("高级："), 0, 10);
        panel.Controls.Add(BuildAdvancedPanel(), 1, 10);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 11);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 11);

        return panel;
    }

    private Control BuildStoragePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_openCacheFolderButton, 0, 0);
        return panel;
    }

    private Control BuildLogsPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var row = new FlowLayoutPanel { AutoSize = true };
        row.Controls.Add(_openLogFolderButton);
        row.Controls.Add(_clearLogsButton);
        panel.Controls.Add(row, 0, 0);
        return panel;
    }

    private Control BuildAdvancedPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_openSettingsJsonButton, 0, 0);
        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(_resetAllButton, 0, 2);
        return panel;
    }

    private Control BuildFileAssociationPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_associateMarkdownCheck, 0, 0);
        panel.Controls.Add(Gap(10), 0, 1);
        panel.Controls.Add(_associateTextCheck, 0, 2);
        return panel;
    }

    private Control BuildImagesTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(16, 20, 16, 12),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel("剪切板图片(&C)："), 0, 0);
        panel.Controls.Add(_clipboardImageCombo, 1, 0);
        panel.Controls.Add(Gap(20), 0, 1);
        panel.Controls.Add(Gap(20), 1, 1);

        panel.Controls.Add(NewLabel("来自文件(&F)："), 0, 2);
        panel.Controls.Add(_fileImageCombo, 1, 2);
        panel.Controls.Add(Gap(20), 0, 3);
        panel.Controls.Add(Gap(20), 1, 3);

        panel.Controls.Add(NewLabel("默认目录(&D)："), 0, 4);
        panel.Controls.Add(BuildDefaultDirectoryPanel(), 1, 4);
        panel.Controls.Add(Gap(20), 0, 5);
        panel.Controls.Add(Gap(20), 1, 5);

        panel.Controls.Add(NewLabel("引用方式(&R)："), 0, 6);
        panel.Controls.Add(BuildReferencePanel(), 1, 6);
        panel.Controls.Add(Gap(20), 0, 7);
        panel.Controls.Add(Gap(20), 1, 7);

        panel.Controls.Add(NewLabel("图片上传(&U)："), 0, 8);
        panel.Controls.Add(_imageUploadButton, 1, 8);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 9);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 9);

        return panel;
    }

    private Control BuildDefaultDirectoryPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_defaultDirectoryTextBox, 0, 0);
        panel.Controls.Add(Gap(6), 0, 1);
        panel.Controls.Add(_browseDirectoryButton, 0, 2);
        return panel;
    }

    private Control BuildReferencePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_useRelativePathsCheck, 0, 0);
        panel.Controls.Add(Gap(6), 0, 1);
        panel.Controls.Add(_prefixRelativeWithDotSlashCheck, 0, 2);
        return panel;
    }

    private void BrowseDefaultDirectory()
    {
        var current = _defaultDirectoryTextBox.Text.Trim();
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择默认图片目录",
            UseDescriptionForTitle = true,
            SelectedPath = current.Length > 0 && Directory.Exists(current) ? current : string.Empty,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var selected = dialog.SelectedPath;
        if (string.Equals(selected, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 更换目录后，原本保存在当前默认目录、并被文档引用的图片将不再由应用管理。
        if (current.Length > 0 && Directory.Exists(current) && ImageAssetService.DirectoryContainsImages(current))
        {
            var choice = MessageBox.Show(
                this,
                "若更换目录，所有链接到此处的图片将失效。是否继续？",
                "更换默认图片目录",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (choice != DialogResult.Yes)
            {
                return;
            }
        }

        _defaultDirectoryTextBox.Text = selected;
    }

    private void LoadSettingsIntoControls()
    {
        var file = _settings.File;
        _startupAction.SelectedIndex = (int)file.StartupAction;
        _autoSaveCheck.Checked = file.AutoSaveEnabled;
        _saveOnSwitchCheck.Checked = file.SaveOnDocumentSwitch;
        _snapshotInterval.Value = file.SnapshotIntervalSeconds;
        _recordRecentFilesCheck.Checked = file.RecordRecentFiles;
        _recordRecentFoldersCheck.Checked = file.RecordRecentFolders;
        _newLineStyleCombo.SelectedIndex = file.NewLineStyle == NewLineStyle.Lf ? 0 : 1;

        var editor = _settings.Editor;
        _visualLineHeight.Value = (decimal)editor.VisualLineHeight;
        _visualFontSize.Value = editor.VisualFontSize;
        _visualMaxWidth.Value = editor.VisualMaxContentWidth;
        _sourceFontSize.Value = editor.SourceFontSize;
        _sourceIndentWidth.Value = editor.SourceIndentWidth;

        var appearance = _settings.Appearance;
        _zoomCombo.SelectedIndex = FindZoomIndex(appearance.ZoomPercent);
        _restoreZoomCheck.Checked = appearance.RestoreZoomOnOpen;
        _ctrlWheelZoomCheck.Checked = appearance.CtrlWheelZoom;
        _topMostCheck.Checked = appearance.TopMostWindow;
        _autoHideScrollbarsCheck.Checked = appearance.AutoHideScrollbars;

        _associateMarkdownCheck.Checked = _settings.General.AssociateMarkdownFiles;
        _associateTextCheck.Checked = _settings.General.AssociateTextFiles;

        var image = _settings.Image;
        _clipboardImageCombo.SelectedIndex = ToComboIndex((int)image.ClipboardHandling, _clipboardImageCombo);
        _fileImageCombo.SelectedIndex = ToComboIndex((int)image.FileHandling, _fileImageCombo);
        _defaultDirectoryTextBox.Text = image.DefaultDirectory;
        _useRelativePathsCheck.Checked = image.UseRelativePaths;
        _prefixRelativeWithDotSlashCheck.Checked = image.PrefixRelativeWithDotSlash;

        _styleCombo.SelectedIndex = FindStyleIndex(_settings.MarkdownStyle);
        _themeCombo.SelectedIndex = FindThemeIndex(_settings.ColorTheme);
    }

    private static int ToComboIndex(int value, ComboBox combo)
    {
        return value >= 0 && value < combo.Items.Count ? value : 0;
    }

    private static int FindZoomIndex(int percent)
    {
        var options = AppearanceSettings.ZoomPercentOptions;
        var closest = 0;
        for (var index = 0; index < options.Length; index++)
        {
            if (Math.Abs(options[index] - percent) < Math.Abs(options[closest] - percent))
            {
                closest = index;
            }
        }

        return closest;
    }

    private int FindStyleIndex(string styleId)
    {
        for (var index = 0; index < _styleOptions.Length; index++)
        {
            if (string.Equals(_styleOptions[index].Id, styleId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        for (var index = 0; index < _styleOptions.Length; index++)
        {
            if (string.Equals(_styleOptions[index].Id, StyleService.DefaultStyleId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private int FindThemeIndex(string themeId)
    {
        for (var index = 0; index < _themeOptions.Length; index++)
        {
            if (string.Equals(_themeOptions[index].Id, themeId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        var file = _settings.File;
        file.StartupAction = (StartupAction)_startupAction.SelectedIndex;
        file.AutoSaveEnabled = _autoSaveCheck.Checked;
        file.SaveOnDocumentSwitch = _saveOnSwitchCheck.Checked;
        file.SnapshotIntervalSeconds = (int)_snapshotInterval.Value;
        file.RecordRecentFiles = _recordRecentFilesCheck.Checked;
        file.RecordRecentFolders = _recordRecentFoldersCheck.Checked;
        file.NewLineStyle = _newLineStyleCombo.SelectedIndex == 0 ? NewLineStyle.Lf : NewLineStyle.Crlf;

        var editor = _settings.Editor;
        editor.VisualLineHeight = (float)_visualLineHeight.Value;
        editor.VisualFontSize = (int)_visualFontSize.Value;
        editor.VisualMaxContentWidth = (int)_visualMaxWidth.Value;
        editor.SourceFontSize = (int)_sourceFontSize.Value;
        editor.SourceIndentWidth = (int)_sourceIndentWidth.Value;

        if (_styleCombo.SelectedIndex >= 0 && _styleCombo.SelectedIndex < _styleOptions.Length)
        {
            _settings.MarkdownStyle = _styleOptions[_styleCombo.SelectedIndex].Id;
        }

        if (_themeCombo.SelectedIndex >= 0 && _themeCombo.SelectedIndex < _themeOptions.Length)
        {
            _settings.ColorTheme = _themeOptions[_themeCombo.SelectedIndex].Id;
        }

        var appearance = _settings.Appearance;
        var zoomOptions = AppearanceSettings.ZoomPercentOptions;
        if (_zoomCombo.SelectedIndex >= 0 && _zoomCombo.SelectedIndex < zoomOptions.Length)
        {
            appearance.ZoomPercent = zoomOptions[_zoomCombo.SelectedIndex];
        }
        appearance.RestoreZoomOnOpen = _restoreZoomCheck.Checked;
        appearance.CtrlWheelZoom = _ctrlWheelZoomCheck.Checked;
        appearance.TopMostWindow = _topMostCheck.Checked;
        appearance.AutoHideScrollbars = _autoHideScrollbarsCheck.Checked;

        _settings.General.AssociateMarkdownFiles = _associateMarkdownCheck.Checked;
        _settings.General.AssociateTextFiles = _associateTextCheck.Checked;

        var image = _settings.Image;
        if (_clipboardImageCombo.SelectedIndex >= 0)
        {
            image.ClipboardHandling = (ClipboardImageHandling)_clipboardImageCombo.SelectedIndex;
        }
        if (_fileImageCombo.SelectedIndex >= 0)
        {
            image.FileHandling = (FileImageHandling)_fileImageCombo.SelectedIndex;
        }
        image.DefaultDirectory = _defaultDirectoryTextBox.Text.Trim();
        image.UseRelativePaths = _useRelativePathsCheck.Checked;
        image.PrefixRelativeWithDotSlash = _prefixRelativeWithDotSlashCheck.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label NewLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    }

    private static Control Gap(int height)
    {
        return new Panel { Height = height, Dock = DockStyle.None };
    }

    private static TabPage CreateTab(string text, Control content)
    {
        var page = new TabPage(text) { UseVisualStyleBackColor = true, Padding = new Padding(8), AutoScroll = true };
        page.Controls.Add(content);
        return page;
    }
}
