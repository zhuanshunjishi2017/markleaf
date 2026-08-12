# Windows 1.1.4 and 1.1.5 macOS Parity Design

## Goal

Bring the macOS port to product-behavior parity with Windows releases 1.1.4 (`4d1e158`) and 1.1.5 (`02ee06c`) while retaining native AppKit interaction and the already accepted workspace selection and folder-name double-click behavior.

## Existing Parity

macOS already follows the system appearance, hot-reloads UI language, clears logs older than seven days, supports source-mode font choices, and persists basic sidebar visibility. Those implementations remain the foundation rather than being replaced.

## Batch 1: Settings and Mouse Interaction

- Persist configurable default light and dark theme identifiers.
- Use those identifiers whenever follow-system appearance chooses a theme, with safe built-in fallbacks when a custom theme disappears.
- Expose both defaults in Preferences using light-only and dark-only theme choices.
- Persist the active Workspace/Outline sidebar tab whenever it changes and restore it in new windows.
- Select workspace files on mouse-down but open them only from the matching mouse-up path. A drag that consumes the mouse sequence must not open the file.

## Batch 2: Workspace Drag and Drop

- Publish workspace entries as file URLs so dropping from MarkLeaf into Finder uses a copy operation.
- Use a private pasteboard marker for local drags so dropping onto a folder or the workspace root performs an in-workspace move.
- Reject moves to the current parent, onto the item itself, into a descendant, outside the current workspace, or onto an existing destination.
- Keep an open document associated with its new URL and restart its external-change watcher without producing a false external-change warning.
- Refresh the affected workspace tree while preserving the native outline drop indicator.

## Batch 3: Windows 1.1.5 Features

- Add a persisted CJK glyph preference for `zh-Hans`, `zh-Hant`, `ja`, and `ko`; apply it to the editor root `lang` attribute and `--ml-cjk-lang` CSS variable.
- Consolidate source CJK font, Western font, and source font size into a dedicated AppKit font-settings dialog launched from Preferences.
- Add Focus Mode to View. F11 enters/exits; Escape exits. Focus Mode temporarily hides the sidebar and status bar and requests auto-hidden menu bar/Dock presentation without changing the user's persisted visibility choices.
- Apply the 1.1.5 shared CSS fallback changes, Morandi background adjustment, and Outline-tab typography/selection treatment.
- Update the bundle version, fallback About version, and changelog to 1.1.5.

## Non-Goals

- Porting the Windows `MainForm` partial-class file split.
- Replacing AppKit controls with WinForms visual geometry.
- Changing the accepted gray Workspace selection or folder-name double-click disclosure behavior.

## Verification

Each batch follows red-green XCTest cycles. Final verification includes the complete Swift suite, frontend tests, an ad-hoc signed app build, strict signature verification, installation to `/Applications/MarkLeaf.app`, and direct UI checks for Preferences, mouse-up opening, drag/drop, Focus Mode, CJK preference, and restored sidebar state.
