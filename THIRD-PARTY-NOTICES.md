# Third-Party Notices

MarkLeaf uses the following third-party packages. Their license texts are
included in the distribution package or available at the listed URLs.

## .NET (NuGet)

| Package | Version | License |
| --- | --- | --- |
| Microsoft.Web.WebView2 | 1.0.4078.44 | [Microsoft package license](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4078.44/License) |

## Editor Frontend (npm)

### Runtime Dependencies

| Package | Version | License |
| --- | --- | --- |
| @tiptap/core | 3.29.2 | MIT |
| @tiptap/pm | 3.29.2 | MIT |
| @tiptap/starter-kit | 3.29.2 | MIT |
| @tiptap/markdown | 3.29.2 | MIT |
| @tiptap/extension-image | 3.29.2 | MIT |
| @tiptap/extension-link | 3.29.2 | MIT |
| @tiptap/extension-table | 3.29.2 | MIT |
| @tiptap/extension-task-item | 3.29.2 | MIT |
| @tiptap/extension-task-list | 3.29.2 | MIT |
| @codemirror/state | 6.5.2 | MIT |
| @codemirror/view | 6.38.1 | MIT |
| @codemirror/language | 6.11.3 | MIT |
| @codemirror/commands | 6.8.1 | MIT |
| @codemirror/lang-markdown | 6.3.4 | MIT |

Tiptap is built on [ProseMirror](https://prosemirror.net/) (MIT), which is
bundled via `@tiptap/pm`.

### Dev Dependencies

| Package | Version | License |
| --- | --- | --- |
| TypeScript | 5.9.3 | Apache-2.0 |
| Vite | 7.2.4 | MIT |
| Vitest | 4.0.15 | MIT |
| jsdom | 27.2.0 | MIT |

## Fonts

The editor uses the following system font stacks and does not bundle font
files:

- UI sans-serif: Segoe UI, system-ui
- Editor body: charter, Georgia, Cambria, "Times New Roman", "宋体", serif
- Editor code: "Cascadia Code", "JetBrains Mono", "Fira Code", Consolas, "Courier New", monospace

## License Compliance

All third-party packages are used under their respective open-source
licenses. No GPL or AGPL code is included. A full transitive dependency
tree with license metadata is available via:

```
pnpm --dir packages/editor-web licenses list --json
```
