using MarkLeaf.Documents;
using MarkLeaf.Native;
using MarkLeaf.Services;
using MarkLeaf.Services.Settings;
using MarkLeaf.Services.Styles;
using MarkLeaf.UI.Controls;

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
    private readonly Action? _onAddTheme;
    private readonly Action? _onResetAll;

    private readonly Button _resetAllButton = new()
    { Text = Loc.Get("prefs.resetAll"), AutoSize = true, FlatStyle = FlatStyle.System };

    private readonly PreferencesTabBar _tabBar = new();
    private readonly Panel _contentPanel = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        Margin = Padding.Empty,
        BackColor = SystemColors.ControlLightLight,
    };
    private Control[] _tabContents = [];

    private readonly ComboBox _startupAction = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly CheckBox _autoSaveCheck;
    private readonly CheckBox _saveOnSwitchCheck;
    private readonly NumericUpDown _snapshotInterval;
    private readonly Button _recoverButton;

    private readonly CheckBox _recordRecentFilesCheck;
    private readonly CheckBox _recordRecentFoldersCheck;
    private readonly Button _clearHistoryButton = new()
    { Text = Loc.Get("prefs.clearHistory"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly ComboBox _newLineStyleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _editShortcutsButton;

    private readonly NumericUpDown _visualLineHeight;
    private readonly NumericUpDown _visualFontSize;
    private readonly NumericUpDown _visualMaxWidth;

    private readonly NumericUpDown _sourceFontSize;
    private readonly NumericUpDown _sourceIndentWidth;
    private readonly Button _selectCjkFontButton = new()
    { Text = Loc.Get("prefs.editor.selectCjkFont"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Label _cjkFontLabel = new()
    { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _selectWesternFontButton = new()
    { Text = Loc.Get("prefs.editor.selectWesternFont"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Label _westernFontLabel = new()
    { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };

    private readonly (string Id, string DisplayName)[] _styleOptions;
    private readonly ComboBox _styleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly (string Id, string DisplayName)[] _themeOptions;
    private readonly ComboBox _themeCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _followSystemCheck;
    private readonly Button _defaultThemeButton;
    private readonly Button _addThemeButton = new()
    { Text = Loc.Get("prefs.appearance.addTheme"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _openThemeFolderButton = new()
    { Text = Loc.Get("prefs.appearance.openThemeFolder"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _restoreZoomCheck;
    private readonly CheckBox _ctrlWheelZoomCheck;
    private readonly CheckBox _topMostCheck;
    private readonly CheckBox _autoHideScrollbarsCheck;
    private readonly ComboBox _menuStyleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly ComboBox _languageCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _openCacheFolderButton = new()
    { Text = Loc.Get("prefs.general.openCacheFolder"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _openLogFolderButton = new()
    { Text = Loc.Get("prefs.general.openLogFolder"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _clearLogsButton = new()
    { Text = Loc.Get("prefs.general.clearLogs"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _openSettingsJsonButton = new()
    { Text = Loc.Get("prefs.general.openSettingsJson"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _associateMarkdownCheck;
    private readonly CheckBox _associateTextCheck;

    private readonly ComboBox _clipboardImageCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _fileImageCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _defaultDirectoryTextBox = new()
    { };
    private readonly Button _browseDirectoryButton = new()
    { Text = Loc.Get("prefs.images.browse"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _useRelativePathsCheck;
    private readonly CheckBox _prefixRelativeWithDotSlashCheck;
    private readonly Button _imageUploadButton = new()
    { Text = Loc.Get("prefs.images.uploadConfig"), AutoSize = true, FlatStyle = FlatStyle.System };

    private readonly Button _okButton = new()
    { Text = Loc.Get("common.ok"), FlatStyle = FlatStyle.System };

    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    private static readonly string[] StartupActionItems = [];

    private static readonly string[] ClipboardImageHandlingItems = [];

    private static readonly string[] FileImageHandlingItems = [];

    public PreferencesDialog(
        AppSettings settings,
        Action? onRecover = null,
        Action? onShowShortcuts = null,
        Action? onOpenThemeFolder = null,
        Action? onAddTheme = null,
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
        _onAddTheme = onAddTheme;
        _onOpenCacheFolder = onOpenCacheFolder;
        _onOpenLogFolder = onOpenLogFolder;
        _onClearLogs = onClearLogs;
        _onOpenSettingsJson = onOpenSettingsJson;
        _onClearHistory = onClearHistory;
        _onResetAll = onResetAll;

        _languageCombo.Items.Add(Loc.Get("language.zh-CN"));
        _languageCombo.Items.Add(Loc.Get("language.zh-TW"));
        _languageCombo.Items.Add(Loc.Get("language.en-US"));
        _languageCombo.Items.Add(Loc.Get("language.ja-JP"));

        _styleOptions = StyleService.GetAllStyles().ToArray();
        foreach (var (_, displayName) in _styleOptions)
            _styleCombo.Items.Add(displayName);
        _themeOptions = ColorThemeService.All
            .Select(t => (t.Id, t.DisplayName)).ToArray();
        foreach (var (_, displayName) in _themeOptions)
            _themeCombo.Items.Add(displayName);
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.darkOnly"));
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.alwaysOwnerDraw"));
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.system"));

        _startupAction.Items.Add(Loc.Get("prefs.file.startupAction.newFile"));
        _startupAction.Items.Add(Loc.Get("prefs.file.startupAction.lastWorkspace"));
        _startupAction.Items.Add(Loc.Get("prefs.file.startupAction.lastWorkspaceAndFiles"));

        _clipboardImageCombo.Items.Add(Loc.Get("prefs.images.clipboard.saveToDefault"));
        _clipboardImageCombo.Items.Add(Loc.Get("prefs.images.clipboard.copyToAssets"));
        _clipboardImageCombo.Items.Add(Loc.Get("prefs.images.clipboard.copyToAssetsUpload"));
        _fileImageCombo.Items.Add(Loc.Get("prefs.images.file.referenceOriginal"));
        _fileImageCombo.Items.Add(Loc.Get("prefs.images.file.copyToAssets"));
        _fileImageCombo.Items.Add(Loc.Get("prefs.images.file.copyToAssetsUpload"));

        _useRelativePathsCheck = new CheckBox
        { Text = Loc.Get("prefs.images.useRelativePaths"), AutoSize = true, FlatStyle = FlatStyle.System };
        _prefixRelativeWithDotSlashCheck = new CheckBox
        { Text = Loc.Get("prefs.images.prefixWithDotSlash"), AutoSize = true, FlatStyle = FlatStyle.System };

        _autoSaveCheck = new CheckBox
        { Text = Loc.Get("prefs.file.autoSave"), AutoSize = true, FlatStyle = FlatStyle.System };
        _saveOnSwitchCheck = new CheckBox
        { Text = Loc.Get("prefs.file.saveOnSwitch"), AutoSize = true, FlatStyle = FlatStyle.System };

        _snapshotInterval = new NumericUpDown
        { Minimum = 10, Maximum = 300, Increment = 5 };
        _recoverButton = new Button
        { Text = Loc.Get("prefs.file.recoverUnsaved"), AutoSize = true, FlatStyle = FlatStyle.System };

        _recordRecentFilesCheck = new CheckBox
        { Text = Loc.Get("prefs.file.recordRecentFiles"), AutoSize = true, FlatStyle = FlatStyle.System };
        _recordRecentFoldersCheck = new CheckBox
        { Text = Loc.Get("prefs.file.recordRecentFolders"), AutoSize = true, FlatStyle = FlatStyle.System };
        _newLineStyleCombo.Items.Add("LF");
        _newLineStyleCombo.Items.Add("CRLF");

        _visualLineHeight = new NumericUpDown
        { Minimum = 1.0m, Maximum = 3.0m, Increment = 0.05m, DecimalPlaces = 2 };
        _visualFontSize = new NumericUpDown
        { Minimum = 12, Maximum = 24, Increment = 1 };
        _visualMaxWidth = new NumericUpDown
        { Minimum = 600, Maximum = 1200, Increment = 20 };

        _sourceFontSize = new NumericUpDown
        { Minimum = 12, Maximum = 24, Increment = 1 };
        _sourceIndentWidth = new NumericUpDown
        { Minimum = 2, Maximum = 8, Increment = 2 };

        _editShortcutsButton = new Button
        { Text = Loc.Get("prefs.general.editShortcuts"), AutoSize = true, FlatStyle = FlatStyle.System };

        _restoreZoomCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.restoreZoom"), AutoSize = true, FlatStyle = FlatStyle.System };
        _ctrlWheelZoomCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.ctrlWheelZoom"), AutoSize = true, FlatStyle = FlatStyle.System };
        _topMostCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.topMost"), AutoSize = true, FlatStyle = FlatStyle.System };
        _autoHideScrollbarsCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.autoHideScrollbars"), AutoSize = true, FlatStyle = FlatStyle.System };
        _followSystemCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.followSystemColor"), AutoSize = true, FlatStyle = FlatStyle.System };
        _followSystemCheck.CheckedChanged += (_, _) =>
            _themeCombo.Enabled = !_followSystemCheck.Checked;

        _defaultThemeButton = new Button
        { Text = Loc.Get("prefs.appearance.defaultTheme"), AutoSize = true, FlatStyle = FlatStyle.System };
        _defaultThemeButton.Click += (_, _) =>
        {
            using var dialog = new DefaultThemeDialog(
                _settings.Appearance.DefaultLightThemeId,
                _settings.Appearance.DefaultDarkThemeId);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _settings.Appearance.DefaultLightThemeId = dialog.LightThemeId;
                _settings.Appearance.DefaultDarkThemeId = dialog.DarkThemeId;
            }
        };

        _associateMarkdownCheck = new CheckBox
        { Text = Loc.Get("prefs.general.associateMarkdown"), AutoSize = true, FlatStyle = FlatStyle.System };
        _associateTextCheck = new CheckBox
        { Text = Loc.Get("prefs.general.associateText"), AutoSize = true, FlatStyle = FlatStyle.System };

        LoadSettingsIntoControls();

        ApplyDpiSizes();

        Text = Loc.Get("prefs.title");
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(this.ScaleForDpi(446), this.ScaleForDpi(537));

        _tabBar.Margin = Padding.Empty;
        _tabContents = [BuildFileTab(), BuildAppearanceTab(), BuildEditorTab(), BuildImagesTab(), BuildGeneralTab()];
        _contentPanel.Controls.Add(_tabContents[0]);
        _tabBar.TabChanged += (_, index) => SwitchTabPage(index);

        _okButton.Click += OnOkClick;
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _recoverButton.Click += (_, _) => _onRecover?.Invoke();
        _editShortcutsButton.Click += (_, _) => _onShowShortcuts?.Invoke();
        _selectCjkFontButton.Click += (_, _) => SelectCjkFont();
        _selectWesternFontButton.Click += (_, _) => SelectWesternFont();
        _addThemeButton.Click += (_, _) => _onAddTheme?.Invoke();
        _openThemeFolderButton.Click += (_, _) => _onOpenThemeFolder?.Invoke();
        _openCacheFolderButton.Click += (_, _) => _onOpenCacheFolder?.Invoke();
        _openLogFolderButton.Click += (_, _) => _onOpenLogFolder?.Invoke();
        _clearLogsButton.Click += (_, _) => _onClearLogs?.Invoke();
        _openSettingsJsonButton.Click += (_, _) => _onOpenSettingsJson?.Invoke();
        _clearHistoryButton.Click += (_, _) => _onClearHistory?.Invoke();
        _resetAllButton.Click += (_, _) =>
        {
            if (MessageBox.Show(
                    this,
                    Loc.Get("prefs.resetAll.confirm"),
                    Loc.Get("prefs.resetAll.title"),
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
            Loc.Get("dialog.imageUploadNotAvailable"),
            "MarkLeaf",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Height = this.ScaleForDpi(26),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(this.ScaleForDpi(20), this.ScaleForDpi(23), this.ScaleForDpi(20), 0),
            BackColor = SystemColors.ControlLightLight,
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_okButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 0, this.ScaleForDpi(23)),
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

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        Shown += (_, _) =>
        {
            _tabBar.ApplyThemeColors(ColorThemeService.GetActiveColors());
            if (ColorThemeService.IsActiveThemeDark())
            {
                DarkModeService.ApplyDialogDarkMode(this, SystemColors.Control, SystemColors.ControlText);
                DarkModeService.SetWindowDarkTitleBar(this);
                // .NET SetColorMode 对首个可见 TabPage 的控件覆盖不完整，再次强制设色。
                ForceComboDark(_startupAction);
                ForceComboDark(_newLineStyleCombo);
            }
        };
    }

    private static void ForceComboDark(ComboBox combo)
    {
        if (!combo.IsHandleCreated) return;
        typeof(Control).GetMethod("RecreateHandle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(combo, null);
    }

    private Control BuildFileTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(11), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel(Loc.Get("prefs.file.startupAction.label")), 0, 0);
        panel.Controls.Add(_startupAction, 1, 0);

        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);


        panel.Controls.Add(NewLabel(Loc.Get("prefs.file.saveOptions.label")), 0, 2);
        panel.Controls.Add(BuildSaveOptionsPanel(), 1, 2);

        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.file.newLineStyle.label")), 0, 4);
        panel.Controls.Add(BuildNewLinePanel(), 1, 4);

        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(Gap(), 1, 5);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.file.history.label")), 0, 6);
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
        panel.Controls.Add(Gap(), 0, 1);
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
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(new Label
        {
            Text = Loc.Get("prefs.file.newLineHint"),
            AutoSize = true,
            MaximumSize = new Size(this.ScaleForDpi(246), 0),
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Regular),

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
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_saveOnSwitchCheck, 0, 2);
        panel.Controls.Add(Gap(), 0, 3);

        var intervalRow = new FlowLayoutPanel { AutoSize = true };
        intervalRow.Controls.Add(new Label { Text = Loc.Get("prefs.file.snapshotInterval"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        intervalRow.Controls.Add(_snapshotInterval);
        intervalRow.Controls.Add(new Label { Text = Loc.Get("prefs.file.seconds"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
        panel.Controls.Add(intervalRow, 0, 4);

        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(_recoverButton, 0, 6);

        return panel;
    }

    private Control BuildEditorTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(11), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel(Loc.Get("prefs.editor.visual")), 0, 0);
        panel.Controls.Add(BuildVisualPanel(), 1, 0);

        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.editor.source")), 0, 2);
        panel.Controls.Add(BuildSourcePanel(), 1, 2);

        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.editor.zoom.label")), 0, 4);
        panel.Controls.Add(BuildZoomPanel(), 1, 4);

        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(Gap(), 1, 5);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 6);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 6);

       /*  var noteLabel = new Label
        {
            Text = "某些设置可能由当前的排版样式接管，转到”外观”以更改。",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Regular),
        };
        panel.Controls.Add(noteLabel, 0, 7);
        panel.SetColumnSpan(noteLabel, 2); */

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

        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.visualLineHeight"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 0);
        panel.Controls.Add(_visualLineHeight, 1, 0);
        panel.Controls.Add(Gap(), 0, 1);


        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.visualFontSize"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 2);
        panel.Controls.Add(_visualFontSize, 1, 2);
        panel.Controls.Add(Gap(), 0, 3);


        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.visualMaxWidth"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 4);
        var widthRow = new FlowLayoutPanel { AutoSize = true };
        widthRow.Controls.Add(_visualMaxWidth);
        widthRow.Controls.Add(new Label { Text = Loc.Get("prefs.editor.pixels"), AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
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

        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.visualFontSize"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 0);
        panel.Controls.Add(_sourceFontSize, 1, 0);
        panel.Controls.Add(Gap(), 0, 1);

        
        panel.Controls.Add(_selectCjkFontButton, 0, 4);
        panel.Controls.Add(_cjkFontLabel, 1, 4);

        panel.Controls.Add(Gap(), 0, 3);

        panel.Controls.Add(_selectWesternFontButton, 0, 6);
        panel.Controls.Add(_westernFontLabel, 1, 6);
        panel.Controls.Add(Gap(), 0, 5);

        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.sourceIndentWidth"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 2);
        panel.Controls.Add(_sourceIndentWidth, 1, 2);

        return panel;
    }

    private Control BuildAppearanceTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(11), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));


        panel.Controls.Add(NewLabel(Loc.Get("prefs.appearance.style.label")), 0, 0);
        panel.Controls.Add(_styleCombo, 1, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.appearance.colorScheme.label")), 0, 2);
        panel.Controls.Add(_themeCombo, 1, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);

        //panel.Controls.Add(NewLabel("颜色模式(&M)"), 0, 4);
        panel.Controls.Add(_followSystemCheck, 1, 4);
        panel.Controls.Add(Gap(), 1, 5);
        panel.Controls.Add(_defaultThemeButton, 1, 6);

        panel.Controls.Add(Gap(), 0, 7);
        panel.Controls.Add(Gap(), 1, 7);

        var themeFolderRow = new FlowLayoutPanel { AutoSize = true };
        themeFolderRow.Controls.Add(_addThemeButton);
        themeFolderRow.Controls.Add(_openThemeFolderButton);
        panel.Controls.Add(themeFolderRow, 1, 8);
        panel.Controls.Add(Gap(), 0, 8);


        panel.Controls.Add(Gap(), 0, 9);
        panel.Controls.Add(Gap(), 1, 9);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.appearance.window.label")), 0, 10);
        panel.Controls.Add(BuildWindowPanel(), 1, 10);

        panel.Controls.Add(Gap(), 0, 11);
        panel.Controls.Add(Gap(), 1, 11);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.appearance.menuStyle.label")), 0, 12);
        panel.Controls.Add(BuildMenuStylePanel(), 1, 12);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 13);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 13);

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
        panel.Controls.Add(_restoreZoomCheck, 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_ctrlWheelZoomCheck, 0, 2);
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
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_autoHideScrollbarsCheck, 0, 2);
        return panel;
    }

    private Control BuildMenuStylePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_menuStyleCombo, 0, 0);
        return panel;
    }

    private Control BuildGeneralTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(11), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.language.label")), 0, 0);
        panel.Controls.Add(_languageCombo, 1, 0);

        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.shortcuts.label")), 0, 2);
        panel.Controls.Add(_editShortcutsButton, 1, 2);

        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.fileAssociation.label")), 0, 4);
        panel.Controls.Add(BuildFileAssociationPanel(), 1, 4);

        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(Gap(), 1, 5);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.storage.label")), 0, 6);
        panel.Controls.Add(BuildStoragePanel(), 1, 6);

        panel.Controls.Add(Gap(), 0, 7);
        panel.Controls.Add(Gap(), 1, 7);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.logs.label")), 0, 8);
        panel.Controls.Add(BuildLogsPanel(), 1, 8);

        panel.Controls.Add(Gap(), 0, 9);
        panel.Controls.Add(Gap(), 1, 9);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.advanced.label")), 0, 10);
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
        panel.Controls.Add(Gap(), 0, 1);
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
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_associateTextCheck, 0, 2);
        return panel;
    }

    private Control BuildImagesTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(11), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(NewLabel(Loc.Get("prefs.images.clipboard.label")), 0, 0);
        panel.Controls.Add(_clipboardImageCombo, 1, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.images.fromFile.label")), 0, 2);
        panel.Controls.Add(_fileImageCombo, 1, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.images.defaultDirectory.label")), 0, 4);
        panel.Controls.Add(BuildDefaultDirectoryPanel(), 1, 4);
        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(Gap(), 1, 5);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.images.reference.label")), 0, 6);
        panel.Controls.Add(BuildReferencePanel(), 1, 6);
        panel.Controls.Add(Gap(), 0, 7);
        panel.Controls.Add(Gap(), 1, 7);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.images.upload.label")), 0, 8);
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
        panel.Controls.Add(Gap(), 0, 1);
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
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_prefixRelativeWithDotSlashCheck, 0, 2);
        return panel;
    }

    private void SelectCjkFont()
    {
        using var dialog = new FontDialog
        {
            FontMustExist = true,
            AllowScriptChange = false,
            ShowColor = false,
            ShowEffects = false,
        };
        if (!string.IsNullOrWhiteSpace(_cjkFontLabel.Text))
        {
            try { dialog.Font = new Font(_cjkFontLabel.Text, (float)_sourceFontSize.Value); }
            catch { }
        }
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _cjkFontLabel.Text = dialog.Font.Name;
        dialog.Font.Dispose();
    }

    private void SelectWesternFont()
    {
        using var dialog = new FontDialog
        {
            FontMustExist = true,
            AllowScriptChange = false,
            ShowColor = false,
            ShowEffects = false,
        };
        if (!string.IsNullOrWhiteSpace(_westernFontLabel.Text))
        {
            try { dialog.Font = new Font(_westernFontLabel.Text, (float)_sourceFontSize.Value); }
            catch { }
        }
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _westernFontLabel.Text = dialog.Font.Name;
        dialog.Font.Dispose();
    }

    private void BrowseDefaultDirectory()
    {
        var current = _defaultDirectoryTextBox.Text.Trim();
        using var dialog = new FolderBrowserDialog
        {
            Description = Loc.Get("prefs.images.selectDefaultDir"),
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
                Loc.Get("dialog.imageDirChangeWarn"),
                Loc.Get("dialog.imageDirChangeTitle"),
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

    private void SwitchTabPage(int index)
    {
        if (index < 0 || index >= _tabContents.Length) return;
        _contentPanel.Controls.Clear();
        _contentPanel.Controls.Add(_tabContents[index]);
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
        _cjkFontLabel.Text = editor.SourceCjkFontFamily;
        _westernFontLabel.Text = editor.SourceFontFamily;
        _sourceIndentWidth.Value = editor.SourceIndentWidth;

        var appearance = _settings.Appearance;
        _restoreZoomCheck.Checked = appearance.RestoreZoomOnOpen;
        _ctrlWheelZoomCheck.Checked = appearance.CtrlWheelZoom;
        _topMostCheck.Checked = appearance.TopMostWindow;
        _autoHideScrollbarsCheck.Checked = appearance.AutoHideScrollbars;
        _followSystemCheck.Checked = appearance.FollowSystemColorMode;
        _themeCombo.Enabled = !appearance.FollowSystemColorMode;
        _menuStyleCombo.SelectedIndex = (int)appearance.MenuBarStyle;

        _associateMarkdownCheck.Checked = _settings.General.AssociateMarkdownFiles;
        _associateTextCheck.Checked = _settings.General.AssociateTextFiles;
        _languageCombo.SelectedIndex = LanguageToIndex(_settings.General.UiLanguage);

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

    private static readonly string[] LocaleCodes = ["zh-CN", "zh-TW", "en-US", "ja-JP"];

    private static int LanguageToIndex(string code)
    {
        var index = Array.IndexOf(LocaleCodes, code);
        return index >= 0 ? index : 0;
    }

    private static string IndexToLanguage(int index)
    {
        return index >= 0 && index < LocaleCodes.Length ? LocaleCodes[index] : "zh-CN";
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
        if (!string.IsNullOrWhiteSpace(_cjkFontLabel.Text))
            editor.SourceCjkFontFamily = _cjkFontLabel.Text;
        if (!string.IsNullOrWhiteSpace(_westernFontLabel.Text))
            editor.SourceFontFamily = _westernFontLabel.Text;
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
        appearance.RestoreZoomOnOpen = _restoreZoomCheck.Checked;
        appearance.CtrlWheelZoom = _ctrlWheelZoomCheck.Checked;
        appearance.TopMostWindow = _topMostCheck.Checked;
        appearance.AutoHideScrollbars = _autoHideScrollbarsCheck.Checked;
        appearance.FollowSystemColorMode = _followSystemCheck.Checked;
        if (_menuStyleCombo.SelectedIndex >= 0)
            appearance.MenuBarStyle = (MenuBarStyle)_menuStyleCombo.SelectedIndex;

        _settings.General.AssociateMarkdownFiles = _associateMarkdownCheck.Checked;
        _settings.General.AssociateTextFiles = _associateTextCheck.Checked;
        _settings.General.UiLanguage = IndexToLanguage(_languageCombo.SelectedIndex);

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

    private void ApplyDpiSizes()
    {
        _contentPanel.Padding = new Padding(
            this.ScaleForDpi(14), this.ScaleForDpi(17), this.ScaleForDpi(14), 0);

        var comboW = this.ScaleForDpi(183);
        _startupAction.Width = comboW;
        _newLineStyleCombo.Width = comboW;
        _styleCombo.Width = comboW;
        _themeCombo.Width = comboW;
        _menuStyleCombo.Width = comboW;
        _languageCombo.Width = comboW;
        _clipboardImageCombo.Width = comboW;
        _fileImageCombo.Width = comboW;
        _defaultDirectoryTextBox.Width = comboW;

        var nudW = this.ScaleForDpi(40);
        _snapshotInterval.Width = nudW;
        _visualLineHeight.Width = nudW;
        _visualFontSize.Width = nudW;
        _visualMaxWidth.Width = nudW;
        _sourceFontSize.Width = nudW;
        _sourceIndentWidth.Width = nudW;

        var btnW = this.ScaleForDpi(86);
        var btnH = this.ScaleForDpi(26);
        _okButton.Width = btnW;
        _okButton.Height = btnH;
        _cancelButton.Width = btnW;
        _cancelButton.Height = btnH;
    }

    private Label NewLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Bold),
            Padding = new Padding(this.ScaleForDpi(6), this.ScaleForDpi(6), 0, 0),

        };
    }

    private Control Gap()
    {
        return new Panel { Height = this.ScaleGapForDpi(), Width = 0, Dock = DockStyle.None };
    }
}
