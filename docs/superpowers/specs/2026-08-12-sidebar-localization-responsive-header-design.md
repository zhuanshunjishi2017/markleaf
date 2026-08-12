# Sidebar Localization and Responsive Header Design

## Problem

When MarkLeaf starts in English with no workspace open, the sidebar placeholder remains in Simplified Chinese even though the tab labels and folder button are translated. The English `Open Folder` title is also truncated in the current 230-point sidebar because the segmented tabs, spacer, and text button share one horizontal row.

The localization table already contains the correct English placeholder. The defect comes from constructing the placeholder with a raw Chinese string and only translating it later through `applyLanguage()`. That later refresh is not guaranteed during initial view construction.

## Goals

- Show the correct localized placeholder on first render and after an in-app language change.
- Keep the existing 230-point default sidebar instead of permanently reducing editor width.
- Keep the folder-opening action available both before and after a workspace is opened.
- Prevent the header action from truncating in English and other longer translations.
- Preserve native AppKit appearance, keyboard accessibility, VoiceOver labeling, and existing workspace-opening behavior.

## Chosen Design

Use an adaptive hybrid layout.

The workspace header keeps the `Workspace` and `Outline` segmented control. The trailing text button becomes a fixed-size system icon button using `folder.badge.plus`. Its localized full title is exposed through the Tooltip and accessibility label. The icon is visible only while the Workspace tab is active, matching the current visibility rule.

When no workspace is open, the center placeholder becomes a vertical stack containing:

1. A localized status label such as `No workspace open`.
2. A text-labeled `Open Folder` call-to-action button using its natural width.

Both the header icon and the empty-state button invoke the same folder-selection action. When a workspace opens, the empty state is hidden while the header icon remains available for switching workspaces.

## Width Behavior

- Keep the default workspace width at 230 points.
- Raise the interactive minimum sidebar width from 150 to 200 points so the English segmented labels and icon remain usable.
- Clamp previously saved widths below 200 points to 200 when applying window state.
- Do not add a new preference or forcibly widen existing widths that are already at least 200 points.
- Preserve the current maximum-width rule that reserves at least 420 points for the editor.

## Localization Behavior

All user-visible sidebar strings are localized at construction time using `L10n.t(...)`, including the initial empty-state label. `SidebarView.applyLanguage()` subsequently refreshes:

- Both segmented-control labels.
- The header icon Tooltip and accessibility label.
- The empty-state status label.
- The empty-state text button.

The standalone canonical key `暂未打开工作区` is added to every language table and translated as `No workspace open` in English. The redundant instruction `Click “Open Folder” to start` is removed from the empty state because the visible text button directly communicates the action. `SidebarView` no longer uses the old multiline placeholder key; that key is retained only if another caller still references it.

## Component Changes

### `SidebarView`

- Replace the single multiline placeholder text field with a vertical `NSStackView` containing a status label and text button.
- Configure the existing header folder control as a fixed-size icon button.
- Route both folder controls to the existing `openFolder` selector.
- Update `showTab`, `workspaceChanged`, and `applyLanguage` to treat the entire empty-state stack as one visibility unit.

### `EditorWindowController`

- Centralize the 200-point minimum sidebar width and use it when restoring, showing, animating, and constraining the split view.
- Leave the 230-point default setting unchanged.

### `L10n`

- Add the canonical `暂未打开工作区` entry with English, Traditional Chinese, and Japanese translations rather than composing translated fragments at runtime.
- Remove the old multiline placeholder entry if repository-wide usage confirms that it has no remaining callers.

## Testing

Automated tests will cover:

- A newly constructed English sidebar immediately shows English placeholder content, with no Chinese fallback.
- `applyLanguage()` refreshes segmented labels, the icon Tooltip/accessibility label, status text, and the text button.
- The header folder control is icon-only and has a fixed width at the 230-point default layout.
- The empty-state button and header icon share the same folder-opening action.
- Empty-state visibility follows workspace availability and the active sidebar tab.
- Saved sidebar widths below 200 points are clamped, while valid wider values are preserved.
- Existing localization table completeness tests continue to pass for English, Traditional Chinese, and Japanese.

Manual verification will check the 230-point English sidebar in light and dark appearances, including Tooltip display, VoiceOver labeling, and resizing down to the 200-point minimum.

## Non-Goals

- Adding a user preference for header layout.
- Permanently increasing the default sidebar width.
- Moving workspace selection into the application toolbar or File menu only.
- Introducing custom icons, custom animation, or language-specific width constants.

## Acceptance Criteria

- No Chinese sidebar placeholder appears when the interface language is English.
- `Workspace`, `Outline`, and the header folder icon fit without truncation at 230 points.
- The sidebar cannot be manually narrowed below 200 points while visible.
- The empty state shows a complete localized `Open Folder` button.
- The header icon remains available after a workspace opens and disappears on the Outline tab.
- Both folder controls open the same native folder picker.
