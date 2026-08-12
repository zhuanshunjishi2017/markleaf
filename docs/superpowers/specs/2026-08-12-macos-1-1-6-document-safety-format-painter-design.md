# macOS 1.1.6 Document Safety, Multi-Window, and Format Painter Design

## Goal

Release MarkLeaf for macOS 1.1.6 with safer document lifecycle behavior, configurable external-file window routing, a one-shot format painter, and fully localized Markdown changelogs.

The release must prevent silent loss of modified content, preserve MarkLeaf's existing multi-window architecture, and keep the editor window visually uncluttered.

## Scope

This release contains four coordinated areas:

1. Unsaved-document handling when closing a window, replacing a document, or quitting the application.
2. A preference controlling how files opened externally are routed to windows.
3. A one-shot format painter exposed through the Format menu and editor context menu.
4. Version 1.1.6 metadata and complete four-language Markdown changelogs.

The existing preference label `自动保存文件` remains unchanged. No persistent toolbar button or floating formatting toolbar will be added.

## External File Opening

### Preference

Add a File-page preference named `外部文件打开方式` with two choices:

- `始终在新窗口中打开` — default.
- `在当前窗口中打开`.

The setting applies to files sent to MarkLeaf from Finder, Finder's Open With command, the Dock, or another application. It does not change the explicit in-app `在新窗口中打开…` command.

The setting is persisted as a typed Codable value with a backward-compatible default of `newWindow`.

### Routing Rules

Before creating or reusing a window, MarkLeaf checks whether the standardized file URL is already open. If so, it activates that existing window and does not open a duplicate.

For `始终在新窗口中打开`:

- Each externally supplied file opens in its own new editor window.
- Cold-start bootstrap may still consume the first incoming file as the initial window's document, preserving the existing launch flow.

For `在当前窗口中打开`:

- The first incoming file targets the frontmost editor window.
- If there is no editor window, MarkLeaf creates one.
- Additional files in the same Finder open event each receive a new window.
- An unmodified current document is replaced directly.
- A modified current document first runs the document-disposition flow described below.

MarkLeaf must validate that an incoming file exists and is readable before disposing of the current document. A failed incoming-file read leaves the current document and window unchanged.

## Unified Document Disposition

### Architecture

Window close, current-window replacement, and application quit must use one shared asynchronous document-disposition coordinator. The coordinator receives:

- the document state: modified/unmodified and saved/untitled;
- the operation reason: close, replace, or quit;
- the relevant auto-save policy;
- callbacks for save, discard, cancel, and completion.

This avoids three independent implementations of save/discard logic.

### Unmodified Documents

Unmodified documents require no prompt:

- Close proceeds immediately.
- Replacement proceeds immediately.
- Quit proceeds immediately for that window.

### Modified Documents With a File URL

For window close and application quit:

- If `自动保存文件` is enabled, MarkLeaf saves the document and proceeds only after a successful write.
- If `自动保存文件` is disabled, MarkLeaf presents a native macOS sheet with `不保存`, `取消`, and `保存`.

For current-window replacement:

- If `切换文档时自动保存` is enabled, MarkLeaf saves the document and replaces it only after a successful write.
- If it is disabled, MarkLeaf presents the same native sheet.

The sheet copy is localized in Simplified Chinese, Traditional Chinese, English, and Japanese. It asks whether to save changes to the displayed filename and warns that unsaved changes will be lost.

### Modified Untitled Documents

Untitled documents can never be silently auto-saved. Close, replacement, and quit always present a TextEdit-style native sheet:

- Primary message: whether to keep the new untitled document.
- Actions: `删除`, `取消`, and `保存…`.
- `保存…` opens the standard `NSSavePanel` for the filename and location.
- Cancelling either the sheet or save panel cancels the original close, replacement, or quit operation.

The UI remains an AppKit sheet attached to the editor window. MarkLeaf will not implement the custom Word-style vertically stacked button panel.

### Save and Failure Semantics

- While a save or prompt is in progress, the originating close/replacement operation is blocked.
- A successful save resumes the original operation.
- `不保存` or `删除` resumes without writing.
- `取消` leaves the document and window unchanged.
- A snapshot failure or disk write failure leaves the document and window open, reports the existing localized save error, and does not continue the original operation.
- Re-entrant close requests while a disposition is active are ignored.

### Application Quit

Implement AppKit's deferred termination flow. On `⌘Q`, MarkLeaf processes editor windows sequentially:

1. Unmodified windows pass immediately.
2. Eligible saved documents are auto-saved according to `自动保存文件`.
3. Other modified documents show their appropriate sheet.
4. If any document cancels or fails to save, MarkLeaf cancels the entire quit.
5. MarkLeaf replies that termination is allowed only after every editor window has completed successfully.

Normal termination cleanup runs only after termination is approved.

## One-Shot Format Painter

### Entry Points

Add `格式刷` to:

- the native Format menu;
- the visual editor's native context menu.

Do not add a title-bar, toolbar, status-bar, or floating-selection button. The menu command is disabled in source mode and whenever the current source selection is not eligible.

The 1.1.6 release does not assign a default keyboard shortcut. A shortcut can be considered separately after the menu interaction has shipped without creating a conflict in the existing command map.

### Eligibility

The source and target must each be a non-empty text selection entirely within one text block.

The command is disabled or rejected for:

- a caret-only selection;
- images or node selections;
- selections crossing multiple paragraphs or structural blocks;
- tables, lists, or other structurally mixed selections;
- source mode.

### Captured Formatting

The first release captures only unambiguous text formatting:

- block type: paragraph or heading levels 1 through 6;
- inline marks: bold, italic, underline, strike, and inline code.

It does not capture or modify:

- text content;
- link URLs or link marks;
- image attributes;
- list structure;
- table structure or alignment;
- document styles, themes, fonts, or colors.

### State and Interaction

EditorWeb owns a per-editor one-shot state machine:

- `idle`: no captured formatting;
- `armed`: a valid source selection has been captured;
- `applied`: the next valid target selection has received the captured format, then state returns to `idle`;
- `cancelled`: state returns to `idle` without changing content.

Flow:

1. The user selects source text.
2. The user invokes `格式刷`.
3. EditorWeb captures the block type and supported inline marks and enters `armed` state.
4. The user makes the next target selection.
5. EditorWeb replaces the target's supported formatting with the captured formatting in one undoable transaction.
6. The format painter exits automatically.

Pressing `Esc`, switching documents, entering source mode, closing the window, or receiving an invalid target selection cancels the armed state. An invalid target does not modify content and does not remain armed.

Native AppKit code only routes the start command and reflects command availability. EditorWeb remains the source of truth for selection eligibility and the armed state.

## Localized Markdown Changelog

Replace `macos/Changelog/changelog.txt` with four complete Markdown files:

- `changelog.zh-Hans.md`
- `changelog.zh-Hant.md`
- `changelog.en.md`
- `changelog.ja.md`

Each file contains the complete available release history, including 1.1.6, 1.1.5, 1.1.4, and 1.1.3, translated naturally for that language.

The build copies the complete Changelog directory into the application bundle. When the user opens `更新内容`, MarkLeaf selects the file matching `displayLanguage`. Missing language resources fall back to Simplified Chinese. If neither the requested resource nor the fallback exists, MarkLeaf reports the existing localized `无法打开更新内容` error.

The selected Markdown file is copied to MarkLeaf's writable cache directory before being opened, preserving the current editable-resource safety model.

## Localization

All new user-facing strings must be present in Simplified Chinese, Traditional Chinese, English, and Japanese:

- external-file opening preference and its two choices;
- saved-document and untitled-document disposition sheets;
- `格式刷` and any armed/cancelled status copy exposed to the user;
- 1.1.6 changelog content.

No fifth display language is added.

## Release Metadata

Update the macOS release version to exactly `1.1.6` in:

- `CFBundleShortVersionString` generated by the build script;
- `CFBundleVersion` generated by the build script;
- the About-panel fallback version;
- all release-specific tests and verification expectations.

The 1.1.6 changelog entry must mention:

- configurable external-file window behavior and duplicate-window avoidance;
- safe save/discard/cancel handling for window close, document replacement, and application quit;
- the one-shot Format-menu/context-menu format painter;
- localized Markdown changelogs;
- the previously completed recovery-dialog localization, Outline/Workspace consistency, first-click Outline selection, and Preferences language-refresh context preservation, because those changes are included in the 1.1.6 application build.

## Testing Strategy

### Swift Tests

- AppSettings default value, round-trip encoding, and missing-key compatibility for external-file opening mode.
- Preferences construction, selected value, persistence, and four-language labels.
- Incoming-file routing for new-window/current-window modes, multiple URLs, no active window, duplicate open files, and cold-start bootstrap.
- Document-disposition policy for modified/unmodified, saved/untitled, close/replace/quit, auto-save enabled/disabled, save/discard/cancel, save-panel cancellation, and save failure.
- Deferred application termination: all windows succeed, one window cancels, and one save fails.
- Changelog language selection, Simplified Chinese fallback, missing-resource failure, `.md` cache target, and build resource inclusion.
- Bundle and About fallback version assertions for 1.1.6.

### EditorWeb Tests

- Source eligibility for same-text-block selections and rejection of empty, node, cross-block, list, table, and mixed selections.
- Capture of paragraph/heading type and supported inline marks.
- Applying captured formatting without changing text or links.
- Single application followed by automatic exit.
- `Esc`, source-mode change, document load, and invalid target cancellation.
- One undo operation restores the target's original formatting.

### Manual Acceptance

- Finder opens files into new windows by default.
- Current-window mode prompts or auto-saves according to the confirmed policies and never discards content before validating the incoming file.
- Closing saved and untitled modified documents shows the correct native sheet.
- `⌘Q` processes multiple modified windows and cancellation aborts quitting.
- Format painter is available from both menus, applies once, and cancels with `Esc`.
- The Help menu opens the correct full-history Markdown changelog after changing among all four interface languages.
- The built and installed application reports version 1.1.6.

## Out of Scope

- Continuous/double-click format painter mode.
- A persistent format-painter toolbar button.
- Word-style custom save dialogs.
- Full NSDocument migration, system document version browsing, or TextEdit-style automatic versioning.
- Copying links, images, lists, tables, theme, font family, font size, or color through the format painter.
- Adding another interface language.
