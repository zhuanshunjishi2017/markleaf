# Native Sidebar and Preferences Consistency Design

## Scope

This design combines four macOS fixes into one verified UI-consistency batch:

1. Make Outline typography and row geometry match Workspace.
2. Make the Outline selection background appear on the first click.
3. Preserve the active Preferences page when changing display language.
4. Complete the previously approved four-language recovery-window message fix described in `2026-08-12-recovery-window-localization-design.md`.

No Windows source or shared EditorWeb behavior will be changed.

## Root Causes

### Outline visual mismatch

Workspace uses medium rows with a 26-point height and the normal system label font. Outline uses large rows, a 28-point height, extra intercell spacing, and semibold text for level-one and level-two headings. Its explicit 13-point size therefore still looks larger and heavier than Workspace.

### First-click selection loss

`OutlineTreeView.outlineView(_:shouldSelectItem:)` scrolls the editor before AppKit commits the native row selection. The editor then reports `outlineSelectionChanged`; `EditorSession` routes that through the same `onOutlineChanged` callback used for content changes; `SidebarView` responds with `reloadData()`, clearing the selection that the click just created. The frontend suppresses a duplicate active-position message on the second click, so the second native selection remains visible.

### Preferences page reset

Changing language intentionally destroys and rebuilds `PreferencesWindowController` because most labels are localized during construction. `AppWindowManager.applyLanguage()` does not preserve the selected tab, so the new controller opens its default first page, File.

## Chosen Design

### Shared tree presentation

Introduce a small shared presentation definition used by both Workspace and Outline:

- `rowSizeStyle = .medium`
- `rowHeight = 26`
- the same system-default intercell spacing
- `selectionHighlightStyle = .sourceList`
- `FinderWorkspaceRowView` for a non-emphasized native gray selection
- an explicit 13-point regular system font for row labels

Outline keeps its heading-level indentation. It no longer changes font weight for level-one or level-two headings. Workspace and Outline remain separate data-source/delegate implementations; this change does not introduce a shared tree superclass.

### Separate content and selection events

`EditorSession` will expose independent callbacks for outline content and active-position changes:

- Outline content changes update `outlineHeadings` and request a data reload.
- Outline active-position changes update `activeOutlinePosition` and request selection synchronization only.

`SidebarView` will reload `OutlineTreeView` only for content changes. After a content reload it will restore the active row when that position still exists. Active-position callbacks will select the corresponding row without reloading.

User selection will be handled by `outlineViewSelectionDidChange(_:)`, after AppKit has committed the native selection. It will then call `scrollToPosition`. A short-lived programmatic-selection guard prevents selection synchronized from the editor from issuing the same scroll command back to the editor.

`OutlineTreeView` will route user activation through a small heading-activation closure configured by `SidebarView`. Production connects it to `EditorSession.scrollToPosition`; tests can observe the exact activation count without mocking the editor web view.

If the editor reports no active heading, Outline clears its selection without scrolling. If an active position no longer exists after an outline-content change, no row is selected.

### Preserve Preferences context across language rebuild

`PreferencesWindowController` will retain its `NSTabViewController` and expose the selected page index through a package-internal property. It will accept an initial selected page index and clamp it to the available five pages.

Before rebuilding Preferences, `AppWindowManager.applyLanguage()` will capture:

- the selected page index
- the current window frame
- whether Preferences was visible

The newly localized controller will restore the same page and frame. It will be shown again only when the old Preferences window was visible. Other editor windows and the frontend continue to receive their existing language refresh.

This deliberately keeps the existing rebuild strategy instead of adding error-prone, per-control in-place translation.

### Recovery-window localization

The recovery introduction will use singular and plural localization keys in Simplified Chinese, Traditional Chinese, English, and Japanese. Simplified Chinese removes `（上次异常退出遗留）`. The recovery title, table, snapshot actions, and persistence behavior remain unchanged.

## Components

### `SidebarView.swift`

- Add the shared tree presentation definition.
- Apply it to `WorkspaceTreeView` and `OutlineTreeView`.
- Give Workspace labels an explicit shared font.
- Remove Outline heading weight differences.
- Route outline-content and active-position callbacks separately.
- Move user scrolling from `shouldSelectItem` to `outlineViewSelectionDidChange`.
- Route user heading activation through a testable closure connected to the existing session command.
- Add guarded selection synchronization by heading position.

### `EditorSession.swift`

- Keep the existing outline-content callback for data updates.
- Add a dedicated active-outline-selection callback.
- Handle a null active position as `nil` and notify the selection callback.

### `PreferencesWindowController.swift`

- Retain its tab controller.
- Accept and expose the selected page index.
- Clamp restored indices safely.

### `AppWindowManager.swift`

- Rebuild Preferences with its previous page and frame.
- Do not open Preferences during a language change if it was not visible.

### `RecoveryWindowController.swift` and `L10n.swift`

- Implement the singular/plural four-language recovery copy approved in the dedicated recovery design.

## Testing

Automated AppKit and model tests will cover:

- Workspace and Outline row size style, row height, spacing, font size, and regular font weight are equal.
- Outline retains level indentation while no longer using semibold heading text.
- A real first AppKit click leaves the Outline row selected and sends one scroll command.
- An editor active-position callback selects the correct Outline row without reloading its content.
- A content reload restores the active row when the heading still exists.
- Programmatic selection does not echo a scroll command back to the editor.
- A null or missing active heading clears the Outline selection.
- Rebuilding Preferences after a language change preserves the selected page and clamps invalid indices.
- A hidden Preferences window is not opened merely because language changes elsewhere.
- Recovery-window singular and plural messages are correct in all four languages and contain no obsolete parenthetical key.
- The complete macOS Swift test suite and EditorWeb tests remain green.

Manual verification will check:

1. Outline and Workspace side by side for identical typography and gray row shape.
2. First-click selection on at least two different headings.
3. Editor scrolling and editor-driven Outline selection synchronization.
4. Language switching while Preferences is on General, confirming it stays on General and preserves its window position.
5. Simplified Chinese and English recovery-window introductory text.

## Non-Goals

- Adding a fifth display language.
- Persisting the last Preferences page across application launches.
- Replacing the Preferences rebuild with in-place translation.
- Removing Outline heading indentation.
- Changing the temporary editor heading-highlight animation.
- Refactoring Workspace and Outline into a shared superclass.

## Acceptance Criteria

- Outline and Workspace use the same 13-point regular font, 26-point row height, spacing, and native gray selection shape.
- A heading row displays its gray selection on the first click and scrolls the editor exactly once.
- Editor-driven active-heading changes update Outline selection without flashing or clearing it through `reloadData()`.
- Changing display language keeps Preferences on its current page and at its current window position.
- Language changes do not unexpectedly open a previously closed Preferences window.
- Recovery-window copy is natural and complete in Simplified Chinese, Traditional Chinese, English, and Japanese.
