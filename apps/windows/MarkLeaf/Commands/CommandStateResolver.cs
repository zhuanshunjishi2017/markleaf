using MarkLeaf.Documents;

namespace MarkLeaf.Commands;

public static class CommandStateResolver
{
    public static CommandState Resolve(AppCommand command, CommandContext context)
    {
        if (EditorCommandBindings.TryGetAction(command, out var identifier))
        {
            if ((context.SelectionScope is null or "mixed") && identifier is
                "addRowBefore" or "addRowAfter" or "deleteRow" or "addColumnBefore" or "addColumnAfter" or "deleteColumn"
                or "alignTableLeft" or "alignTableCenter" or "alignTableRight" or "deleteTable" or "setTableCaption"
                or "setCodeBlockLanguage" or "formatCodeBlock" or "exitCode") return new(false);
            if (!context.DocumentAvailable || !context.EditorReady
                || context.EditorActions is null || !context.EditorActions.TryGetValue(identifier, out var action))
                return new(false);
            // 段落专注/打字机模式不是文档格式，渲染器的 action 只给出可用性；
            // 勾选状态由宿主窗口维护的模式直接提供（与 macOS 视图菜单一致）。
            return command switch
            {
                AppCommand.ToggleEditorFocusMode => action with { IsChecked = context.EditorFocusMode },
                AppCommand.ToggleEditorTypewriterMode => action with { IsChecked = context.EditorTypewriterMode },
                _ => action,
            };
        }

        return command switch
        {
            AppCommand.Exit or AppCommand.ShowShortcuts or AppCommand.ShowPreferences
                or AppCommand.ShowAbout or AppCommand.ShowChangelog or AppCommand.ShowWelcome
                or AppCommand.LearnMarkdown or AppCommand.ShowCodeFormatter
                or AppCommand.CheckForUpdates
                or AppCommand.OpenThemeFolder or AppCommand.AddTheme
                or AppCommand.OpenFolder
                or AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow or AppCommand.InstallOptionalFonts
                or AppCommand.ShowColorThemes
                or AppCommand.ShowTypographyStyles
                or AppCommand.ShowThemeSettings
                or AppCommand.OpenOnlineHelp or AppCommand.OpenSampleAlert
                or AppCommand.OpenSampleYamlBasic or AppCommand.OpenSampleYamlAdvanced
                or AppCommand.RecoverUnsavedFiles
                => new(true),
            AppCommand.FollowSystemColorMode => new(true, context.FollowSystemColorMode),
            AppCommand.ShowCodeHighlight => new(true, context.ShowCodeHighlight),

            AppCommand.ToggleSidebar => new(true, context.SidebarVisible),
            AppCommand.ToggleFocusMode => new(true, context.FocusMode),
            AppCommand.ToggleEditorFullScreen => new(true, context.EditorFullScreen),
            AppCommand.ViewTree => new(true, !context.ListViewActive),
            AppCommand.ViewList => new(true, context.ListViewActive),
            AppCommand.UseIndependentOutlineSidebar => new(true, context.IndependentOutlineSidebar),
            AppCommand.ShowStatusBar => new(true, context.StatusBarVisible),
            AppCommand.SwitchToWorkspace => new(
                true,
                !context.OutlineActive),
            AppCommand.SwitchToOutline => new(
                true,
                context.OutlineActive),

            AppCommand.SaveDocument => new(DocumentMenuPolicy.CanSave(
                context.DocumentAvailable, context.EditorReady, context.DocumentDirty,
                context.DocumentHasPath, context.ReadOnlyMode)),
            AppCommand.SaveDocumentAs => new(DocumentMenuPolicy.CanSaveAs(
                context.DocumentAvailable, context.EditorReady)),
            AppCommand.SaveAllDocuments => new(context.EditorReady && context.SavableDocumentsAvailable),
            AppCommand.ReopenClosedTab => new(context.ClosedTabsAvailable),
            AppCommand.CopyCurrentFileContents => new(
                context.DocumentAvailable && context.EditorReady && context.DocumentHasContent),
            AppCommand.ToggleReadOnlyMode => new(
                context.DocumentAvailable && context.EditorReady && context.ReadOnlyCanBeToggled,
                context.ReadOnlyMode),
            AppCommand.ExportWithLastSettings or AppCommand.ExportPdf or AppCommand.ExportHtml
                or AppCommand.ExportImage or AppCommand.ExportDocument or AppCommand.Print =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.ZoomIn or AppCommand.ZoomOut or AppCommand.ZoomReset => new(context.EditorReady),
            AppCommand.RestartEditor => new(context.EditorReady),

            AppCommand.SwitchDocumentTab1 or AppCommand.SwitchDocumentTab2
                or AppCommand.SwitchDocumentTab3 or AppCommand.SwitchDocumentTab4
                or AppCommand.SwitchDocumentTab5 or AppCommand.SwitchDocumentTab6
                or AppCommand.SwitchDocumentTab7 or AppCommand.SwitchDocumentTab8
                or AppCommand.SwitchDocumentTab9 or AppCommand.CloseCurrentDocumentTab
                or AppCommand.CloseOtherDocumentTabs or AppCommand.SwitchToNextDocumentTab
                or AppCommand.SwitchToPreviousDocumentTab
                or AppCommand.LocateCurrentDocumentInWorkspace
                => new(context.DocumentAvailable),

            AppCommand.NewDocument or AppCommand.NewPlainText
                or AppCommand.OpenDocument or AppCommand.OpenDocumentReadOnly => new(true),
            AppCommand.CloseFolder => new(context.WorkspaceAvailable),
            _ => new(context.EditorReady),
        };
    }

}
