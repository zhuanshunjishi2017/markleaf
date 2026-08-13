# Native Destructive Save-Prompt Button Design

## Context

MarkLeaf presents saved-document and untitled-document disposition prompts with
`NSAlert`. The current implementation imitates TextEdit's destructive action by
setting `bezelColor` and `attributedTitle`, then applying those values again on
the next main-queue turn after `beginSheetModal`.

That implementation has two observable defects:

- AppKit presents the button once with its default title styling, then the
  delayed recoloring changes the title from white to red.
- The hard-coded colors do not match TextEdit and do not automatically follow
  the system's appearance, accessibility, or future macOS styling.

AppKit documents `hasDestructiveAction` as the supported way to identify a
destructive `NSAlert` button. `bezelColor` and `attributedTitle` are not among
the button properties supported in alert contexts.

## Approved Scope

Keep the existing compact `NSAlert` layout, wording, application icon, button
order, keyboard equivalents, response mapping, and close-after-sheet behavior.
Only the visual and semantic treatment of the “不保存” and “删除” buttons changes.

Rebuilding the prompt as TextEdit's expanded save panel is out of scope.

## Design

Immediately after adding the third alert button, mark it as destructive with:

```swift
alert.buttons[2].hasDestructiveAction = true
```

This property must be set before `beginSheetModal` so the button has its final
system appearance on the first rendered frame.

Remove the custom `styleDestructive` helper, all hard-coded red colors, and the
post-presentation `DispatchQueue.main.async` recoloring block. AppKit becomes
the sole owner of destructive-button color, typography, bezel rendering, hover,
pressed, disabled, light/dark, and accessibility appearances.

## Behavior and Safety

- Saved document: “不保存” is destructive; “保存” and “取消” are unchanged.
- Untitled document: “删除” is destructive; “保存…” and “取消” are unchanged.
- Return still invokes the default save action.
- Escape still invokes cancel.
- Alert response indices and their mapped document choices do not change.
- `EditorWindowController.windowDidEndSheet` remains unchanged so one click on
  the destructive action still closes the editor after the sheet has ended.

## Verification

1. Add a regression test that constructs both alert variants and asserts that
   only the third response button has `hasDestructiveAction == true` before
   presentation.
2. Run the focused macOS tests and the complete macOS test suite with writable
   Swift/Clang cache directories.
3. Build and install the application, then exercise both saved and untitled
   dirty-document prompts.
4. Compare MarkLeaf with TextEdit under the same light appearance. Confirm the
   destructive button uses the system treatment and does not visibly transition
   from white text to red while the sheet opens.
5. Repeat in dark appearance to confirm there are no fixed light-mode colors.
6. Click “不保存” and “删除” once and confirm the sheet and editor close without
   requiring a second click.

## Acceptance Criteria

- No hard-coded destructive-button colors remain.
- No delayed destructive-button restyling remains.
- “不保存” and “删除” use AppKit's native destructive-action semantics.
- The final destructive appearance is present from the first visible frame.
- Save, cancel, response mapping, and close behavior do not regress.
