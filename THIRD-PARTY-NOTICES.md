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
| marked | 17.0.6 | MIT |
| entities | 8.1.0 | BSD-2-Clause |
| parse5 | 8.0.1 | MIT |
| KaTeX | 0.16.21 | MIT (code), OFL-1.1 (fonts) |

Tiptap is built on [ProseMirror](https://prosemirror.net/) (MIT), which is
bundled via `@tiptap/pm`.

### Dev Dependencies

| Package | Version | License |
| --- | --- | --- |
| TypeScript | 5.9.3 | Apache-2.0 |
| Vite | 7.2.4 | MIT |
| Vitest | 4.0.15 | MIT |
| jsdom | 27.2.0 | MIT |

## VS Code Export Runtime (npm)

| Package | Version | License |
| --- | --- | --- |
| puppeteer-core | 24.43.1 | Apache-2.0 |

The VS Code package bundles Puppeteer Core and its runtime dependencies, but does
not bundle or download Chrome/Chromium. It launches an installed Chrome or Edge
with an isolated temporary profile. Bundled runtime license texts are in
`dist/export-licenses/` in the VSIX.
The Puppeteer license text is retained from the upstream
[24.43.1 source tag](https://github.com/puppeteer/puppeteer/blob/puppeteer-v24.43.1/LICENSE)
in `apps/vscode/licenses/puppeteer-LICENSE` because its npm packages omit that file.

## Fonts

The editor bundles KaTeX WOFF2 math fonts under OFL-1.1. Other text uses the following system font stacks:

- UI sans-serif: Segoe UI, system-ui
- Editor body: charter, Georgia, Cambria, "Times New Roman", "宋体", serif
- Editor code: "Cascadia Code", "JetBrains Mono", "Fira Code", Consolas, "Courier New", monospace

## License Compliance

All third-party packages are used under their respective open-source
licenses. No GPL or AGPL code is included. A full transitive dependency
tree with license metadata is available via:

```
corepack pnpm --dir packages/editor-core licenses list --json
```
