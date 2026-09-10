# MarkLeaf for VS Code

[简体中文](../README.md) | [日本語](./README.ja.md) | [繁體中文](./README.zh-TW.md)

Read and visually edit Markdown in VS Code using MarkLeaf's Tiptap/ProseMirror core, KaTeX, Mermaid, and typography. The TypeScript extension uses VS Code APIs and Webviews, without adding a separate Electron dependency or desktop shell.

Version **0.2.6** provides 36 settings and 67 configurable formatting actions, including a format painter, tables, footnotes, math and diagrams, image resources, find and replace, an outline, and reading preferences. PDF, HTML, PNG/JPG images, preview, and printing are now available.

Toolbar menus close on an outside click, Escape, switching menus, or leaving the extension's focus. Math and Mermaid source panels have opaque backgrounds that follow light/dark themes. They always open below the corresponding content and scroll out of view with the document; they do not flip to the other side or dock at the bottom of the window. The math symbol panel adapts its layout to the available space.

Project and extension READMEs are available in four languages. UI translation coverage is described under “Typography and preferences” below.


## Export, preview, and printing

Use the toolbar's export menu or **MarkLeaf: Export Document / PDF / HTML / Image / Print** in the Command Palette. Export also works in reading mode. **Export with Last Settings** reuses the last successfully saved options and asks for a new destination.

- **PDF**: paginated output with selectable text; A4/A5/Letter/Legal, landscape, four margins, plain-text headers/footers, and page numbers. Tables and headings can stay with adjacent content when they fit; oversized tables may still span pages.
- **HTML**: a complete standalone document with typography CSS, rendered math and Mermaid, KaTeX fonts, and embedded images. Creating the file does not require a browser. Web hyperlinks retain their original destinations.
- **PNG/JPG**: content width, 1–3× resolution, maximum output height per image, and JPEG quality. Long documents split into numbered files such as `name-01.png`; replacing existing split files requires confirmation.
- **Preview**: a separate Chrome/Edge window shows the generated PDF/images, or the full HTML document. Close it to return, or cancel from VS Code's progress notification.
- **Print**: Chrome/Edge opens its print dialog for printer selection and confirmation. Paper, margins, and background graphics can be adjusted there. Closing the dialog does not prove that a printer completed the job. Repeating page furniture uses modern Chromium paged-media support; use a current stable browser.

Exports default to `minimal` (Web · Minimal) and a light palette, with all nine built-in typography styles and nineteen palettes available. Document colors still follow VS Code. Export options do not change editor preferences; the configured body font and CJK spacing are reused, while custom CSS files and editor zoom are excluded.

PDF, images, preview, and printing use an installed **Chrome/Edge**. The extension does not bundle or download a browser. If discovery fails, select its executable, or set **MarkLeaf: Export Browser Path** (`markleaf.exportBrowserPath`, machine scope). On macOS, for example: `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`. Each operation uses a temporary profile that is released on completion or cancellation, without attaching to an existing browser session.

Export waits for synchronization and snapshots the VS Code `TextDocument`; it does not save or edit Markdown. Conflicts, unreadable images, and math/diagram parse errors stop export and retain the original error. Local, workspace, and HTTP/HTTPS images are embedded from their document-relative URIs, with a 16 MiB limit per image. Network images must be reachable. Body fonts remain system fonts; only KaTeX fonts are embedded. Operations are cancellable, and already-written split files are listed in the result.

A remote extension host can export files using its filesystem and installed browser. Interactive preview and printing require a local desktop VS Code window; for SSH/WSL, export the file and open it locally. Windows/Linux/remote hosts and physical printing still need acceptance in those environments.


## Install and open

Build `artifacts/markleaf-vscode-0.2.6.vsix`, then choose **Install from VSIX…** in VS Code, or run this from the repository root:

```bash
code --install-extension artifacts/markleaf-vscode-0.2.6.vsix
```

After enabling the extension, newly opened `.md` and `.markdown` files use MarkLeaf by default. For an existing source tab, choose **Reopen Editor With… → MarkLeaf** or press **Ctrl/Cmd+Shift+V**. Explorer also offers **MarkLeaf: Open Markdown**.

After upgrading the VSIX, save your documents and run **Developer: Reload Window** so the window reloads the extension and its configuration declarations. If the settings page still shows only old options, or reports that `markleaf.shortcuts` is not registered, reload the entire window before recording shortcuts. Restarting only the extension host may leave the old settings registry in place.

MarkLeaf declares a default custom editor. If Markdown already has another default editor, or multiple installed extensions claim it, use **Reopen Editor With… → Configure default editor for… → MarkLeaf**. Select **Text Editor** there to return to source by default. Existing VS Code editor associations take precedence.

### Git diff views

On VS Code 1.120 and later, Git comparisons for `.md` and `.markdown`, including working tree changes, staged changes, and commit history, default to the native source diff editor with line numbers, addition/deletion highlights, and change navigation. Ordinary files still default to MarkLeaf for reading and editing.

The extension supplies defaults through `workbench.diffEditorAssociations` without writing user settings. Existing user or workspace diff associations take precedence. If you explicitly selected another Markdown diff editor, set `*.md` and `*.markdown` to `default` in that setting. After upgrading, run **Developer: Reload Window**, then close and reopen existing diff tabs. Older VS Code versions do not support this automatic association; use **Reopen Editor With… → Text Editor** on the diff tab, or upgrade VS Code.

## Editing and reading

| Feature | Entry points and behavior |
| --- | --- |
| Text and paragraphs | The toolbar and “格式…” (Format) menu offer bold, italic, underline, strikethrough, highlight, inline code, clear formatting, H1–H6, heading-level changes, insert before/after, duplicate/delete paragraph, list indentation, and five GitHub alert types. Search menus by action name. |
| Format painter and block handle | Select formatted text, activate the painter, then drag over the target. It exits after one application; Escape cancels. The handle beside a paragraph opens its actions and stays outside editable content. |
| Tables | Insert with row/column counts, add/delete rows and columns, align columns, set/clear captions, and delete tables. Available actions depend on the cursor position. |
| Math and Mermaid | Insert inline/block math, number formulas, change formula type, or delete. Double-click a formula or diagram to open its shared source control, including math symbol assistance. Render Mermaid code, edit diagrams, and render again. |
| Footnotes and metadata | Insert footnotes, rename labels, return to references, clear references, delete definitions, and display/insert YAML Front Matter. A label cannot reuse an existing definition or reference. |
| Code | Choose a block language, copy code, exit a block, and toggle syntax highlighting. |
| Clipboard | “编辑” (Edit) offers copying as Markdown, plain text, or HTML, and pasting plain text. Normal copy supplies selected text and HTML; plain-text paste keeps Markdown/HTML markers literal. |
| Find and replace | Search rendered text with case/whole-word matching, previous/next results, replace one, or replace all. Find works in reading mode; replacement requires editing mode. |
| Outline and reading | “视图” (View) provides an H1–H6 outline, paragraph focus, typewriter scrolling, zoom, typography, and colors. The status bar shows character counts, selected characters, current block, and position; hover for more statistics. |

The **阅读 / 编辑** (Read/Edit) button switches modes. Opening a document, searching, changing mode, or adjusting display settings does not write Markdown back. Reading mode allows copy, find, links, and outline navigation; document changes require editing mode.

Menus, image pickers, and prompts retain the selection from when they opened. If the document changes while waiting, select the target again. If an image was already saved but the selection expired, the message retains its resource path so it can be inserted elsewhere.

Click links directly in reading mode; use **Ctrl+click** (**Cmd+click** on macOS) in editing mode. Web URLs, local files, and document headings are supported.

## Image resources

- The toolbar's “图片” (Image) button selects one or more files. “编辑 → 图片地址” inserts a relative path or web URL. Supported types are PNG, JPEG, GIF, WebP, SVG, BMP, and AVIF.
- Pasting or dropping images saves them to an `assets` directory beside the document and inserts Markdown references. Each image is limited to 16 MiB. Unsaved documents use the first workspace folder; without a workspace, save the document first.
- File selection copies images into the resource directory by default. Set `markleaf.fileImageHandling` to reference the original file instead. The resource directory, relative paths, and `./` prefix are configurable. Generated names avoid overwriting existing resources.
- Select an image and use “编辑 → 当前图片操作” to replace it, change its caption, rotate, resize, or save a copy. Reading mode only allows saving a copy.
- Display uses VS Code resource URLs while Markdown retains the original paths. Images outside the workspace load from their containing directory. Remote files use VS Code's file system API.

## Shortcuts and document synchronization

| Action | Windows / Linux | macOS |
| --- | --- | --- |
| Switch source/rendered view | `Ctrl+Shift+V` | `Cmd+Shift+V` |
| Find in rendered text | `Ctrl+F` | `Cmd+F` |
| Replace in rendered text | `Ctrl+H` | `Cmd+Alt+F` |
| Paste plain text | `Ctrl+Alt+V` | `Cmd+Alt+V` |
| Save | `Ctrl+S` | `Cmd+S` |
| Undo / redo | `Ctrl+Z` / `Ctrl+Shift+Z` | `Cmd+Z` / `Cmd+Shift+Z` |

In the find bar, Enter/Shift+Enter selects the next/previous result and Escape closes it. Ctrl+mouse wheel changes document zoom. Search for MarkLeaf in VS Code **Keyboard Shortcuts** to change view switching, find, replace, or plain-text paste. View switching has separate source and rendered-view rules; update both. Rendered-view shortcuts require MarkLeaf focus, and plain-text paste does not take over find or formula-source inputs. Heading and text-format defaults come from the shared editor and can be changed in MarkLeaf's shortcut settings. Source switching takes over the native Markdown preview shortcut in Markdown editing contexts; native preview commands remain available in the command palette.

### Formatting shortcuts

Open **视图 → 快捷键…** (View → Shortcuts), run **MarkLeaf: Configure Shortcuts / 快捷键设置**, or use the typography/settings picker. Search all formatting actions, record a key, clear it, or restore its default. “保存键位” (Save binding) applies the change immediately and updates menu/toolbar hints.

Inline math, block math, and Mermaid are unbound by default. Bind an insert-math action to insert at the rendered editor's cursor and open the existing source/symbol assistant. Actions needing parameters, such as table dimensions or footnotes, continue to use their existing prompts.

**Markleaf: Shortcuts** in VS Code settings uses the same configuration. In your settings JSON, for example:

```json
"markleaf.shortcuts": {
  "insertMathInline": "Mod+Alt+M",
  "insertMathBlock": "Mod+Alt+Shift+M",
  "toggleBold": "Mod+Alt+B",
  "toggleHighlight": ""
}
```

- `Mod` means Cmd on macOS and Ctrl on Windows/Linux. Explicit `Ctrl`, `Cmd`, `Alt`, and `Shift` are supported. Letters and digits use physical key positions, avoiding symbols generated by macOS Option.
- Bindings are single combinations using A–Z, 0–9, or F1–F24. Chords and punctuation keys are not supported here. Letters/digits require Ctrl, Cmd, or Alt so normal typing remains available.
- Omitted actions keep their defaults; an empty string disables a binding. Rebinding or clearing removes the old formatting shortcut in MarkLeaf's rendered editor. “使用默认” (Use default) still requires Save and does not alter other actions.
- Duplicate MarkLeaf bindings and reserved save/undo/source-switch keys are rejected with an explanation. Invalid or duplicate entries written directly in JSON are shown in the panel and disabled instead of silently retaining old bindings.
- The panel displays the save scope: existing folder configuration takes precedence, then workspace, otherwise user settings. Inherited bindings for other actions are not copied into that scope. Settings never rewrite Markdown.
- Custom formatting keys only apply in the active MarkLeaf rendered editing area. They do not run in reading mode, find fields, math/diagram source inputs, or the native source editor. IME composition and AltGraph retain their behavior.

All 67 formatting actions are also independent VS Code commands, such as `markleaf.insertMathInline`, `markleaf.insertMathBlock`, and `markleaf.setHeading1`. For chords or advanced rules, use VS Code Keyboard Shortcuts with the condition `activeWebviewPanelId == markleaf.editor && markleaf.focus == document`. Clear the old MarkLeaf binding for the same action to avoid keeping both entries.

MarkLeaf menus display only effective `markleaf.shortcuts` settings. The public VS Code extension API does not expose the resolved global keymap, so menu hints cannot reflect separate `keybindings.json` overrides or detect every shortcut intercepted by other extensions or the system. Check those conflicts in VS Code Keyboard Shortcuts.

VS Code's `TextDocument` owns the document, saving, dirty state, and edit history. Source and side-by-side views share it. Continuous input is undone by synchronization batch; IME input synchronizes after composition commits. “已同步到 VS Code” means edits reached the text buffer; the VS Code dirty indicator determines whether they were saved to disk.

The Webview submits one edit at a time and waits for its version acknowledgement before sending accumulated changes. Its own acknowledgements do not rebuild the editor. If another view changes the file, stale edits cannot overwrite the new version. Unsynchronized content stays in MarkLeaf and can be opened as a draft using “将未同步内容打开为草稿”. Once VS Code opens the draft, MarkLeaf reloads the current file; merge the draft in source view.

Each file supports one MarkLeaf visual view alongside native source. Hidden views retain context to protect unfinished input. Normal save waits for synchronization; VS Code may skip save participants during shutdown. Confirm synchronization before closing, and open conflicting content as a draft first.

## Typography and preferences

Use the typography/settings entry in “视图” (View), or search VS Code settings for `@ext:markleaf.markleaf`. Settings use existing user, workspace, or workspace-folder scopes.

| Group | Main settings |
| --- | --- |
| Typography and colors | `typography`: nine styles—sans, serif, print, print-double, latex, retro-print, minimal, magazine, notebook. `colorTheme`: follows VS Code by default, or selects one of nineteen built-in palettes. Rendered documents also default to `minimal` (Web · Minimal), preserving type hierarchy, whitespace, and table detail. Existing user/workspace typography choices take precedence. |
| Fonts and width | `fontSize` (default 16), `fontFamily`, `lineHeight`, `maxWidth` (default 820), `ignoreMaxWidth`, `zoom`. Fonts used by a style must be installed locally. |
| Code and CJK | `showCodeHighlight`, `sourceFontFamily` / `sourceFontSize` for math/diagram source, `cjkLanguage`, `cjkAutoSpacing`. Visual spacing does not insert spaces into source text. |
| View | `defaultMode`, `showOutline`, `focusMode`, `typewriterMode`, `showStatusBar`, `showBlockHandle`, `autoHideScrollbars`, `ctrlWheelZoom`. |
| Markdown editing | `codeFence`, `emphasisMarker`, `bulletMarker`, `exitBlockOnEmptyEnter`, `useShiftEnterHardBreak`, emphasis conversion, and literal-symbol escaping. Serialization preferences apply after actual edits. |
| Images | `imageDirectory`, `fileImageHandling`, `useRelativeImagePaths`, `prefixImagePathsWithDot`. |
| Formatting shortcuts | `shortcuts`, shared with the recorder under “视图 → 快捷键…”. Applies only to MarkLeaf's rendered editing area in VS Code. |
| Custom CSS | `customCss`, a CSS path relative to the document or an absolute path. Loaded only in trusted workspaces; reload the editor after changing the CSS file. |

Normal Markdown source fonts, indentation, and shortcuts use native VS Code settings. Files/folders, recent files, tabs, autosave, recovery, encoding, line endings, window layout, and extension updates also use VS Code's capabilities. Shared formula and diagram controls select existing Simplified Chinese, Traditional Chinese, English, or Japanese translations from the VS Code language. Export menus, dialogs, commands, and status messages use Simplified Chinese, Traditional Chinese, English, or Japanese according to VS Code. Existing editing menus and formatting-command titles still use Chinese; other existing command titles remain English. Four-language documentation does not imply a fully translated UI.

## Fidelity and delivery limits

Opening and reading do not change the source file. Actual visual edits parse and serialize Markdown and may normalize list markers, blank lines, emphasis, or other formatting. The document's line-ending type and existing final newline are preserved, but byte-for-byte fidelity is not promised. Supported content includes paragraphs, headings, lists/tasks, quotes, code, tables, images, links, YAML Front Matter, footnotes, math, Mermaid, and alerts. Use source editing for MDX, custom plugin syntax, or arbitrary raw HTML that must be preserved.

Underline can read bounded `++text++`: its inner edges cannot be whitespace, and its outer edges cannot touch Latin letters, digits, underscores, or other plus signs. `C++`, `C++17`, and ordinary increment operators stay literal. Visual edits serialize underline as `<u>…</u>`, avoiding ambiguity with literal plus signs or selections inside words while retaining repeated spaces and other inline formatting. Native applications and the extension share this rule.

The current target is desktop VS Code; there is no browser-extension entry point. Remote URIs use VS Code file system/resource APIs, but Windows, Linux, SSH/WSL, and real clipboard, drag/drop, IME, and keyboard interactions still need acceptance in their respective environments. Builds, Vitest, and mocked-host tests do not replace installed-extension UI acceptance.

See the [feature mapping (Simplified Chinese)](./feature-parity.md).

## Development and build

From the repository root, use Node.js 22.12+ and the project's specified pnpm version:

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir apps/vscode install --frozen-lockfile
pnpm build:vscode
pnpm test:editor-web
pnpm package:vscode
```

Use an existing VS Code installation for development:

```bash
code --new-window --extensionDevelopmentPath="$PWD/apps/vscode" path/to/document.md
```

`packages/editor-web/src/vscode.ts` is the extension frontend; `apps/vscode/src/extension.ts` adapts VS Code documents. Windows/macOS retain `main.ts` and the native host protocol. Extension output goes to `apps/vscode/dist`; native frontend output remains in `packages/editor-web/dist`.
