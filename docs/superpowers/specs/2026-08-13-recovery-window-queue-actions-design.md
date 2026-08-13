# Recovery Window Queue Actions Design

## Context

The recovery window currently presents recovery snapshots in a table with
“保存”, “另存为…”, “全部丢弃”, and “取消”. “全部丢弃” removes every recovery
snapshot, while successful save actions close the window immediately. There is
no single-snapshot discard action, and processing multiple snapshots requires
reopening the recovery window.

## Approved Scope

Turn the recovery window into a queue that processes one selected snapshot at a
time. Keep the existing table, copy, localization, save workflows, and window
layout except for the additional single-item action and the post-success queue
refresh behavior.

## Action Semantics

### Selected-snapshot actions

- “保存” remains visible only when the selected snapshot has an original file
  path. On successful write-back, delete that snapshot's recovery files, remove
  it from the in-memory list, reload the table, and select an adjacent remaining
  row. If it was the final snapshot, close the window automatically.
- “另存为…” remains disabled until a snapshot is selected. On successful save,
  delete that snapshot's recovery files, remove it from the list, reload, and
  select an adjacent row; close automatically when the list becomes empty.
- Add a “丢弃” button disabled until a snapshot is selected. On activation,
  delete only the selected snapshot's recovery files, remove it from the list,
  reload, and select an adjacent row; close automatically when the list becomes
  empty.

When a save panel is cancelled or a save operation fails, retain the selected
  snapshot, table row, and window. Existing save-failure alerts remain in place.

### Whole-list actions

- “全部丢弃” remains available at all times and uses AppKit's native destructive
  button semantics. It deletes all recovery files and closes the window.
- “取消” closes the window without changing any recovery snapshot.

## UI and Localization

Use `NSButton.hasDestructiveAction = true` for both “全部丢弃” and “丢弃” before
the buttons are presented. Do not set hard-coded red colors, attributed titles,
or delayed recoloring. Add the “丢弃” translation to the existing Simplified
Chinese, Traditional Chinese, English, and Japanese localization tables.

## Queue Refresh Rules

After a successful selected-snapshot action:

1. Remove the snapshot by its `documentId` through `RecoveryService.delete`.
2. Remove the same element from the controller's `snapshots` array.
3. Reload the table view.
4. If rows remain, select the nearest valid row (same index unless the removed
   row was last, then the new last row) and update button state.
5. If no rows remain, close the recovery window.

The refresh helper must be shared by successful save-to-original, successful
save-as, and single discard so all three paths have identical behavior.

## Error Handling

- Recovery file deletion remains best-effort through the existing service API.
- A write-back or save-as error must not remove the snapshot or close the
  window; the current error presentation remains authoritative.
- Cancelling an `NSSavePanel` must not mutate the snapshot list.
- Discard actions are intentionally destructive and have no undo in this
  window, matching the existing “全部丢弃” behavior.

## Verification

1. Add unit-level coverage for the queue refresh/index rule: removing a middle
   row selects the row that shifts into its position; removing the last row
   selects the new last row; removing the final row requests window closure.
2. Add controller coverage that “全部丢弃” and “丢弃” are destructive, with
   “丢弃” disabled before selection and enabled after selection.
3. Run the focused tests and the full macOS suite where the local toolchain can
   resolve XCTest; separately run a real AppKit probe when the CommandLineTools
   XCTest module is unavailable.
4. In the installed app, create multiple recovery snapshots and verify:
   successful save, save-as, and single discard each refresh the same window;
   the final successful action closes it; all-discard closes immediately; save
   failure and save-panel cancellation preserve the row.
5. Verify destructive buttons in light and dark appearances and confirm no
   hard-coded color or delayed recoloring exists.

## Acceptance Criteria

- The recovery window exposes a selected-row-only “丢弃” action.
- “全部丢弃” and “丢弃” render through AppKit destructive semantics.
- Successful save, save-as, and single discard remove exactly one snapshot and
  refresh the queue without closing while rows remain.
- The last successful action closes the empty recovery window automatically.
- All-discard closes the window and removes all snapshots.
- Save errors and save-panel cancellation leave the snapshot available.
- Existing recovery and localization behavior does not regress.
