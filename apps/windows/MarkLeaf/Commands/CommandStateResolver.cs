namespace MarkLeaf.Commands;

public static class CommandStateResolver
{
    public static CommandState Resolve(AppCommand command, CommandContext context)
    {
        if (EditorCommandBindings.TryGetAction(command, out var identifier))
        {
            if (!context.DocumentAvailable || !context.EditorReady
                || context.EditorActions is null || !context.EditorActions.TryGetValue(identifier, out var action))
                return new(false);
            return action;
        }

        return command switch
        {
            AppCommand.Exit or AppCommand.ShowShortcuts or AppCommand.ShowPreferences
                or AppCommand.ShowAbout or AppCommand.ShowChangelog or AppCommand.ShowWelcome
                or AppCommand.LearnMarkdown
                or AppCommand.CheckForUpdates
                or AppCommand.OpenThemeFolder or AppCommand.AddTheme
                or AppCommand.OpenFolder
                or AppCommand.NewWindow or AppCommand.OpenDocumentInNewWindow or AppCommand.InstallOptionalFonts
                or AppCommand.ShowColorThemes
                or AppCommand.ShowTypographyStyles
                or AppCommand.ShowThemeSettings
                or AppCommand.RecoverUnsavedFiles
                or AppCommand.FollowSystemColorMode => new(true),
            AppCommand.ShowCodeHighlight => new(context.EditorReady, context.ShowCodeHighlight),

            AppCommand.ToggleSidebar => new(!context.FocusMode, context.SidebarVisible),
            AppCommand.ToggleFocusMode => new(true, context.FocusMode),
            AppCommand.ToggleEditorFullScreen => new(true, context.EditorFullScreen),
            AppCommand.ViewTree => new(true, !context.ListViewActive),
            AppCommand.ViewList => new(true, context.ListViewActive),
            AppCommand.UseIndependentOutlineSidebar => new(true, context.IndependentOutlineSidebar),
            AppCommand.ShowStatusBar => new(true, context.StatusBarVisible),
            AppCommand.SwitchToWorkspace => new(
                !context.FocusMode && context.SidebarVisible && !context.IndependentOutlineSidebar,
                !context.OutlineActive),
            AppCommand.SwitchToOutline => new(
                !context.FocusMode && context.SidebarVisible && !context.IndependentOutlineSidebar,
                context.OutlineActive),

            AppCommand.SaveDocument or AppCommand.SaveDocumentAs =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.ExportWithLastSettings or AppCommand.ExportPdf or AppCommand.ExportHtml
                or AppCommand.ExportImage or AppCommand.Print =>
                new(context.DocumentAvailable && context.EditorReady),
            AppCommand.ZoomIn or AppCommand.ZoomOut or AppCommand.ZoomReset => new(context.EditorReady),
            AppCommand.RestartEditor => new(context.EditorReady),

            AppCommand.SwitchDocumentTab1 or AppCommand.SwitchDocumentTab2
                or AppCommand.SwitchDocumentTab3 or AppCommand.SwitchDocumentTab4
                or AppCommand.SwitchDocumentTab5 or AppCommand.SwitchDocumentTab6
                or AppCommand.SwitchDocumentTab7 or AppCommand.SwitchDocumentTab8
                or AppCommand.SwitchDocumentTab9 or AppCommand.CloseCurrentDocumentTab
                or AppCommand.CloseOtherDocumentTabs or AppCommand.SwitchToNextDocumentTab
                or AppCommand.LocateCurrentDocumentInWorkspace
                => new(context.DocumentAvailable),

            AppCommand.NewDocument or AppCommand.OpenDocument or AppCommand.OpenDocumentReadOnly => new(true),
            _ => new(context.EditorReady),
        };
    }

}
