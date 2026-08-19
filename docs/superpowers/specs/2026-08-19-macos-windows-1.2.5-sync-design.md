# macOS Windows 1.2.5 Sync Design

## Goal

Bring every user-visible Windows 1.2.5 capability that applies to macOS into the macOS application, while consuming the same shared editor and style sources and preserving native macOS interaction patterns.

## Scope

- Sync the feature branch onto the remote `main` that already contains Windows 1.2.5 and the shared editor/style changes.
- Consume shared source-mode undo/redo, paste fixes, full-width input fixes, safe Markdown serialization, and the `colors-saltlemon.css` theme.
- Add the macOS host side of `unsafeEmphasisRequested` / `unsafeEmphasisResponse`, including a native decision dialog and a persisted “do not ask again” preference.
- Add explicit paste-as-plain-text commands to the main Edit menu and editor context menu.
- Restrict read-only context menus and menu command enablement to non-mutating operations.
- Persist export format, paper, orientation, margins, style, color scheme, header, footer, and header/footer configuration.
- Add “Export with Last Settings” and make the regular export window restore the last-used settings.
- Match Windows 1.2.5 PDF header/footer behavior: presets, custom text, alignment, page-number placeholders, style-derived font, and the documented spacing.
- Keep the macOS 1.2.5 Dock lifecycle and icon safe-area changes already implemented locally.

## Architecture

Shared TypeScript and CSS remain the source of truth for editor behavior and themes. The macOS host adds narrowly scoped Swift policies/models for protocol decisions, menu visibility, and export persistence, while AppKit controllers remain responsible for dialogs and menus. Pure models are covered by standalone Swift regression scripts; shared editor behavior remains covered by the existing pnpm tests.

## Non-goals

- No GitHub Release or tag.
- No Windows code changes.
- No redesign of the existing macOS export window beyond controls required for Windows 1.2.5 parity.
