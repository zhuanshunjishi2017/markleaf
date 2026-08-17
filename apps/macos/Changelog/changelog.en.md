# MarkLeaf Changelog

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
