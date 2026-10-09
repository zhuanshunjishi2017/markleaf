# Change Log

All notable changes to the MarkLeaf VS Code extension are documented here.

## [0.3.0] - 2026-10-08

- Keep the outline sidebar and divider at the available viewport height, with independent scrolling for heading lists of any length.
- Align outline navigation with the Windows desktop product: hierarchical expand/collapse, case-insensitive heading search, locate current heading, keyboard navigation, and keep the active visible row in view.
- Show complete heading text on hover and preserve the clicked heading while scroll positioning settles.
- Cache heading indexes by immutable document and use bounded heading-position queries during scrolling in the shared editor kernel; update sidebar highlighting only when the active heading changes.
- Cancel pending source chapter positioning when the editor is destroyed, the document changes, or a newer jump replaces it.
- Load Mermaid diagram and layout implementations together on first use, with a separate preparation timeout so cold dependency loading does not consume a diagram's rendering budget.
- Resume queued diagrams when a timed-out renderer finishes, bound waiting for a stuck renderer, distinguish loading/blocked/rendering errors, and offer retry for recoverable diagram failures.
- Yield the main thread between diagrams so consecutive renders do not delay document painting and input handling.
- Preserve the existing outline visibility preference and configurable package publisher. This release is distributed as a local VSIX.

## 0.2.8

- Fixed long-document scrolling stalls by animating scrollbar-specific styles instead of an inherited property on the document root.
- Initialize the editor in its actual editable or read-only state to avoid an extra full-document view update on open.
- Improved long-document initialization by applying code highlighting before view creation and deferring hidden outlines.
- Fixed repeated Mermaid rendering and queue wait time being counted against each diagram's render timeout.
- Local packages default to publisher `markleaf`; `--publisher` overrides the packaged identity without changing the source configuration.
- Fixed toolbar actions rejected by validation of shared kernel command state.
- Fixed overlapping export and shortcut dialogs, off-screen menus, and obscured math overlays.
- Settings and help now use the running extension ID, including packages published as `zhuanshunjishi2017.markleaf`; existing `markleaf.*` settings are preserved.
- Updated the four-language guides, settings entry instructions, and feature mapping.

## [0.2.7] - 2026-09-13

- Added PDF, standalone HTML, PNG/JPG image export, preview, and printing.
- Added visual Markdown editing for tables, footnotes, math, Mermaid diagrams, images, and GitHub alert blocks.
- Added source/rendered editor switching, outline navigation, focus and typewriter modes, typography and color preferences.
- Added localized extension documentation in Simplified Chinese, English, Japanese, and Traditional Chinese.
- Improved synchronization with VS Code `TextDocument`, conflict handling, image resources, and remote workspace behavior.

## [0.2.6]

- Internal improvements to the editor integration and bundled document kernel.

## [0.2.5]

- Previous preview release.

[0.2.7]: https://github.com/zhuanshunjishi2017/markleaf/compare/vscode-v0.2.6...vscode-v0.2.7
[0.2.6]: https://github.com/zhuanshunjishi2017/markleaf/compare/vscode-v0.2.5...vscode-v0.2.6
[0.2.5]: https://github.com/zhuanshunjishi2017/markleaf/releases
