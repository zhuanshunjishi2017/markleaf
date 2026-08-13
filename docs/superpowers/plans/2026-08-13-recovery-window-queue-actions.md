# Recovery Window Queue Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add native-red single/all discard actions and keep the recovery window open as a queue after each successful save, save-as, or single discard until the last snapshot is processed.

**Architecture:** Put the deterministic “which row is selected after removal?” rule in a small Foundation-only helper that can be exercised without XCTest. Keep all file deletion, in-memory removal, table reload, introduction copy refresh, selection, and final-window closure in one `RecoveryWindowController.completeProcessing(row:)` method shared by the three successful selected-row actions.

**Tech Stack:** Swift 5, AppKit (`NSWindow`, `NSTableView`, `NSButton.hasDestructiveAction`), SwiftPM, standalone Swift probes, and macOS Computer Use.

## Global Constraints

- “保存”, “另存为…”, and selected “丢弃” process exactly one selected snapshot.
- Successful selected-row processing refreshes the existing window and selects the nearest remaining row.
- Processing the last snapshot automatically closes the empty recovery window.
- Save-panel cancellation and write failure retain the snapshot, selection, and window.
- “全部丢弃” removes all recovery snapshots and closes immediately.
- “取消” closes without changing recovery snapshots.
- “丢弃” and “全部丢弃” use `hasDestructiveAction` plus semantic `NSColor.systemRed` styling applied once before presentation; no fixed RGB values or delayed recoloring.
- Add “丢弃” in Simplified Chinese, Traditional Chinese, English, and Japanese.
- Use `/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk` because the default SDK is newer than the installed Swift compiler.

---

### Task 1: Deterministic queue selection rule

**Files:**
- Create: `macos/Sources/MarkLeaf/Services/RecoveryQueueSelection.swift`
- Create: `macos/Tests/Probes/RecoveryQueueSelectionProbe.swift`
- Create: `macos/Tests/MarkLeafTests/RecoveryQueueSelectionTests.swift`

**Interfaces:**
- Produces: `RecoveryQueueSelection.nextRow(afterRemoving:remainingCount:) -> Int?`.
- Consumes: a zero-based removed row and the count after removal; returns the nearest valid row or `nil` when the queue is empty.

- [ ] **Step 1: Write a failing standalone probe**

```swift
import Foundation

@main
enum RecoveryQueueSelectionProbe {
    static func main() {
        precondition(RecoveryQueueSelection.nextRow(afterRemoving: 1, remainingCount: 2) == 1)
        precondition(RecoveryQueueSelection.nextRow(afterRemoving: 2, remainingCount: 2) == 1)
        precondition(RecoveryQueueSelection.nextRow(afterRemoving: 0, remainingCount: 0) == nil)
    }
}
```

The probe catches a missing helper, selecting past the final row, jumping backward after middle-row removal, and failing to close an empty queue.

- [ ] **Step 2: Run the probe and verify RED**

```bash
swiftc -sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
  Tests/Probes/RecoveryQueueSelectionProbe.swift \
  -o /private/tmp/markleaf-recovery-queue-probe
```

Expected: compilation fails because `RecoveryQueueSelection` does not exist.

- [ ] **Step 3: Implement the minimal selection helper**

```swift
import Foundation

enum RecoveryQueueSelection {
    static func nextRow(afterRemoving removedRow: Int, remainingCount: Int) -> Int? {
        guard remainingCount > 0 else { return nil }
        return min(max(removedRow, 0), remainingCount - 1)
    }
}
```

- [ ] **Step 4: Verify GREEN and add XCTest parity coverage**

Compile the production helper with the probe and run it:

```bash
swiftc -sdk /Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
  Sources/MarkLeaf/Services/RecoveryQueueSelection.swift \
  Tests/Probes/RecoveryQueueSelectionProbe.swift \
  -o /private/tmp/markleaf-recovery-queue-probe
/private/tmp/markleaf-recovery-queue-probe
```

Add equivalent literal assertions to `RecoveryQueueSelectionTests.swift` for future full-Xcode runs.

---

### Task 2: Recovery controller queue actions and localization

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Create: `macos/Tests/Probes/RecoveryWindowActionsProbe.swift`
- Modify: `macos/Tests/MarkLeafTests/RecoveryWindowLocalizationTests.swift`

**Interfaces:**
- Consumes: `RecoveryQueueSelection.nextRow(afterRemoving:remainingCount:)` from Task 1 and existing `RecoveryService.delete(documentId:)`.
- Produces: controller-owned `discardSelectedButton` and `discardAllButton` properties for real button-state assertions; `completeProcessing(row:)` shared by successful save, save-as, and discard.

- [ ] **Step 1: Write a failing AppKit controller probe**

Compile `RecoveryWindowController.swift` with test-local stubs for `RecoverySnapshot`, `RecoveryService`, `SettingsService`, `L10n`, `AppWindowManager`, `AppLog`, and `RecoverySaveFailure`. The probe must instantiate two snapshots and assert:

```swift
precondition(controller.discardAllButton?.hasDestructiveAction == true)
precondition(controller.discardSelectedButton?.hasDestructiveAction == true)
precondition(controller.discardSelectedButton?.isEnabled == false)

controller.tableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
controller.tableViewSelectionDidChange(Notification(name: NSTableView.selectionDidChangeNotification))
precondition(controller.discardSelectedButton?.isEnabled == true)

controller.discardSelectedButton?.performClick(nil)
precondition(controller.numberOfRows(in: controller.tableView) == 1)
precondition(controller.tableView.selectedRow == 0)
precondition(RecoveryService.shared.deletedDocumentIDs == ["first"])
```

The production change that makes this pass is the real button construction and shared queue mutation; no mock button is asserted.

- [ ] **Step 2: Run the controller probe and verify RED**

Run `swiftc` against the existing controller, Task 1 helper, and probe. Expected: compilation fails because the selected discard button and exposed table/button contracts do not exist.

- [ ] **Step 3: Add localized native destructive buttons**

In each `L10n` table add literal translations:

```swift
// zh-Hans key is returned directly
"丢弃": "丟棄",   // zh-Hant
"丢弃": "Discard", // en
"丢弃": "破棄",    // ja
```

In `RecoveryWindowController`, retain internal read-only access for the table and destructive buttons:

```swift
let tableView = NSTableView()
private(set) var discardSelectedButton: NSButton?
private(set) var discardAllButton: NSButton?
```

Create “丢弃” disabled initially and mark both discard buttons before display:

```swift
discardSelectedButton.isEnabled = false
discardSelectedButton.hasDestructiveAction = true
discardAllButton.hasDestructiveAction = true
```

Put the selected discard button between “另存为…” and “全部丢弃”.

- [ ] **Step 4: Centralize action availability and successful processing**

Add `updateActionAvailability()` and call it from selection changes. Add:

```swift
private func completeProcessing(row: Int) {
    guard snapshots.indices.contains(row) else { return }
    let snapshot = snapshots.remove(at: row)
    RecoveryService.shared.delete(documentId: snapshot.documentId)
    introductionLabel.stringValue = RecoveryWindowCopy.introduction(
        snapshotCount: snapshots.count,
        language: language
    )
    tableView.reloadData()

    guard let nextRow = RecoveryQueueSelection.nextRow(
        afterRemoving: row,
        remainingCount: snapshots.count
    ) else {
        close()
        return
    }
    tableView.selectRowIndexes(IndexSet(integer: nextRow), byExtendingSelection: false)
    updateActionAvailability()
}
```

Add selected discard as `completeProcessing(row:)`. Replace `close()` after successful save-to-original and save-as with `completeProcessing(row:)`. In save-as, return from the `catch` path without deleting or closing. A cancelled panel already returns without mutation.

- [ ] **Step 5: Verify controller probe GREEN and localization coverage**

Run the controller probe. Extend its scenario to discard the final row after showing the window and assert the window is no longer visible. Add XCTest localization assertions that `L10n.translate("丢弃", language:)` returns `丟棄`, `Discard`, and `破棄`.

- [ ] **Step 6: Build, audit, and commit**

```bash
SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.5.sdk \
CLANG_MODULE_CACHE_PATH=/private/tmp/markleaf-clang-module-cache \
SWIFTPM_MODULECACHE_OVERRIDE=/private/tmp/markleaf-swiftpm-module-cache \
swift build --disable-sandbox
git diff --check
git add macos/Sources/MarkLeaf/Services/RecoveryQueueSelection.swift \
  macos/Sources/MarkLeaf/Services/L10n.swift \
  macos/Sources/MarkLeaf/Views/RecoveryWindowController.swift \
  macos/Tests/Probes/RecoveryQueueSelectionProbe.swift \
  macos/Tests/Probes/RecoveryWindowActionsProbe.swift \
  macos/Tests/MarkLeafTests/RecoveryQueueSelectionTests.swift \
  macos/Tests/MarkLeafTests/RecoveryWindowLocalizationTests.swift
git commit -m "feat(macos): process recovery snapshots as a queue"
```

Expected: no manual red colors or asynchronous restyling; the only closure after a selected action comes from the empty-queue branch.

---

### Task 3: Installed-app recovery workflow verification

**Files:**
- Verify: `macos/dist/MarkLeaf.app`
- Install and verify: `/Applications/MarkLeaf.app`

**Interfaces:**
- Consumes: the committed queue behavior from Tasks 1–2.
- Produces: evidence for multi-item refresh, nearest-row selection, final auto-close, native destructive appearance, and persistence semantics.

- [ ] **Step 1: Package and install**

Run the existing build/package script with the macOS 26.5 SDK and writable caches, preserve the current installed app under `/private/tmp`, replace `/Applications/MarkLeaf.app`, clear copied Finder metadata, and verify the ad-hoc signature with `codesign --verify --deep --strict`.

- [ ] **Step 2: Create isolated recovery fixtures**

Preserve any existing user recovery directory before testing. Create three disposable recovery `.md`/`.meta` pairs with unique document IDs: one with a writable original path, one without a path for save-as, and one for discard. Launch the installed app and open the recovery window through the real menu/startup path.

- [ ] **Step 3: Verify single discard and native styling**

Before selection, verify “丢弃” is disabled. Select the discard fixture, verify it enables, and visually confirm both discard buttons use AppKit destructive styling in light and dark appearances. Click “丢弃”; confirm only that pair is removed, the window remains, the list count decreases, and the nearest remaining row is selected.

- [ ] **Step 4: Verify save and save-as queue refresh**

Select the fixture with an original path and click “保存”; confirm the original file receives the snapshot content, its recovery pair disappears, and the same recovery window remains on the next row. Use “另存为…” on the pathless fixture and confirm successful save removes it and closes the now-empty window. Repeat cancellation once and confirm no row or files are removed.

- [ ] **Step 5: Verify all-discard and restore user state**

Create two disposable fixtures, click “全部丢弃”, and confirm the window closes and both pairs are removed. Remove test files, restore any preserved user recovery directory byte-for-byte, restore the original appearance, and leave the installed tested app in place.

- [ ] **Step 6: Final verification evidence**

Freshly run both standalone probes, `swift build --disable-sandbox`, `git diff --check`, `git status --short --branch`, and installed-app signature verification. Run `swift test --disable-sandbox`; if the local CommandLineTools still cannot resolve `XCTest`, report that exact environment limitation rather than claiming the suite passed.
