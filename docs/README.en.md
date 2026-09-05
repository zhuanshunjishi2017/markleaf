# MarkLeaf

[简体中文](../README.md) | [日本語](./README.ja.md) | [繁體中文](./README.zh-TW.md)

This is a native lightweight Markdown visual editor, pursuing a clean interface and typography, providing a space dedicated to thinking, reading, and writing.

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

Currently supports export to PDF/HTML/long images. PDF allows custom paper size, margins, headers/footers, etc. It also supports advanced settings such as preventing table page breaks. After exporting to PDF using themes like Print/LaTeX, the result is well‑suited for reading and printing, and can meet some academic writing layout requirements.

### Minimal yet Complete Operation Logic and Features

- <strong>Workspace Management</strong>: Supports opening a folder as a workspace, viewing files in tree or list view, and searching documents by name/content.
- <strong>Multiple Windows and Tabs</strong>: Supports opening multiple window instances, and can also open a document in a new window. Additionally, the application supports opening multiple tabs in the same window, with each tab managing its document content independently.
- <strong>Source Mode</strong>: Built‑in CodeMirror 6 source editing mode, allowing instant switching between visual editing and Markdown source.
- <strong>Automatic Conversion of Non‑compliant Markdown Markers</strong>: For common issues in Chinese Markdown text where <strong>literal asterisks are exposed</strong>, the application can detect asterisk markers that do not conform to CommonMark specifications and convert them to HTML tags.
- <strong>Menus and Shortcuts</strong>: All paragraph and format operations can be performed via context menus and the paragraph format button. The application also has a complete custom shortcut system.
- <strong>LaTeX Formula Input Assistance</strong>: No need to memorize LaTeX source; covers most mathematical symbols, allowing complex LaTeX formulas to be entered by clicking.
- <strong>Focused Reading and Writing</strong>: Provides focus mode, typewriter mode, minimal mode, and full‑screen editing.
- <strong>Chinese‑Western Typography Friendly</strong>: You can choose the preferred Chinese character glyph standard (Simplified Chinese/Traditional Chinese/Japanese/Korean) in preferences. At the same time, <strong>the application automatically adds spacing between Chinese and Western characters without manual insertion of spaces.</strong>

## Platform Support


| Platform | Technology Used                  | Code Directory          |
| -------- | -------------------------------- | ----------------------- |
| Windows  | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS    | Swift + AppKit + WKWebView       | `apps/macos`            |


Both platforms share the same editor frontend and styles, while the application uses platform‑native methods to implement them.

## Project Structure

```text
markleaf/
├── apps/
│   ├── windows/                  # Windows native app (C# WinForms)
│   │   ├── MarkLeaf/             #   Main program (.NET 10 + WebView2)
│   │   └── setup/                #   Inno Setup installer
│   └── macos/                    # macOS native app (Swift AppKit + WKWebView)
│       ├── Sources/MarkLeaf/     #   Main program
│       ├── Changelog/            #   Product changelog (four languages)
│       └── script/               #   Build / release scripts
├── packages/
│   ├── editor-web/               # Shared editor frontend (Tiptap/ProseMirror + CodeMirror 6)
│   └── styles/                   # Shared typography / theme styles (print styles, shared by both platforms)
├── MarkLeaf.slnx                 # Windows solution
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # Shared application icons
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## Technical Architecture

```text
apps/windows (C# WinForms)        apps/macos (Swift AppKit)
  Main Window / Menu / Workspace / Export       Main Window / Menu / Workspace / Export
        │                                  │
        ├── packages/editor-web ───────────┤   Shared frontend (Tiptap + CodeMirror)
        ├── packages/styles ───────────────┤   Shared print styles
        └── WebView2 / WKWebView ──────────┘   native-shim.js message bridge
```

## Build and Run

### Web Frontend Editor

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # output to packages/editor-web/dist
pnpm --dir packages/editor-web test        # vitest frontend tests
```

### Windows

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
# One‑shot (build frontend + compile + package .app + launch)
./apps/macos/script/build_and_run.sh

# Release packaging (.app / ZIP / branded DMG / checksums)
./apps/macos/script/release/package.sh
```

## License

The application is licensed under the MIT License. See [LICENSE](../LICENSE).