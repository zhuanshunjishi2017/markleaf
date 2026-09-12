# MarkLeaf

[简体中文](../README.md) | [日本語](./README.ja.md) | [繁體中文](./README.zh-TW.md)

MarkLeaf is a lightweight Markdown visual editor available as native Windows/macOS applications and a VS Code extension. Its clean interface and typography provide a focused space for thinking, reading, and writing.

The project was originally initiated and created by [fcz](https://github.com/zhuanshunjishi2017), with the first version supporting only Windows. Later, [Na Bian](https://github.com/Na-Bian) provided support for macOS. **Currently, the Windows version and the macOS version are updated together.**

## Screenshots

![screenshot-light](./assets/screenshot-light.png)

## Features

### Rich Typographic Styles and Color Schemes

#### **Typographic Styles**

The application comes with a variety of built‑in typographic styles, for example:

- **Web**: Suitable for screen reading and daily editing, this is the mainstream Markdown rendering style of most editors, pursuing efficiency and a clear experience. **(The typography used in the upper‑left window in the screenshot above)**
- **Print**: Uses serif fonts and bold faces commonly found in print, with justified paragraphs, first‑line indentation, centered headings, and generous margins, simulating the layout of modern books. Suitable for long‑form writing and reading.
- **LaTeX**: Uses CMU fonts and a layout similar to a LaTeX `document` class, with quotes and callout boxes in tcolorbox style, as close as possible to LaTeX rendering. **(The typography used in the middle window in the screenshot above)**
- **Letterpress**: Uses the Huiwen, Chaohua, and Jinghua Lao Song typefaces created by Mr. Terry Wang, creating a more retro style on top of the print layout. **(The typography used in the upper‑right window in the screenshot above)**

> [!NOTE]
> Some themes may require specific fonts for a better experience. You can visit the following pages, or download the related font packages directly from [Releases](https://github.com/zhuanshunjishi2017/markleaf/releases) and install them on your computer.
>
> - [Computer Modern series fonts](https://www.fontsquirrel.com/fonts/computer-modern) (default LaTeX typography font)
> - [Huiwen, Chaohua series fonts and Jinghua Lao Song](https://huozi.cool/) (Letterpress typography, free fonts created by 特里王)
> - [Lxgw WenKai](https://github.com/lxgw/LxgwWenKai) (excellent open‑source Chinese font created by Lxgw)

#### Color Schemes

The application supports **multiple color themes**, including dark and light, and **implements Win32 menu support for dark mode.** Below are some of the preset color theme effects.

> [!TIP]
> Since both color schemes and rendering themes **are CSS styles**, you can **fully customize** color themes and typographic styles. In the future, we will also release a related theme editor.

### Markdown Syntax Support

Based on the **Tiptap/ProseMirror** editor core, supports full CommonMark and GitHub Flavored Markdown syntax.

**Additionally supports:**

- LaTeX math formulas (rendered by KaTeX)
- Mermaid diagrams (rendered as SVG)
- Footnote definitions, references, and navigation
- GitHub‑style alert blocks, including notes, tips, warnings, etc., displayed differently under each theme.
- <strong>(Custom syntax)</strong> Captions for images and tables.

### Excellent Export Quality

The native Windows app supports PDF, HTML, PNG/JPG long images, and printing; the native macOS app supports PDF, HTML, PNG/JPG long images, and system printing. The VS Code extension now provides PDF, standalone HTML, PNG/JPG images, preview, and browser printing, with MarkLeaf Minimal typography by default. PDF offers paper, orientation, margins, headers/footers, and page numbers; long images split into numbered files. Except for HTML file export, the extension uses `puppeteer-core` with an installed Chrome/Edge, without bundling or downloading a browser.

### Minimal yet Complete Operation Logic and Features

The workspace, window, and built-in source-mode features below primarily describe the native applications. The VS Code extension uses VS Code Explorer, windows, tabs, and source editing; its own entry points are documented in the extension guide below.

- <strong>Workspace Management</strong>: Supports opening a folder as a workspace, viewing files in tree or list view, and searching documents by name/content. Automatic refresh preserves expanded folders, selection, and scrolling when files change. Currently lists only `.md` and `.txt` text files and folders; PDF, image, and archive contents are not read.
- <strong>Multiple Windows and Tabs</strong>: Supports opening multiple window instances, and can also open a document in a new window. Additionally, the application supports opening multiple tabs in the same window, with each tab managing its document content independently.
- <strong>Source Mode</strong>: Built‑in CodeMirror 6 source editing mode, allowing instant switching between visual editing and Markdown source.
- <strong>Automatic Conversion of Non‑compliant Markdown Markers</strong>: For common issues in Chinese Markdown text where <strong>literal asterisks are exposed</strong>, the application can detect asterisk markers that do not conform to CommonMark specifications and convert them to HTML tags.
- <strong>Menus and Shortcuts</strong>: All paragraph and format operations can be performed via context menus and the paragraph format button. The application also has a complete custom shortcut system.
- <strong>LaTeX Formula Input Assistance</strong>: No need to memorize LaTeX source; covers most mathematical symbols, allowing complex LaTeX formulas to be entered by clicking.
- <strong>Focused Reading and Writing</strong>: Provides focus mode, typewriter mode, minimal mode, and full‑screen editing.
- <strong>Chinese‑Western Typography Friendly</strong>: You can choose the preferred Chinese character glyph standard (Simplified Chinese/Traditional Chinese/Japanese/Korean) in preferences. At the same time, <strong>the application automatically adds spacing between Chinese and Western characters without manual insertion of spaces.</strong>

## Platform Support

| Platform | Technology | Code directory |
| --- | --- | --- |
| Windows | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS | Swift + AppKit + WKWebView | `apps/macos` |
| VS Code extension | TypeScript + CustomTextEditorProvider + Webview | `apps/vscode` |

All three hosts share the editor core and typography. The VS Code extension uses the existing VS Code runtime without adding a separate Electron dependency or desktop shell. It supports reading and visual editing, a format painter, tables, footnotes, math and Mermaid, image paste and drop, find and replace, an outline, and reading preferences. VS Code manages saving, undo/redo, tabs, and native Markdown source editing, including switching views and opening source alongside the rendered document.

Extension 0.2.8 provides 36 settings and 67 configurable formatting actions. Math and diagram selection, subsequent-click expansion, and viewport positioning follow the shared kernel. Shortcut configuration affects MarkLeaf in VS Code only; native application shortcuts are independent. Project and extension READMEs are available in Simplified Chinese, English, Japanese, and Traditional Chinese. The extension UI is only partly localized; see the [extension guide](../apps/vscode/docs/README.en.md) and [feature mapping (Simplified Chinese)](../apps/vscode/docs/feature-parity.md) for details.

All three products follow the Windows copy/paste rules: Copy HTML produces source text; ordinary text paste and Paste Plain Text parse Markdown in visual editing, while source editing keeps literal text. Paste feedback distinguishes success, formatting conversion, plain-text fallback, and failure, retaining fallback reasons.

Rendered documents also default to `minimal` (Web · Minimal), preserving type hierarchy, whitespace, and table detail. Existing user/workspace typography choices take precedence.

On VS Code 1.120+, Markdown Git comparisons default to the native source diff editor with addition/deletion highlights. Ordinary files still default to MarkLeaf.

## Project Structure

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows native app (C# WinForms)
│   │   ├── MarkLeaf/             #   Main program (.NET 10 + WebView2)
│   │   └── setup/                #   Inno Setup installer
│   ├── vscode/                   # VS Code Markdown reading and editing extension (TypeScript)
│   │   ├── src/                  #   Extension process: document adapter, commands, export
│   │   └── webview/              #   Webview adapter: extension protocol, settings, shortcuts
│   └── macos/                    # macOS native app (Swift AppKit + WKWebView)
│       ├── Sources/MarkLeaf/     #   Main program
│       ├── Changelog/            #   Product changelog (four languages)
│       └── script/               #   Build / release scripts
├── packages/
│   ├── editor-core/              # Shared document and rendering kernel (TypeScript)
│   ├── editor-web/               # Webview adapter for macOS / Windows
│   └── styles/                   # Shared typography / theme styles (shared by all three hosts)
├── MarkLeaf.slnx                 # Windows solution
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # Shared application icons
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## Technical Architecture

```text
packages/editor-core (shared document and rendering kernel) + packages/styles (shared typography)
├── packages/editor-web    → apps/windows → WinForms + WebView2 → native message bridge
│                          → apps/macos   → AppKit + WKWebView  → native message bridge
└── apps/vscode/webview    → apps/vscode  → VS Code Webview    → TextDocument / WorkspaceEdit

The kernel owns document rules, rendering, editing, export and typography; it carries no
host transport and no host UI. Host differences are expressed through capability
injection in host-capabilities, never through host type checks inside the kernel.
The rendering stack (Tiptap / ProseMirror / CodeMirror / Mermaid / KaTeX) is owned
solely by the kernel; adapters never redeclare it, so no second copy can be resolved.

Windows/macOS: editor-web/src/main.ts, with built-in CodeMirror 6 source mode
VS Code: apps/vscode/webview/src/vscode.ts, using the native VS Code Markdown source editor
```

`build:editor-web` and `build:products` build the shared renderer distribution for all Webviews. The DOM-free `document-kernel.cjs` is built and loaded only by the VS Code Node.js extension; macOS and Windows retain their native document encoding, file, and recovery implementations. See [kernel boundaries](./kernel-boundaries.md).

## Build and Run

### VS Code Extension

Run from the repository root with Node.js 22.12+ and the project's pinned pnpm 11.9.0 through Corepack:

```bash
corepack pnpm install:vscode
corepack pnpm package:vscode
```

The package is written to `artifacts/markleaf-vscode-0.2.8.vsix`.

Install the generated package with **Install from VSIX…** in VS Code. Newly opened `.md` and `.markdown` files use MarkLeaf by default. For existing source tabs, use **Reopen Editor With… → MarkLeaf**; change an existing association with **Configure default editor for…**. **Ctrl+Shift+V** (**Cmd+Shift+V** on macOS) switches between native source and rendered views.

After upgrading, save your documents and run **Developer: Reload Window**. Missing settings or an unregistered `markleaf.shortcuts` setting require a full window reload. Open **视图 → 快捷键…** (View → Shortcuts) to record formatting shortcuts. Reading does not write to the file; visual edits may normalize Markdown formatting. See the [extension guide and fidelity limits](../apps/vscode/docs/README.en.md).

### Web Frontend Editor

Shared by macOS and Windows. `editor-web` depends on `editor-core` through `link:`,
so the kernel's own dependencies must be installed first for the symlink to work:

```bash
corepack pnpm install:editor-web
corepack pnpm build:editor-web
```

The frontend is written to `packages/editor-web/dist`; its `kernel/` directory contains the unchanged shared renderer distribution.

Run tests separately: `corepack pnpm test:editor-web` for the native host protocol, and `corepack pnpm test:editor-core` for shared kernel contracts.

### Windows

```powershell
corepack pnpm install:editor-web
corepack pnpm build:editor-web
dotnet restore .\apps\windows\MarkLeaf\MarkLeaf.csproj
dotnet build .\apps\windows\MarkLeaf\MarkLeaf.csproj --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# One‑shot (build frontend + compile + package .app + launch)
./apps/macos/script/build_and_run.sh

# Release packaging (.app / ZIP / branded DMG / checksums)
./apps/macos/script/release/package.sh
```

The default output directory is `apps/macos/dist/release`, containing the arm64 app ZIP, DMG, dSYM and checksums. After `corepack pnpm build:products`, set `MARKLEAF_USE_BUILT_EDITOR_WEB=1` when packaging to reuse those kernel and frontend artifacts.

## License

The application is licensed under the MIT License. See [LICENSE](../LICENSE).
