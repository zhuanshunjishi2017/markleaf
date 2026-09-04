# MarkLeaf

[简体中文](../README.md) | [日本語](./README.ja.md) | [繁體中文](./README.zh-TW.md)

MarkLeaf is a native lightweight visual Markdown editor pursuing a simple interface and typography, providing a space focused on thinking, reading, and writing.

The project was initiated and created by [fcz](https://github.com/zhuanshunjishi2017). The initial version supported Windows only; [Na Bian](https://github.com/Na-Bian) later provided macOS support. The **Windows and macOS versions are currently updated together**.

## Screenshots

![screenshot-light](./assets/screenshot-light.png)

## Features

### Rich typography styles and color schemes

#### **Typography styles**

The application includes many typography styles:

- **Web**: Suitable for screen reading and everyday editing, pursuing efficiency and clarity.
- **Print**: Uses serif and sans-serif typography common in print, justified paragraphs, first-line indentation, centered headings, and generous page whitespace. Suitable for long-form writing and reading.
- **LaTeX**: Uses CMU fonts and typography similar to a LaTeX document. Quotes and alert boxes follow the tcolorbox style as closely as possible. Suitable for academic writing.
- **Type Printing**: Uses the Huiwen and Chaohua font families created by Mr. Te Li Wang, together with KingHwa Old Song, to create a more retro style.

#### Color schemes

The application supports **multiple color themes**, including light and dark themes, each with its own style.

Color schemes and rendering themes are CSS styles, so **color themes and typography styles can be fully customized**. Every style supports export to **PDF/HTML/images**, with customizable paper size, margins, headers, and footers.

### Markdown syntax support

Based on the **Tiptap/ProseMirror** editor core, MarkLeaf supports complete CommonMark and GitHub Flavored Markdown syntax.

**Additional support**

- LaTeX mathematical formulas, rendered by KaTeX
- Mermaid diagrams, rendered as SVG
- Footnote definition references and navigation
- GitHub-style alert boxes, including note, tip, warning, and other types, with different appearances in each theme
- **(custom syntax)** image and table captions

### Minimal but complete interaction logic

- **Workspace management**: Open a folder as a workspace; view files in tree or list view; search documents by name or content.
- **Multiple windows and tabs**: Open multiple window instances and open documents in new windows. Multiple tabs can also be opened in one window, with each tab independently managing its document content.
- **Source mode**: Built-in CodeMirror 6 source editing mode enables instant switching between visual editing and Markdown source.
- **Menus and shortcuts**: Paragraph and formatting operations are available through context menus and the paragraph-format button. The application also provides complete shortcut customization.
- **Focused reading and writing**: Provides focus mode, typewriter mode, minimal mode, and full-screen editing.

## Platform support


| Platform | Technology stack                 | Code directory          |
| -------- | -------------------------------- | ----------------------- |
| Windows  | C# + .NET 10 WinForms + WebView2 | `apps/windows/MarkLeaf` |
| macOS    | Swift + AppKit + WKWebView       | `apps/macos`            |


Both platforms share the same editor frontend and styles, while each application uses native platform APIs.

## Project structure

```text
markleaf/
├── apps/
│   ├── windows/                  # Native Windows application (C# WinForms)
│   │   ├── MarkLeaf/             #   Main program (.NET 10 + WebView2)
│   │   └── setup/                #   Inno Setup installer
│   └── macos/                    # macOS native application (Swift AppKit)
│       ├── Sources/MarkLeaf/     #   Main program
│       ├── Changelog/            #   Product changelog (four languages)
│       └── script/               #   Build / release scripts
├── packages/
│   ├── editor-web/               # Shared editor frontend (Tiptap/ProseMirror + CodeMirror 6)
│   └── styles/                   # Shared typography / themes
├── MarkLeaf.slnx                 # Windows solution
├── Directory.Build.props
├── global.json
├── appicon.png / fileicon.png    # Shared application icons
├── LICENSE / THIRD-PARTY-NOTICES.md
└── README.md
```

## Technical architecture

```text
apps/windows (C# WinForms)        apps/macos (Swift AppKit)
  Main window / menus / workspace / export       Main window / workspace / export
        │                                  │
        ├── packages/editor-web ───────────┤   Shared frontend (Tiptap + CodeMirror)
        ├── packages/styles ───────────────┤   Shared styles
        └── WebView2 / WKWebView       ─────┘   native-shim.js message bridge
```

## Build and run

### Web editor frontend

```bash
pnpm --dir packages/editor-web install --frozen-lockfile
pnpm --dir packages/editor-web build       # Output: packages/editor-web/dist
pnpm --dir packages/editor-web test        # Vitest frontend tests
```

### Windows

```powershell
dotnet restore .\MarkLeaf.slnx
dotnet build .\MarkLeaf.slnx --no-restore
dotnet run --project .\apps\windows\MarkLeaf\MarkLeaf.csproj
```

### macOS

```bash
./apps/macos/script/build_and_run.sh
./apps/macos/script/release/package.sh
```

## License

This application is licensed under the MIT License.

## Other notes

### Fonts

Some themes may require specific fonts:

- [Computer Modern font family](https://www.fontsquirrel.com/fonts/computer-modern) (default LaTeX typography)
- [Huiwen, Chaohua, and KingHwa Old Song fonts](https://huozi.cool/) (type-printing typography)
- [Lxgw WenKai](https://github.com/lxgw/LxgwWenKai) (handwritten-note typography)

