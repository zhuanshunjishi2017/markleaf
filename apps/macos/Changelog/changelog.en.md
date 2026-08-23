# MarkLeaf Changelog

## 1.3.2 — 2026-08-23

- Fix resizing the window from its left edge unexpectedly revealing a hidden sidebar.
- Fix the first sidebar reveal after launching with the sidebar hidden flashing at full width before restarting its animation, and prevent status-bar buttons from unexpectedly taking keyboard focus.
- Fix Format Painter remaining armed without applying when selecting a complete line from right to left or selecting multiple consecutive paragraphs.

## 1.3.1 — 2026-08-22

- Add encoding settings: Preferences can set the default encoding for new files, while the status bar can change the current document encoding immediately and open files using the most suitable encoding.

- Fine-tune the Preferences window layout.
- Turn the Visual control in the status bar into a mode menu.

- Show the version in About using the version number plus build number format.
- Fix the About build number being fixed at 42: it now reads the running app bundle's real version metadata, while the build script uses the Git commit count by default.
- Keep the current document position in view while zooming.
- Position the About window above the center of the current screen, use a more compact layout, show the full GitHub link and copyright details, and refine the copyright color and spacing.

- Move Preferred Han Glyphs into Font Settings.
- Give Simplified Chinese, Traditional Chinese, English, and Japanese Preferences their own stable window widths; switching pages now changes height only, and long English and Japanese labels no longer get clipped.
- Fade auto-hidden scrollbars in and out smoothly, while automatically disabling the animation when the system Reduce Motion setting is enabled.
- Unify Format and Paragraph menu availability: disable Bold, Italic, Underline, and Strikethrough without a selection; allow inline code to be inserted from an input dialog; and apply paragraph commands consistently to the current paragraph or all paragraphs covered by the selection.
- Merge Find and Replace into one Find & Replace entry in both the menu bar and Keyboard Shortcuts, retaining Command-F as the default; replacement controls expand and collapse smoothly in the same window.

- Match the text fields in the block-math, inline-code, and footnote dialogs to the subtly rounded rectangular style used in Preferences, and tighten the gap between the block-math number label and its field; the footnote number supports Esc to revert the current edit and validates immediately when focus leaves the field.
- Fix selecting a rendered LaTeX formula also selecting the text before it.

- Fix Copy and Copy As remaining available when no text is selected.
- Fix text fields in the Find & Replace window not accepting system shortcuts.

- Fix a crash when choosing another folder from the sidebar after a workspace was already open.
- Fix the first sidebar reveal after a hidden-sidebar launch misplacing the empty-workspace message, hiding the Open Folder button, and using the wrong width.

- Fix opening or saving a document unexpectedly revealing a hidden sidebar.

## 1.3.0 — 2026-08-20

### New

- Show folder icons and nearby keyword context in search results; opening a result now exits search and expands and selects the target file in the workspace tree.
- Make the status bar customizable: independently show or hide the sidebar toggle, command status, character count, block type, cursor position, encoding, newline style, source/visual mode toggle, and zoom percentage.
- Make the character count clickable to open detailed document statistics (characters, selected characters, total characters, non-whitespace characters, CJK characters, western words, formulas, code lines, paragraphs, line and column).
- Expand the workspace context menu with New File, New Folder, Copy Path, Show in Finder, Open in New Window, Rename, Delete, Tree/List view switch, sorting, refresh, and close workspace.
- Show the containing folder and modification time in document list mode, with sorting by file name or modification time in ascending or descending order.
- Support dragging .md/.txt/.markdown files from Finder into the workspace, copying them to the target directory.
- Support pressing Return in the workspace tree to open a file or expand/collapse a folder.
- Complete image commands in the context menu and Format menu: Change Image, Resize (100%/75%/90%/50%), Rotate Clockwise, and Save Image As.
- Add Open Read-Only to the File menu.
- Add Clear Paragraph Formatting to the Paragraph menu.
- Add an Auto-Hide Scrollbars option to the Appearance preferences.

### Changed

- MarkLeaf no longer takes over Markdown/plain-text file associations on first launch; it becomes the default editor only after the user explicitly enables the option.
- Separate command feedback from document metrics in the status bar so they no longer overwrite each other; Temporary mode clears after 5 seconds.
- Rework the Custom Status Bar window into a compact layout with consistent alignment and spacing for headings, options, and the command-status display setting.
- Use one Export window for PDF and HTML; the File menu now has a single Export entry while Export with Last Settings remains available.
- Default exports to the active layout style and color theme, with Standard margins. A different theme can still be selected for one export, while Export with Last Settings retains the previously saved theme.
- Search cancellation now stops directory traversal and file reads.

### Fixes

- Fix search results occasionally failing to reveal the target file in the workspace tree.
- Fix the workspace context menu missing new/rename/delete/sort operations.
- Fix Finder drag-and-drop into the workspace being rejected.
- Fix document list mode missing folder/time information and sorting.
- Fix canceled searches continuing to read files and consuming disk and CPU.
- Fix the status bar missing encoding, newline, mode toggle, zoom, and customization.
- Fix excessive empty space, misaligned headings, and an overly wide label-to-popup gap in the Custom Status Bar window.
- Remove the duplicate legacy Export As submenu from the File menu.
- Fix the Export window ignoring the active theme, defaulting to a previously used dark theme, and showing Custom instead of Standard for default margins.
- Fix ⌘A Select All and ⌘C Copy shortcuts not working in read-only mode, and selection not clearing with Esc or on blur, leaving residual highlights.

## 1.2.6 — 2026-08-19

### New

- Add Export with Last Settings to the bottom of the export window, reusing the previous export settings directly.
- Header and footer custom text fields now use a rounded style and appear below the preset popup only when Custom is selected, with other options shifting down smoothly.
- Unify the clipboard commands in the Edit menu and context menus as Cut, Copy, Paste, Paste as Plain Text, and Copy As, automatically disabled based on selection, clipboard content, and read-only state.
- Add footnote support: `[^x]` renders as a superscript reference and `[^x]:` paragraphs as footnote definitions, exported with dedicated structure in HTML/PDF.
- Add Insert Footnote to the Paragraph menu: enter a number and text to insert a reference at the cursor and a definition at the end of the document.
- ⌃-click a footnote reference to jump to its definition, with a clear warning when the definition is missing.
- Add Reset Footnote Number to the footnote definition context menu to rename the label across references and definitions.

### Fixes

- Fix the sidebar collapse animation replaying when opening a new file while the sidebar is hidden.
- Fix dark-theme PDF export leaving the page margin area white while only the content area is dark.
- Fix the export preview flickering and the page-count label disappearing and reappearing on regeneration.

## 1.2.5 — 2026-08-19

### New

- Source mode supports undo and redo, with paste consistently handled as plain text.
- Add Paste as Plain Text (`⌘⇧V`) and unify the copy menu as Copy/Paste As.
- Detect unsafe CommonMark emphasis boundaries in source mode, with options to keep literal markers or convert them to HTML tags; the prompt supports “Don’t show again” and “Learn more”.
- Detect unsafe bold and italic boundaries when generating Markdown and convert them to HTML tags when necessary, preventing exposed asterisk source after reload.
- Restrict the native context menu in read-only documents and disable source-mode editing commands.
- Combine the HTML and PDF export windows, and add Export with Last Settings.
- Persist export settings; PDF export supports headers and footers with title, page, total-page, and custom placeholders.
- Add the light Saltlemon (`saltlemon`) color theme.

### Changes

- Remove the smallest adaptive margin preset and slightly reduce the left/right margins; export dialogs now default to the active style and color theme.
- Render PDF headers and footers with the selected layout style font at 0.875× body size, with 6 mm vertical spacing.
- Remove the body container max-width limit from PDF export so tables can expand to the printable margin width; manual margin edits switch to the custom preset.
- Keep MarkLeaf running in the Dock after the last editor window closes, and reopen an existing window or create a blank one when the Dock icon is clicked.

### Fixes

- Fix empty HTML/rich-text pastes and incorrect caret movement after plain-text pastes in source mode.
- Fix caret jumps when typing full-width symbols, letters, or digits in visual mode.
- Fix images becoming blank or losing their image data when copied and pasted again.
- Fix the theme not applying immediately after confirming the follow-system-color-mode preference.
- Fix front-end editing menus and editing commands remaining available in read-only mode.
- Fix PDF headers/footers ignoring the selected layout font and tables failing to fill the available printable width.
- Reduce the macOS Dock icon artwork safe area so MarkLeaf matches the visual size of neighboring applications.

## 1.2.4 — 2026-08-17

### New

- Add table and image captions: table captions appear above the table and image captions below the image, both centered; right-click the table or image to edit the caption, with inline bold, italic, and strikethrough formatting.
- Add numbering to block formulas (e.g., 1 or 1.1): set the number when editing a formula, and it renders right-aligned.

### Changes

- In the LaTeX layout style, `## *text*` (italic level-2 heading) is treated as an author/contact line and rendered as centered body text.

### Fixes

- Fix editor initialization failing and most menus being disabled when loading documents with multiple table captions.
- Fix table/image captions rendering with inconsistent color and size between the editor and exports.

## 1.2.3 — 2026-08-16

### New

- The export dialog now supports both PDF and HTML with a live preview: review the layout and adjust paper, orientation, margins (including custom margins), style, and color scheme before exporting.

### Fixes & Improvements

- Fix long formulas overflowing or being clipped in PDF export.
- Fix the app crashing when switching the interface language to English.

## 1.2.2 — 2026-08-15

### New

- Export PDF now saves directly to the chosen location without opening the system print panel.
- Add a Print… command (⌘P) that prints the current document through the system print panel with a forced light background and dark text.
- The Shortcuts window now supports custom shortcuts that apply immediately and persist.

### Fixes & Improvements

- Focus Mode now uses ⌘⇧F instead of F11 to avoid conflicting with Show Desktop.
- Fix the Shortcuts window opening with a collapsed, strip-shaped layout.
- Fix Clear in the Shortcuts window not removing the current shortcut.
- Fix the font settings window opening larger than its content.
- Fix bullet, ordered, and task lists in the Paragraph menu not applying formatting.
- Fix heading menu items showing a duplicated “级” character (e.g., “一级级标题”).
- Fix paragraph block handles not showing and format painter not working inside lists.
- Fix an app crash when clicking “Cancel” in the system print panel.
- Fix the overly wide gap between line numbers and text in source mode when the sidebar is closed.

## 1.2.0 — 2026-08-14

### New

- Add LaTeX math formulas: inline `$...$` and block `$$...$$`, rendered with KaTeX as you type.
- Add “Block Math” to the Paragraph menu and “Inline Math” to the Format menu; wrap the selection directly or prompt for LaTeX input.
- The formula context menu supports editing, inline/block conversion, and deletion.
- The context menu now adapts to context: source mode, tables, formulas, code blocks, and headings each show relevant actions.
- The changelog now opens in a new read-only window, so it never replaces the current document, cannot be edited by accident, and never triggers save prompts.
- Export HTML/PDF with math rendering (self-contained KaTeX CSS/fonts, woff2 only to keep size small).

### Fixes & Improvements

- Restore the visible I-beam caret in source mode.
- Plain-text (.txt) documents stay in source mode, and the View > Source Mode menu item is disabled for them.
- Fix the custom-table dialog flashing and closing from the Insert Table menu; align the row/column labels with their input fields.
- Slightly increase the sidebar search field height.
- Restore the recovery window’s Discard All / Discard Selected buttons to the default white button style.
- Export PDF through the system print panel, so you can fully adjust paper size, orientation, and margins before saving as PDF.

### Changes

- Print styles respect the max-width setting; PDF tables size to their content.

## 1.1.7 — 2026-08-13

- Search workspace Markdown/TXT files by name or content, and filter outline headings from the sidebar.
- Add a paragraph block handle with native block actions and insert-before/after commands.
- Align task-list checkboxes with the theme accent color and use Yu Mincho/Yu Gothic for Japanese printing.

## 1.1.6 — 2026-08-12

- Configure externally opened files to open in new windows or the current window, activating an already-open file instead of duplicating it.
- Prompt to save, discard/delete, or cancel when closing, replacing, or quitting with modified documents; auto-save only under the relevant setting and only after a successful write.
- Add a one-shot Format Painter to the Format menu and the editor context menu.
- Provide complete localized Markdown changelogs in four interface languages.
- Localize the recovery dialog completely.
- Align the Outline font size and gray selection background with the Workspace.
- Make the Outline selection appear on the first click.
- Preserve the active Preferences page and frame when changing language.

## 1.1.5 — 2026-08-12

- Add a “CJK-first font” preference for Simplified Chinese, Traditional Chinese, Japanese, and Korean glyphs.
- Add Focus Mode to the View menu: F11 enters, Esc exits.
- Merge editor font options into a dedicated Font Settings dialog.
- Add Source Han/Noto CJK font fallbacks to serif and sans-serif styles.
- Align the Outline font size and gray selection shadow with Workspace.
- Update the Morandi theme page background color.

## 1.1.4 — 2026-08-11

- Set separate default themes for light and dark modes.
- Drag to move items within the Workspace, or drag to Finder to copy.
- Restore sidebar expansion state and the active tab.
- Open files on mouse release to avoid conflicts with dragging.
- Refresh the native UI immediately after switching languages.

## 1.1.3 — 2026-08-10

- Fix editor scrollbars being too thick and ignoring the theme when auto-hide is off.
- Fix a white flash when the editor finishes loading in dark mode.
- Automatically clean up logs older than 7 days on quit.
- Add the What’s New menu (opens inside the editor window).
