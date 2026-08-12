# Finder-Style Workspace Selection Design

## Problem

The workspace tree currently uses the default blue source-list selection highlight. The requested appearance is the neutral gray rounded selection used by the macOS Finder sidebar.

## Goals

- Draw a Finder-style gray background for the selected workspace file or folder row.
- Apply the same treatment to directory and Markdown-file rows.
- Preserve the existing selection, file-opening, disclosure-control, keyboard, and accessibility behavior.
- Adapt automatically to light and dark appearances.
- Leave the unselected sidebar background unchanged.

## Chosen Design

Introduce a small `NSTableRowView` subclass dedicated to workspace selection. It draws only the selected state as an inset rounded rectangle using the semantic AppKit color `unemphasizedSelectedContentBackgroundColor`. The semantic color follows the active macOS appearance without fixed RGB values.

`WorkspaceTreeView` supplies this row view through its existing `NSOutlineViewDelegate` implementation. The outline's selection model remains unchanged, so files continue to open and remain selected, folders remain selectable, and folder-name double-click handling continues to use the existing disclosure path.

The custom drawing affects only the workspace tree. The Outline tab and the sidebar container retain their current appearance.

## Visual Details

- Use a small horizontal and vertical inset so the highlight does not touch the row edges.
- Use a system-like corner radius comparable to Finder sidebar selection.
- Do not recolor icons or hard-code label colors; AppKit continues to resolve their foreground appearance.
- Use one neutral gray treatment regardless of keyboard focus, matching the supplied Finder reference.

## Testing

Automated tests will verify that:

- Workspace rows use the Finder-style row-view class.
- The semantic gray selection color is used instead of the accent-color highlight.
- Directory and file items remain selectable.
- Existing workspace mouse-interaction tests continue to pass.

Manual verification will select an `example.md` file and a folder in both light and dark appearances, checking the rounded gray highlight and confirming that file opening and folder-name double-click expansion still work.

## Non-Goals

- Changing the whole sidebar background.
- Changing the Outline tab selection style.
- Adding a color preference.
- Matching a fixed screenshot RGB value across different macOS appearances.

## Acceptance Criteria

- A selected workspace file or folder has a Finder-like neutral gray rounded background instead of blue.
- Unselected rows and the sidebar background remain unchanged.
- The treatment remains legible in light and dark mode.
- File selection/opening and folder-name double-click expansion/collapse behave as before.
