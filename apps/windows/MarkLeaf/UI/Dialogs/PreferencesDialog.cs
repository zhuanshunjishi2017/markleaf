using System.Text.Json;
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
    private readonly AppSettings _targetSettings;
    private readonly Action? _onRecover;
    private readonly Action? _onShowShortcuts;
    private readonly Action? _onOpenThemeFolder;
    private readonly Action? _onOpenCacheFolder;
    private readonly Action? _onOpenLogFolder;
    private readonly Action? _onClearLogs;
    private readonly Action? _onOpenSettingsJson;
    private readonly Action? _onClearHistory;
    private readonly Action? _onAddTheme;
    private readonly Action? _onCheckForUpdates;
    private readonly List<PreferenceOptionsContainer> _optionContainers = [];

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
    private readonly ComboBox _defaultEncodingCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly Button _editShortcutsButton;

    private readonly NumericUpDown _visualLineHeight;
    private readonly NumericUpDown _visualFontSize;
    private readonly NumericUpDown _visualMaxWidth;
    private readonly CheckBox _visualCjkAutoSpacingCheck;
    private readonly CheckBox _autoConvertUnsafeEmphasisOnNormalizeCheck;
    private readonly CheckBox _escapeLiteralSymbolsCheck;
    private readonly CheckBox _escapeMarkdownLiteralSymbolsCheck;
    private readonly RadioButton _unsafeEmphasisPromptRadio;
    private readonly RadioButton _unsafeEmphasisLiteralRadio;
    private readonly RadioButton _unsafeEmphasisAutoConvertRadio;
    private readonly CheckBox _exitBlockOnEmptyEnterCheck;
    private readonly CheckBox _useShiftEnterHardBreakCheck;
    private readonly ComboBox _markdownCodeFenceCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _markdownEmphasisMarkerCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _markdownBulletMarkerCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly NumericUpDown _sourceIndentWidth;
    private readonly CheckBox _showParagraphBlockHandleCheck;
    private string _cjkFontFamily = "Microsoft YaHei";
    private string _westernFontFamily = "Cascadia Mono";
    private int _sourceFontSize = 14;
    private readonly TextBox _cjkFontTextBox = new();
    private readonly TextBox _westernFontTextBox = new();
    private readonly NumericUpDown _sourceFontSizeNumeric = new()
    { Minimum = 12, Maximum = 24, Increment = 1 };
    private readonly Button _selectCjkFontButton = new()
    { Text = Loc.Get("prefs.editor.fontSettings.select"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _selectWesternFontButton = new()
    { Text = Loc.Get("prefs.editor.fontSettings.select"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly ComboBox _cjkLanguageTagCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList};

    private readonly (string Id, string DisplayName)[] _styleOptions;
    private readonly ComboBox _styleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly (string Id, string DisplayName)[] _themeOptions;
    private readonly ComboBox _themeCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly (string Id, string DisplayName)[] _lightThemeOptions;
    private readonly ComboBox _defaultLightThemeCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly (string Id, string DisplayName)[] _darkThemeOptions;
    private readonly ComboBox _defaultDarkThemeCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _followSystemCheck;
    private readonly Button _addThemeButton = new()
    { Text = Loc.Get("prefs.appearance.addTheme"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly Button _openThemeFolderButton = new()
    { Text = Loc.Get("prefs.appearance.openThemeFolder"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _restoreZoomCheck;
    private readonly CheckBox _ctrlWheelZoomCheck;
    private readonly CheckBox _topMostCheck;
    private readonly CheckBox _autoHideScrollbarsCheck;
    private readonly CheckBox _showCodeHighlightCheck;
    private readonly ComboBox _menuStyleCombo = new()
    { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _showMenuKeyboardShortcutsCheck;
    private readonly CheckBox _showMenuMnemonicsCheck;
    private readonly Button _customizeStatusBarButton = new()
    { Text = Loc.Get("prefs.appearance.statusBar.customize"), AutoSize = true, FlatStyle = FlatStyle.System };
    private StatusBarSettings _statusBarSettings = new();

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
    private readonly Button _checkForUpdatesButton = new()
    { Text = Loc.Get("prefs.general.checkForUpdates"), AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly CheckBox _autoCheckForUpdatesCheck;

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
    {
        Text = Loc.Get("common.ok"),
        FlatStyle = FlatStyle.System,
        UseVisualStyleBackColor = false,
    };

    private readonly Button _cancelButton = new()
    { Text = Loc.Get("common.cancel"), FlatStyle = FlatStyle.System };

    private static readonly string[] StartupActionItems = [];

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
        Action? onCheckForUpdates = null)
    {
        _targetSettings = settings;
        _settings = CloneSettings(settings);
        _onRecover = onRecover;
        _onShowShortcuts = onShowShortcuts;
        _onOpenThemeFolder = onOpenThemeFolder;
        _onAddTheme = onAddTheme;
        _onOpenCacheFolder = onOpenCacheFolder;
        _onOpenLogFolder = onOpenLogFolder;
        _onClearLogs = onClearLogs;
        _onOpenSettingsJson = onOpenSettingsJson;
        _onClearHistory = onClearHistory;
        _onCheckForUpdates = onCheckForUpdates;

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
        _lightThemeOptions = ColorThemeService.All
            .Where(theme => !theme.IsDark)
            .Select(theme => (theme.Id, theme.DisplayName))
            .ToArray();
        foreach (var (_, displayName) in _lightThemeOptions)
            _defaultLightThemeCombo.Items.Add(displayName);
        _darkThemeOptions = ColorThemeService.All
            .Where(theme => theme.IsDark)
            .Select(theme => (theme.Id, theme.DisplayName))
            .ToArray();
        foreach (var (_, displayName) in _darkThemeOptions)
            _defaultDarkThemeCombo.Items.Add(displayName);
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.darkOnly"));
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.alwaysOwnerDraw"));
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.system"));
        _menuStyleCombo.Items.Add(Loc.Get("prefs.appearance.menuStyle.tabBar"));

        _startupAction.Items.Add(Loc.Get("prefs.file.startupAction.newFile"));
        _startupAction.Items.Add(Loc.Get("prefs.file.startupAction.lastWorkspace"));
        _startupAction.Items.Add(Loc.Get("prefs.file.startupAction.lastWorkspaceAndFiles"));

        _cjkLanguageTagCombo.Items.Add(Loc.Get("prefs.editor.cjkLang.sc"));
        _cjkLanguageTagCombo.Items.Add(Loc.Get("prefs.editor.cjkLang.tc"));
        _cjkLanguageTagCombo.Items.Add(Loc.Get("prefs.editor.cjkLang.ja"));
        _cjkLanguageTagCombo.Items.Add(Loc.Get("prefs.editor.cjkLang.ko"));

        _clipboardImageCombo.Items.Add(Loc.Get("prefs.images.clipboard.saveToDefault"));
        _clipboardImageCombo.Items.Add(Loc.Get("prefs.images.clipboard.copyToAssets"));
        _fileImageCombo.Items.Add(Loc.Get("prefs.images.file.referenceOriginal"));
        _fileImageCombo.Items.Add(Loc.Get("prefs.images.file.copyToAssets"));

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
        foreach (var encoding in DocumentEncodingPolicy.All)
        {
            _defaultEncodingCombo.Items.Add(encoding.DisplayName);
        }
        _newLineStyleCombo.Items.Add("LF");
        _newLineStyleCombo.Items.Add("CRLF");

        _visualLineHeight = new NumericUpDown
        { Minimum = 1.0m, Maximum = 3.0m, Increment = 0.05m, DecimalPlaces = 2 };
        _visualFontSize = new NumericUpDown
        { Minimum = 12, Maximum = 24, Increment = 1 };
        _visualMaxWidth = new NumericUpDown
        { Minimum = 600, Maximum = 1200, Increment = 20 };
        _visualCjkAutoSpacingCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.visualCjkAutoSpacing"), AutoSize = true, FlatStyle = FlatStyle.System };
        _autoConvertUnsafeEmphasisOnNormalizeCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.autoConvertUnsafeEmphasis.normalize"), AutoSize = true, FlatStyle = FlatStyle.System };
        _escapeLiteralSymbolsCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.markdown.escapeLiteralSymbols"), AutoSize = true, FlatStyle = FlatStyle.System };
        _escapeMarkdownLiteralSymbolsCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.markdown.escapeMarkdownLiteralSymbols"), AutoSize = true, FlatStyle = FlatStyle.System };
        _unsafeEmphasisPromptRadio = new RadioButton
        { Text = Loc.Get("prefs.editor.sourceUnsafeEmphasis.prompt"), AutoSize = true, FlatStyle = FlatStyle.System };
        _unsafeEmphasisLiteralRadio = new RadioButton
        { Text = Loc.Get("prefs.editor.sourceUnsafeEmphasis.literal"), AutoSize = true, FlatStyle = FlatStyle.System };
        _unsafeEmphasisAutoConvertRadio = new RadioButton
        { Text = Loc.Get("prefs.editor.sourceUnsafeEmphasis.autoConvert"), AutoSize = true, FlatStyle = FlatStyle.System };
        _exitBlockOnEmptyEnterCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.exitBlockOnEmptyEnter"), AutoSize = true, FlatStyle = FlatStyle.System };
        _useShiftEnterHardBreakCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.useShiftEnterHardBreak"), AutoSize = true, FlatStyle = FlatStyle.System };
        _markdownCodeFenceCombo.Items.Add(Loc.Get("prefs.editor.markdown.codeFence.backtick"));
        _markdownCodeFenceCombo.Items.Add(Loc.Get("prefs.editor.markdown.codeFence.tilde"));
        _markdownEmphasisMarkerCombo.Items.Add(Loc.Get("prefs.editor.markdown.emphasis.asterisk"));
        _markdownEmphasisMarkerCombo.Items.Add(Loc.Get("prefs.editor.markdown.emphasis.underscore"));
        _markdownBulletMarkerCombo.Items.Add(Loc.Get("prefs.editor.markdown.bullet.dash"));
        _markdownBulletMarkerCombo.Items.Add(Loc.Get("prefs.editor.markdown.bullet.asterisk"));
        _markdownBulletMarkerCombo.Items.Add(Loc.Get("prefs.editor.markdown.bullet.plus"));

        _sourceIndentWidth = new NumericUpDown
        { Minimum = 2, Maximum = 8, Increment = 2 };

        _showParagraphBlockHandleCheck = new CheckBox
        { Text = Loc.Get("prefs.editor.showParagraphBlockHandle"), AutoSize = true, FlatStyle = FlatStyle.System };

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
        _showCodeHighlightCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.showCodeHighlight"), AutoSize = true, FlatStyle = FlatStyle.System };
        _showMenuKeyboardShortcutsCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.showMenuKeyboardShortcuts"), AutoSize = true, FlatStyle = FlatStyle.System };
        _showMenuMnemonicsCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.showMenuMnemonics"), AutoSize = true, FlatStyle = FlatStyle.System };
        _followSystemCheck = new CheckBox
        { Text = Loc.Get("prefs.appearance.followSystemColor"), AutoSize = true, FlatStyle = FlatStyle.System };
        _followSystemCheck.CheckedChanged += (_, _) =>
            _themeCombo.Enabled = !_followSystemCheck.Checked;

        _associateMarkdownCheck = new CheckBox
        { Text = Loc.Get("prefs.general.associateMarkdown"), AutoSize = true, FlatStyle = FlatStyle.System };
        _associateTextCheck = new CheckBox
        { Text = Loc.Get("prefs.general.associateText"), AutoSize = true, FlatStyle = FlatStyle.System };
        _autoCheckForUpdatesCheck = new CheckBox
        { Text = Loc.Get("prefs.general.autoCheckForUpdates"), AutoSize = true, FlatStyle = FlatStyle.System };

        LoadSettingsIntoControls();

        ApplyDpiSizes();

        Text = Loc.Get("prefs.title");
        BackColor = SystemColors.ControlLightLight;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(this.ScaleForDpi(460), this.ScaleForDpi(600));

        _tabBar.Margin = Padding.Empty;
        _tabContents = [BuildFileTab(), BuildAppearanceTab(), BuildEditorTab(), BuildImagesTab(), BuildGeneralTab()];
        _contentPanel.Padding = new Padding(0, this.ScaleForDpi(5), 0, 0);
        _contentPanel.Controls.Add(_tabContents[0]);
        _tabBar.TabChanged += (_, index) => SwitchTabPage(index);

        _recoverButton.Click += (_, _) => _onRecover?.Invoke();
        _editShortcutsButton.Click += (_, _) => _onShowShortcuts?.Invoke();
        _selectCjkFontButton.Click += (_, _) => SelectSourceFont(_cjkFontTextBox);
        _selectWesternFontButton.Click += (_, _) => SelectSourceFont(_westernFontTextBox);
        _customizeStatusBarButton.Click += (_, _) => OpenStatusBarSettings();
        _addThemeButton.Click += (_, _) => _onAddTheme?.Invoke();
        _openThemeFolderButton.Click += (_, _) => _onOpenThemeFolder?.Invoke();
        _openCacheFolderButton.Click += (_, _) => _onOpenCacheFolder?.Invoke();
        _openLogFolderButton.Click += (_, _) => _onOpenLogFolder?.Invoke();
        _clearLogsButton.Click += (_, _) => _onClearLogs?.Invoke();
        _openSettingsJsonButton.Click += (_, _) => _onOpenSettingsJson?.Invoke();
        _clearHistoryButton.Click += (_, _) => _onClearHistory?.Invoke();
        _checkForUpdatesButton.Click += (_, _) => _onCheckForUpdates?.Invoke();
        _okButton.Click += (_, _) =>
        {
            CommitControlsToSettings();
            CopySettings(_settings, _targetSettings);
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
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
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(
                0,
                this.ScaleForDpi(5),
                this.ScaleForDpi(18),
                this.ScaleForDpi(21)),
            BackColor = SystemColors.ControlLightLight,
        };
        buttons.Controls.Add(_cancelButton);
        buttons.Controls.Add(_okButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
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
            ApplySegmentedTabColors();
            if (ColorThemeService.IsActiveThemeDark())
            {
                DarkModeService.ApplyDialogDarkMode(this, SystemColors.Control, SystemColors.ControlText);
                DarkModeService.SetWindowDarkTitleBar(this);
                // .NET SetColorMode 对首个可见 TabPage 的控件覆盖不完整，再次强制设色。
                ForceComboDark(_startupAction);
                ForceComboDark(_defaultEncodingCombo);
                ForceComboDark(_newLineStyleCombo);
            }
            ApplyOkButtonThemeColors();
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
        var page = CreateSettingsPage();
        page.Controls.Add(NewLabel(Loc.Get("prefs.file.startupAction.label")), 0, 0);
        page.Controls.Add(_startupAction, 1, 0);
        page.Controls.Add(Gap(), 0, 1);
        page.Controls.Add(Gap(), 1, 1);
        page.Controls.Add(NewLabel(Loc.Get("prefs.file.saveOptions.label")), 0, 2);
        page.Controls.Add(BuildSaveOptionsPanel(), 1, 2);
        page.Controls.Add(Gap(), 0, 3);
        page.Controls.Add(Gap(), 1, 3);
        page.Controls.Add(NewLabel(Loc.Get("prefs.file.textFormat.label")), 0, 4);
        page.Controls.Add(BuildTextFormatPanel(), 1, 4);
        page.Controls.Add(Gap(), 0, 5);
        page.Controls.Add(Gap(), 1, 5);
        page.Controls.Add(NewLabel(Loc.Get("prefs.file.history.label")), 0, 6);
        page.Controls.Add(BuildHistoryPanel(), 1, 6);
        page.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);
        page.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 7);
        return BuildFramedPage(page);
    }

    private TableLayoutPanel CreateSettingsPage()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(22), this.ScaleForDpi(14), this.ScaleForDpi(7)),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(86)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return panel;
    }

    private Control BuildSegmentedPage(string[] labels, Control[] pages)
    {
        var container = new PreferenceOptionsContainer(labels, pages) { Dock = DockStyle.Fill };
        _optionContainers.Add(container);
        return container;
    }

    private Control BuildFramedPage(Control page)
    {
        var container = new PreferenceOptionsContainer(page) { Dock = DockStyle.Fill };
        _optionContainers.Add(container);
        return container;
    }

    private Control BuildTextFormatPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(64)));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, this.ScaleForDpi(6)));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var hintMaximumWidth = this.ScaleForDpi(226);
        panel.Controls.Add(NewInlineLabel(Loc.Get("prefs.file.defaultEncoding.label")), 0, 0);
        panel.Controls.Add(_defaultEncodingCombo, 1, 0);
        panel.Controls.Add(new Label
        {
            Text = Loc.Get("prefs.file.defaultEncodingHint"),
            AutoSize = true,
            MaximumSize = new Size(hintMaximumWidth, 0),
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Regular),
            Margin = new Padding(0, this.ScaleForDpi(2), 0, 0),
        }, 1, 1);
        panel.Controls.Add(NewInlineLabel(Loc.Get("prefs.file.newLineStyle.label")), 0, 3);
        panel.Controls.Add(_newLineStyleCombo, 1, 3);
        panel.Controls.Add(new Label
        {
            Text = Loc.Get("prefs.file.newLineHint"),
            AutoSize = true,
            MaximumSize = new Size(hintMaximumWidth, 0),
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Regular),
            Margin = new Padding(0, this.ScaleForDpi(2), 0, 0),
        }, 1, 4);
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
        var visual = CreateSettingsPage();
        visual.Controls.Add(NewLabel(Loc.Get("prefs.editor.displaySettings.label")), 0, 0);
        visual.Controls.Add(BuildVisualDisplayPanel(), 1, 0);
        visual.Controls.Add(Gap(), 0, 1);
        visual.Controls.Add(Gap(), 1, 1);
        visual.Controls.Add(NewLabel(Loc.Get("prefs.editor.featureSettings.label")), 0, 2);
        visual.Controls.Add(BuildVisualFeaturePanel(), 1, 2);
        visual.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 3);
        visual.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 3);

        var source = CreateSettingsPage();
        source.Controls.Add(NewLabel(Loc.Get("prefs.editor.displaySettings.label")), 0, 0);
        source.Controls.Add(BuildSourcePanel(), 1, 0);
        source.Controls.Add(Gap(), 0, 1);
        source.Controls.Add(Gap(), 1, 1);
        source.Controls.Add(NewLabel(Loc.Get("prefs.editor.featureSettings.label")), 0, 2);
        source.Controls.Add(BuildSourceFeaturePanel(), 1, 2);
        source.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 3);
        source.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 3);

        var markdown = CreateSettingsPage();
        markdown.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        markdown.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        markdown.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        markdown.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        markdown.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        markdown.Controls.Add(NewLabel(Loc.Get("prefs.editor.markdown.symbolEscape.label")), 0, 0);
        markdown.Controls.Add(BuildMarkdownAsteriskPanel(), 1, 0);
        markdown.Controls.Add(Gap(), 0, 1);
        markdown.Controls.Add(Gap(), 1, 1);
        markdown.Controls.Add(NewLabel(Loc.Get("prefs.editor.markdown.syntax.label")), 0, 2);
        markdown.Controls.Add(BuildMarkdownSyntaxPanel(), 1, 2);
        markdown.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 3);
        markdown.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 3);
        var markdownHint = new Label
        {
            Text = Loc.Get("prefs.editor.markdown.normalizationHint"),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 8F, FontStyle.Regular),
            Margin = new Padding(this.ScaleForDpi(6), 0, 0, this.ScaleForDpi(4)),
        };
        markdown.Controls.Add(markdownHint, 0, 4);
        markdown.SetColumnSpan(markdownHint, 2);

        return BuildSegmentedPage(
            [Loc.Get("prefs.editor.segment.visual"), Loc.Get("prefs.editor.segment.source"),
                Loc.Get("prefs.editor.segment.markdown")],
            [visual, source, markdown]);
    }

    private Control BuildVisualDisplayPanel()
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
        panel.Controls.Add(Gap(), 0, 5);

        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.cjkLang.label"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 6);
        panel.Controls.Add(_cjkLanguageTagCombo, 1, 6);
        panel.Controls.Add(Gap(), 0, 7);

        panel.Controls.Add(_visualCjkAutoSpacingCheck, 0, 8);
        panel.SetColumnSpan(_visualCjkAutoSpacingCheck, 2);
        panel.Controls.Add(Gap(), 0, 9);
        panel.Controls.Add(_showCodeHighlightCheck, 0, 10);
        panel.SetColumnSpan(_showCodeHighlightCheck, 2);

        return panel;
    }

    private Control BuildVisualFeaturePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.Controls.Add(_exitBlockOnEmptyEnterCheck, 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_useShiftEnterHardBreakCheck, 0, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(_showParagraphBlockHandleCheck, 0, 4);
        return panel;
    }

    private Control BuildMarkdownAsteriskPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.Controls.Add(_autoConvertUnsafeEmphasisOnNormalizeCheck, 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_escapeLiteralSymbolsCheck, 0, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(_escapeMarkdownLiteralSymbolsCheck, 0, 4);
        return panel;
    }

    private Control BuildSourceFeaturePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.Controls.Add(NewPreferenceLabel(Loc.Get("prefs.editor.sourceUnsafeEmphasis.label")), 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_unsafeEmphasisPromptRadio, 0, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(_unsafeEmphasisLiteralRadio, 0, 4);
        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(_unsafeEmphasisAutoConvertRadio, 0, 6);
        return panel;
    }

    private Control BuildMarkdownSyntaxPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, this.ScaleForDpi(180)));

        panel.Controls.Add(NewPreferenceLabel(Loc.Get("prefs.editor.markdown.codeFence.label")), 0, 0);
        _markdownCodeFenceCombo.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_markdownCodeFenceCombo, 1, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(Gap(), 1, 1);
        panel.Controls.Add(NewPreferenceLabel(Loc.Get("prefs.editor.markdown.emphasis.label")), 0, 2);
        _markdownEmphasisMarkerCombo.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_markdownEmphasisMarkerCombo, 1, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(Gap(), 1, 3);
        panel.Controls.Add(NewPreferenceLabel(Loc.Get("prefs.editor.markdown.bullet.label")), 0, 4);
        _markdownBulletMarkerCombo.Anchor = AnchorStyles.Left;
        panel.Controls.Add(_markdownBulletMarkerCombo, 1, 4);
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

        panel.Controls.Add(new Label { Text = Loc.Get("prefs.editor.sourceIndentWidth"),
                                        AutoSize = true,
                                        TextAlign = ContentAlignment.MiddleLeft,
                                        Padding = new Padding(0, 5, 0, 0), }, 0, 0);
        panel.Controls.Add(_sourceIndentWidth, 1, 0);
        panel.Controls.Add(Gap(), 0, 1);

        panel.Controls.Add(new Label
        {
            Text = Loc.Get("prefs.editor.fontSettings.fontSize"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 0),
        }, 0, 2);
        panel.Controls.Add(_sourceFontSizeNumeric, 1, 2);
        panel.Controls.Add(Gap(), 0, 3);

        panel.Controls.Add(new Label
        {
            Text = Loc.Get("prefs.editor.fontSettings.cjkFont"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 0),
        }, 0, 4);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 4)!, 2);
        panel.Controls.Add(BuildSourceFontRow(_cjkFontTextBox, _selectCjkFontButton), 0, 5);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 5)!, 2);
        panel.Controls.Add(Gap(), 0, 6);

        panel.Controls.Add(new Label
        {
            Text = Loc.Get("prefs.editor.fontSettings.westernFont"),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 0),
        }, 0, 7);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 7)!, 2);
        panel.Controls.Add(BuildSourceFontRow(_westernFontTextBox, _selectWesternFontButton), 0, 8);
        panel.SetColumnSpan(panel.GetControlFromPosition(0, 8)!, 2);

        return panel;
    }

    private Control BuildSourceFontRow(TextBox textBox, Button selectButton)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            MaximumSize = new Size(this.ScaleForDpi(300), 0),
        };
        row.Controls.Add(textBox);
        row.Controls.Add(selectButton);
        return row;
    }

    private void SelectSourceFont(TextBox targetTextBox)
    {
        using var dialog = new FontDialog
        {
            FontMustExist = true,
            AllowScriptChange = false,
            ShowColor = false,
            ShowEffects = false,
        };
        if (!string.IsNullOrWhiteSpace(targetTextBox.Text))
        {
            try { dialog.Font = new Font(targetTextBox.Text, (float)_sourceFontSizeNumeric.Value); }
            catch { }
        }
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        targetTextBox.Text = dialog.Font.Name;
        dialog.Font.Dispose();
    }

    private Control BuildAppearanceTab()
    {
        var theme = CreateSettingsPage();
        theme.Controls.Add(NewLabel(Loc.Get("prefs.appearance.style.label")), 0, 0);
        theme.Controls.Add(_styleCombo, 1, 0);
        theme.Controls.Add(Gap(), 0, 1);
        theme.Controls.Add(Gap(), 1, 1);
        theme.Controls.Add(NewLabel(Loc.Get("prefs.appearance.colorScheme.label")), 0, 2);
        theme.Controls.Add(BuildColorThemePanel(), 1, 2);
        theme.Controls.Add(Gap(), 0, 3);
        theme.Controls.Add(Gap(), 1, 3);
        var themeFolderRow = new FlowLayoutPanel { AutoSize = true };
        themeFolderRow.Controls.Add(_addThemeButton);
        themeFolderRow.Controls.Add(_openThemeFolderButton);
        theme.Controls.Add(NewLabel(Loc.Get("prefs.appearance.themeFiles.label")), 0, 4);
        theme.Controls.Add(themeFolderRow, 1, 4);
        theme.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 5);
        theme.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 5);

        var window = CreateSettingsPage();
        window.Controls.Add(NewLabel(Loc.Get("prefs.appearance.window.label")), 0, 0);
        window.Controls.Add(BuildWindowPanel(), 1, 0);
        window.Controls.Add(Gap(), 0, 1);
        window.Controls.Add(Gap(), 1, 1);
        window.Controls.Add(NewLabel(Loc.Get("prefs.editor.zoom.label")), 0, 2);
        window.Controls.Add(BuildZoomPanel(), 1, 2);
        window.Controls.Add(Gap(), 0, 3);
        window.Controls.Add(Gap(), 1, 3);
        window.Controls.Add(NewLabel(Loc.Get("prefs.appearance.menuStyle.label")), 0, 4);
        window.Controls.Add(BuildMenuStylePanel(), 1, 4);
        window.Controls.Add(Gap(), 0, 5);
        window.Controls.Add(Gap(), 1, 5);
        window.Controls.Add(NewLabel(Loc.Get("prefs.appearance.statusBar.label")), 0, 6);
        window.Controls.Add(_customizeStatusBarButton, 1, 6);
        window.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 7);
        window.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 7);

        return BuildSegmentedPage(
            [Loc.Get("prefs.appearance.segment.theme"), Loc.Get("prefs.appearance.segment.window")],
            [theme, window]);
    }

    private Control BuildColorThemePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.Controls.Add(_themeCombo, 0, 0);
        panel.SetColumnSpan(_themeCombo, 2);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_followSystemCheck, 0, 2);
        panel.SetColumnSpan(_followSystemCheck, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(NewInlineLabel(Loc.Get("prefs.appearance.defaultLightTheme")), 0, 4);
        panel.Controls.Add(_defaultLightThemeCombo, 1, 4);
        panel.Controls.Add(Gap(), 0, 5);
        panel.Controls.Add(NewInlineLabel(Loc.Get("prefs.appearance.defaultDarkTheme")), 0, 6);
        panel.Controls.Add(_defaultDarkThemeCombo, 1, 6);
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
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_showMenuKeyboardShortcutsCheck, 0, 2);
        panel.Controls.Add(Gap(), 0, 3);
        panel.Controls.Add(_showMenuMnemonicsCheck, 0, 4);
        return panel;
    }

    private void OpenStatusBarSettings()
    {
        using var dialog = new StatusBarSettingsDialog(_statusBarSettings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _statusBarSettings = dialog.Settings;
        _settings.Appearance.StatusBar = _statusBarSettings.Clone();
    }

    private Control BuildGeneralTab()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(22), this.ScaleForDpi(14), this.ScaleForDpi(7)),
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

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.update.label")), 0, 10);
        panel.Controls.Add(BuildUpdatePanel(), 1, 10);

        panel.Controls.Add(Gap(), 0, 11);
        panel.Controls.Add(Gap(), 1, 11);

        panel.Controls.Add(NewLabel(Loc.Get("prefs.general.advanced.label")), 0, 12);
        panel.Controls.Add(BuildAdvancedPanel(), 1, 12);

        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 13);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 13);

        return BuildFramedPage(panel);
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

    private Control BuildUpdatePanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        panel.Controls.Add(_checkForUpdatesButton, 0, 0);
        panel.Controls.Add(Gap(), 0, 1);
        panel.Controls.Add(_autoCheckForUpdatesCheck, 0, 2);
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
            Padding = new Padding(this.ScaleForDpi(17), this.ScaleForDpi(22), this.ScaleForDpi(14), this.ScaleForDpi(7)),
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

        return BuildFramedPage(panel);
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
        _contentPanel.Padding = new Padding(0, this.ScaleForDpi(10), 0, 0);
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
        _defaultEncodingCombo.SelectedIndex = Math.Max(0, FindEncodingIndex(file.DefaultEncoding));
        _newLineStyleCombo.SelectedIndex = file.NewLineStyle == NewLineStyle.Lf ? 0 : 1;

        var editor = _settings.Editor;
        _visualLineHeight.Value = (decimal)editor.VisualLineHeight;
        _visualFontSize.Value = editor.VisualFontSize;
        _visualMaxWidth.Value = editor.VisualMaxContentWidth;
        _sourceFontSize = editor.SourceFontSize;
        _cjkFontFamily = editor.SourceCjkFontFamily;
        _westernFontFamily = editor.SourceFontFamily;
        _sourceFontSizeNumeric.Value = editor.SourceFontSize;
        _cjkFontTextBox.Text = editor.SourceCjkFontFamily;
        _westernFontTextBox.Text = editor.SourceFontFamily;
        _cjkLanguageTagCombo.SelectedIndex = (int)editor.CjkLanguageTag;
        _visualCjkAutoSpacingCheck.Checked = editor.VisualCjkAutoSpacing;
        _autoConvertUnsafeEmphasisOnNormalizeCheck.Checked = editor.AutoConvertUnsafeEmphasis;
        _escapeLiteralSymbolsCheck.Checked = editor.EscapeLiteralSymbols;
        _escapeMarkdownLiteralSymbolsCheck.Checked = editor.EscapeMarkdownLiteralSymbols;
        _exitBlockOnEmptyEnterCheck.Checked = editor.ExitBlockOnEmptyEnter;
        _useShiftEnterHardBreakCheck.Checked = editor.UseShiftEnterHardBreak;
        _unsafeEmphasisPromptRadio.Checked = editor.UnsafeEmphasisPreference is null;
        _unsafeEmphasisLiteralRadio.Checked = editor.UnsafeEmphasisPreference == "literal";
        _unsafeEmphasisAutoConvertRadio.Checked = editor.UnsafeEmphasisPreference == "html";
        _markdownCodeFenceCombo.SelectedIndex = editor.MarkdownCodeFence == "tilde" ? 1 : 0;
        _markdownEmphasisMarkerCombo.SelectedIndex = editor.MarkdownEmphasisMarker == "underscore" ? 1 : 0;
        _markdownBulletMarkerCombo.SelectedIndex = editor.MarkdownBulletMarker switch
        {
            "asterisk" => 1,
            "plus" => 2,
            _ => 0,
        };
        _sourceIndentWidth.Value = editor.SourceIndentWidth;
        _showParagraphBlockHandleCheck.Checked = editor.ShowParagraphBlockHandle;

        var appearance = _settings.Appearance;
        _restoreZoomCheck.Checked = appearance.RestoreZoomOnOpen;
        _ctrlWheelZoomCheck.Checked = appearance.CtrlWheelZoom;
        _topMostCheck.Checked = appearance.TopMostWindow;
        _autoHideScrollbarsCheck.Checked = appearance.AutoHideScrollbars;
        _showCodeHighlightCheck.Checked = appearance.ShowCodeHighlight;
        _followSystemCheck.Checked = appearance.FollowSystemColorMode;
        _themeCombo.Enabled = !appearance.FollowSystemColorMode;
        _menuStyleCombo.SelectedIndex = (int)appearance.MenuBarStyle;
        _showMenuKeyboardShortcutsCheck.Checked = appearance.ShowMenuKeyboardShortcuts;
        _showMenuMnemonicsCheck.Checked = appearance.ShowMenuMnemonics;
        _statusBarSettings = appearance.StatusBar.Clone();

        _associateMarkdownCheck.Checked = _settings.General.AssociateMarkdownFiles;
        _associateTextCheck.Checked = _settings.General.AssociateTextFiles;
        _languageCombo.SelectedIndex = LanguageToIndex(_settings.General.UiLanguage);
        _autoCheckForUpdatesCheck.Checked = _settings.General.AutoCheckForUpdates;

        var image = _settings.Image;
        _clipboardImageCombo.SelectedIndex = ToComboIndex((int)image.ClipboardHandling, _clipboardImageCombo);
        _fileImageCombo.SelectedIndex = ToComboIndex((int)image.FileHandling, _fileImageCombo);
        _defaultDirectoryTextBox.Text = image.DefaultDirectory;
        _useRelativePathsCheck.Checked = image.UseRelativePaths;
        _prefixRelativeWithDotSlashCheck.Checked = image.PrefixRelativeWithDotSlash;

        _styleCombo.SelectedIndex = FindStyleIndex(_settings.MarkdownStyle);
        _themeCombo.SelectedIndex = FindThemeIndex(_settings.ColorTheme);
        _defaultLightThemeCombo.SelectedIndex = FindThemeIndex(_lightThemeOptions, appearance.DefaultLightThemeId);
        _defaultDarkThemeCombo.SelectedIndex = FindThemeIndex(_darkThemeOptions, appearance.DefaultDarkThemeId);
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

    private static int FindThemeIndex((string Id, string DisplayName)[] themes, string themeId)
    {
        for (var index = 0; index < themes.Length; index++)
        {
            if (string.Equals(themes[index].Id, themeId, StringComparison.Ordinal))
                return index;
        }
        return 0;
    }

    private static int FindEncodingIndex(string encodingId)
    {
        var encoding = DocumentEncodingPolicy.FromId(encodingId);
        for (var index = 0; index < DocumentEncodingPolicy.All.Count; index++)
        {
            if (string.Equals(DocumentEncodingPolicy.All[index].Id, encoding.Id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private void CommitControlsToSettings()
    {
        var file = _settings.File;
        file.StartupAction = (StartupAction)_startupAction.SelectedIndex;
        file.AutoSaveEnabled = _autoSaveCheck.Checked;
        file.SaveOnDocumentSwitch = _saveOnSwitchCheck.Checked;
        file.SnapshotIntervalSeconds = (int)_snapshotInterval.Value;
        file.RecordRecentFiles = _recordRecentFilesCheck.Checked;
        file.RecordRecentFolders = _recordRecentFoldersCheck.Checked;
        if (_defaultEncodingCombo.SelectedIndex >= 0
            && _defaultEncodingCombo.SelectedIndex < DocumentEncodingPolicy.All.Count)
        {
            file.DefaultEncoding = DocumentEncodingPolicy.All[_defaultEncodingCombo.SelectedIndex].Id;
        }
        file.NewLineStyle = _newLineStyleCombo.SelectedIndex == 0 ? NewLineStyle.Lf : NewLineStyle.Crlf;

        var editor = _settings.Editor;
        editor.VisualLineHeight = (float)_visualLineHeight.Value;
        editor.VisualFontSize = (int)_visualFontSize.Value;
        editor.VisualMaxContentWidth = (int)_visualMaxWidth.Value;
        editor.SourceFontSize = (int)_sourceFontSizeNumeric.Value;
        editor.SourceCjkFontFamily = _cjkFontTextBox.Text;
        editor.SourceFontFamily = _westernFontTextBox.Text;
        if (_cjkLanguageTagCombo.SelectedIndex >= 0)
            editor.CjkLanguageTag = (CjkLanguageTag)_cjkLanguageTagCombo.SelectedIndex;
        editor.VisualCjkAutoSpacing = _visualCjkAutoSpacingCheck.Checked;
        editor.AutoConvertUnsafeEmphasis = _autoConvertUnsafeEmphasisOnNormalizeCheck.Checked;
        editor.EscapeLiteralSymbols = _escapeLiteralSymbolsCheck.Checked;
        editor.EscapeMarkdownLiteralSymbols = _escapeMarkdownLiteralSymbolsCheck.Checked;
        editor.ExitBlockOnEmptyEnter = _exitBlockOnEmptyEnterCheck.Checked;
        editor.UseShiftEnterHardBreak = _useShiftEnterHardBreakCheck.Checked;
        editor.UnsafeEmphasisPreference = _unsafeEmphasisAutoConvertRadio.Checked
            ? "html"
            : _unsafeEmphasisLiteralRadio.Checked ? "literal" : null;
        editor.MarkdownCodeFence = _markdownCodeFenceCombo.SelectedIndex == 1 ? "tilde" : "backtick";
        editor.MarkdownEmphasisMarker = _markdownEmphasisMarkerCombo.SelectedIndex == 1
            ? "underscore"
            : "asterisk";
        editor.MarkdownBulletMarker = _markdownBulletMarkerCombo.SelectedIndex switch
        {
            1 => "asterisk",
            2 => "plus",
            _ => "dash",
        };
        editor.SourceIndentWidth = (int)_sourceIndentWidth.Value;
        editor.ShowParagraphBlockHandle = _showParagraphBlockHandleCheck.Checked;

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
        appearance.ShowCodeHighlight = _showCodeHighlightCheck.Checked;
        appearance.FollowSystemColorMode = _followSystemCheck.Checked;
        if (_defaultLightThemeCombo.SelectedIndex >= 0
            && _defaultLightThemeCombo.SelectedIndex < _lightThemeOptions.Length)
        {
            appearance.DefaultLightThemeId = _lightThemeOptions[_defaultLightThemeCombo.SelectedIndex].Id;
        }
        if (_defaultDarkThemeCombo.SelectedIndex >= 0
            && _defaultDarkThemeCombo.SelectedIndex < _darkThemeOptions.Length)
        {
            appearance.DefaultDarkThemeId = _darkThemeOptions[_defaultDarkThemeCombo.SelectedIndex].Id;
        }
        if (_menuStyleCombo.SelectedIndex >= 0)
            appearance.MenuBarStyle = (MenuBarStyle)_menuStyleCombo.SelectedIndex;
        appearance.ShowMenuKeyboardShortcuts = _showMenuKeyboardShortcutsCheck.Checked;
        appearance.ShowMenuMnemonics = _showMenuMnemonicsCheck.Checked;
        appearance.StatusBar = _statusBarSettings.Clone();

        _settings.General.AssociateMarkdownFiles = _associateMarkdownCheck.Checked;
        _settings.General.AssociateTextFiles = _associateTextCheck.Checked;
        _settings.General.UiLanguage = IndexToLanguage(_languageCombo.SelectedIndex);
        _settings.General.AutoCheckForUpdates = _autoCheckForUpdatesCheck.Checked;

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

    }

    private static AppSettings CloneSettings(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.CreateDefaults();
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.MainWindow = source.MainWindow;
        target.Workspace = source.Workspace;
        target.File = source.File;
        target.Editor = source.Editor;
        target.Appearance = source.Appearance;
        target.General = source.General;
        target.Image = source.Image;
        target.Export = source.Export;
        target.Shortcut = source.Shortcut;
        target.MarkdownStyle = source.MarkdownStyle;
        target.ColorTheme = source.ColorTheme;
    }

    private void ApplySegmentedTabColors()
    {
        var colors = ColorThemeService.GetActiveColors();
        foreach (var container in _optionContainers)
            container.ApplyThemeColors(colors);
    }

    private void ApplyOkButtonThemeColors()
    {
        var colors = ColorThemeService.GetActiveColors();
        if (colors.TryGetValue("theme-light", out var background))
            _okButton.BackColor = background;
        if (colors.TryGetValue("text-selected", out var foreground))
            _okButton.ForeColor = foreground;
        _okButton.UseVisualStyleBackColor = false;
    }

    private void ApplyDpiSizes()
    {
        _contentPanel.Padding = new Padding(
            this.ScaleForDpi(14), this.ScaleForDpi(17), this.ScaleForDpi(14), 0);

        var comboW = this.ScaleForDpi(210);
        _startupAction.Width = comboW;
        var textFormatComboW = this.ScaleForDpi(156);
        _defaultEncodingCombo.Width = textFormatComboW;
        _newLineStyleCombo.Width = textFormatComboW;
        _cjkLanguageTagCombo.Width = this.ScaleForDpi(100);
        _styleCombo.Width = comboW;
        _themeCombo.Width = comboW;
        _defaultLightThemeCombo.Width = this.ScaleForDpi(150);
        _defaultDarkThemeCombo.Width = this.ScaleForDpi(150);
        _menuStyleCombo.Width = comboW;
        _languageCombo.Width = comboW;
        _clipboardImageCombo.Width = comboW;
        _fileImageCombo.Width = comboW;
        _defaultDirectoryTextBox.Width = comboW;

        var nudW = this.ScaleForDpi(52);
        _snapshotInterval.Width = nudW;
        _visualLineHeight.Width = nudW;
        _visualFontSize.Width = nudW;
        _visualMaxWidth.Width = nudW;
        _sourceIndentWidth.Width = nudW;
        _sourceFontSizeNumeric.Width = nudW;
        var markdownComboW = this.ScaleForDpi(180);
        _markdownCodeFenceCombo.Width = markdownComboW;
        _markdownEmphasisMarkerCombo.Width = markdownComboW;
        _markdownBulletMarkerCombo.Width = markdownComboW;
        var sourceFontTextBoxWidth = this.ScaleForDpi(150);
        _cjkFontTextBox.Width = sourceFontTextBoxWidth;
        _westernFontTextBox.Width = sourceFontTextBoxWidth;

        var buttonWidth = this.ScaleForDpi(72);
        var buttonHeight = this.ScaleForDpi(23);
        _okButton.Size = new Size(buttonWidth, buttonHeight);
        _cancelButton.Size = new Size(buttonWidth, buttonHeight);

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

    private Label NewInlineLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.ControlText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9F, FontStyle.Regular),
            Padding = new Padding(0, this.ScaleForDpi(4), 0, 0),
        };
    }

    private Label NewPreferenceLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.ControlText,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 9F, FontStyle.Regular),
            Padding = new Padding(0, this.ScaleForDpi(5), 0, 0),
        };
    }

    private Control Gap()
    {
        return new Panel { Height = this.ScaleGapForDpi(), Width = 0, Dock = DockStyle.None };
    }

}
