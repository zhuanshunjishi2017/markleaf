# macOS Document Lifecycle Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Make MarkLeaf saves, external-change detection, and document-disposition sheets reliable and consistent with the approved 1.1.6 macOS design.

**Architecture:** Extract the three stateful concerns embedded in EditorSession: FIFO editor snapshots, serialized document writes, and file-version change decisions. Keep AppKit sheet construction in a dedicated disposition-sheet presenter. EditorSession coordinates these units around existing WebKit, filesystem, and document-disposition APIs while retaining atomic disk writes.

**Tech Stack:** Swift 5.9, AppKit, WebKit, Foundation/Darwin stat, SwiftPM/XCTest.

## Global Constraints

- Target macOS 13+ and preserve atomic document writes.
- Preserve current 1.1.6 close, replace, quit, auto-save, localization, and copy rules.
- A MarkLeaf save never shows an external-change prompt.
- An external atomic replacement remains detectable after a MarkLeaf save or Ignore action.
- Each queued save obtains its own snapshot only after it reaches the front of the write queue.
- Unknown sheet responses cancel; only explicit discard/delete can lose content.
- Do not use time-based event suppression.
- Do not change version metadata, changelogs, or external-change prompt copy.
- Run Swift tests with --disable-sandbox, a writable scratch path, and temporary compiler caches.

## File Map

- Create macos/Sources/MarkLeaf/Services/SnapshotRequestQueue.swift: FIFO snapshot callbacks.
- Create macos/Sources/MarkLeaf/Services/SerialWriteCoordinator.swift: one-at-a-time write scheduling.
- Create macos/Sources/MarkLeaf/Services/ExternalDocumentChangeTracker.swift: file versions and monitor decisions.
- Create macos/Sources/MarkLeaf/Views/DocumentDispositionSheet.swift: horizontal AppKit sheet specification and presenter.
- Modify macos/Sources/MarkLeaf/Services/EditorSession.swift: orchestrate the extracted units.
- Add matching XCTest files under macos/Tests/MarkLeafTests.

---

### Task 1: Queue Editor Snapshot Requests

**Files:**
- Create: macos/Sources/MarkLeaf/Services/SnapshotRequestQueue.swift
- Create: macos/Tests/MarkLeafTests/SnapshotRequestQueueTests.swift
- Modify: macos/Sources/MarkLeaf/Services/EditorSession.swift:98,170-173,577-580

**Interfaces:**
- Produces SnapshotRequestQueue.enqueue(_:), completeNext(_:), cancelAll(with:), and isEmpty.
- Consumes Result<String, Error> from the existing WebKit snapshot message.

- [ ] **Step 1: Write failing FIFO tests**

Create tests that enqueue two callbacks, complete them with one and two, and assert FIFO delivery. Add cases proving an orphan response is ignored and cancelAll fails every pending callback in order.

~~~swift
let queue = SnapshotRequestQueue()
var values: [String] = []
queue.enqueue { if case .success(let value) = $0 { values.append("first: \(value)") } }
queue.enqueue { if case .success(let value) = $0 { values.append("second: \(value)") } }
queue.completeNext(.success("one"))
queue.completeNext(.success("two"))
XCTAssertEqual(values, ["first: one", "second: two"])
XCTAssertTrue(queue.isEmpty)
~~~

- [ ] **Step 2: Verify RED**

~~~bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-lifecycle-t1/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-lifecycle-t1/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-lifecycle-t1/scratch \
  --filter SnapshotRequestQueueTests
~~~

Expected: compile failure because SnapshotRequestQueue does not exist.

- [ ] **Step 3: Implement the minimal queue**

Store completion closures in an array. completeNext removes and invokes the first closure. cancelAll clears storage before invoking every pending closure with failure. Replace EditorSession.pendingSnapshot with the queue; enqueue before sending requestSnapshot and complete the oldest request in the snapshot message handler.

- [ ] **Step 4: Verify GREEN**

Run the focused command, then filter DocumentDispositionTests|ApplicationTerminationCoordinatorTests. All selected tests pass.

- [ ] **Step 5: Commit**

~~~bash
git add macos/Sources/MarkLeaf/Services/SnapshotRequestQueue.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Tests/MarkLeafTests/SnapshotRequestQueueTests.swift
git commit -m "fix(macos): queue editor snapshot requests"
~~~

### Task 2: Serialize Writes Without Sharing Snapshots

**Files:**
- Create: macos/Sources/MarkLeaf/Services/SerialWriteCoordinator.swift
- Create: macos/Tests/MarkLeafTests/SerialWriteCoordinatorTests.swift
- Modify: macos/Sources/MarkLeaf/Services/EditorSession.swift:481-498,918-946

**Interfaces:**
- Produces SerialWriteCoordinator.enqueue(start:) and isWriting.
- start receives an idempotent finish() callback; only then may the next write start.

- [ ] **Step 1: Write failing serial-order tests**

Test that enqueuing two operations starts only the first, finishing it starts the second, and duplicate finish calls never start an operation twice.

~~~swift
let coordinator = SerialWriteCoordinator()
var started: [String] = []
var finishFirst: (() -> Void)?
coordinator.enqueue { finish in started.append("first"); finishFirst = finish }
coordinator.enqueue { finish in started.append("second"); finish() }
XCTAssertEqual(started, ["first"])
finishFirst?()
finishFirst?()
XCTAssertEqual(started, ["first", "second"])
XCTAssertFalse(coordinator.isWriting)
~~~

- [ ] **Step 2: Verify RED**

Run the Task 1 command with --filter SerialWriteCoordinatorTests. Expected: missing-type compile failure.

- [ ] **Step 3: Implement and integrate**

Implement a FIFO array of start closures plus an active flag. The supplied finish closure has a local finished Boolean guard. Refactor writeCurrentDocument so every call enqueues one operation. Only when an operation begins does it request a snapshot, perform its atomic write, invoke only its own completion, and finish the queue item. Snapshot and write failures leave isDirty unchanged and still advance the queue.

- [ ] **Step 4: Verify GREEN**

Run filters SnapshotRequestQueueTests|SerialWriteCoordinatorTests|DocumentDispositionTests|ApplicationTerminationCoordinatorTests. All selected tests pass.

- [ ] **Step 5: Commit**

~~~bash
git add macos/Sources/MarkLeaf/Services/SerialWriteCoordinator.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Tests/MarkLeafTests/SerialWriteCoordinatorTests.swift
git commit -m "fix(macos): serialize document writes"
~~~

### Task 3: Rebind File Monitoring After Atomic Replacement

**Files:**
- Create: macos/Sources/MarkLeaf/Services/ExternalDocumentChangeTracker.swift
- Create: macos/Tests/MarkLeafTests/ExternalDocumentChangeTrackerTests.swift
- Modify: macos/Sources/MarkLeaf/Services/EditorSession.swift:85-87,451-464,511-565,918-946

**Interfaces:**
- Produces DocumentFileVersion.read(from:) from device, inode, nanosecond modification time, and size.
- Produces ExternalDocumentChangeDecision: ignore, rebindAndRecheck, presentExternalChange, missing.
- Produces ExternalDocumentChangeTracker.acceptCurrentVersion(at:), beginSelfWrite(), finishSelfWrite(at:), and decision(forEventAt:).

- [ ] **Step 1: Write failing tracker tests**

Use temporary files and real atomic writes. Prove a self-write is ignored, an external atomic replacement after it requires rebind then prompt, accepting an ignored external version permits a later prompt, and a missing path returns missing.

~~~swift
let tracker = ExternalDocumentChangeTracker()
try tracker.acceptCurrentVersion(at: url)
tracker.beginSelfWrite()
try "saved".write(to: url, atomically: true, encoding: .utf8)
try tracker.finishSelfWrite(at: url)
XCTAssertEqual(try tracker.decision(forEventAt: url), .ignore)
try "external".write(to: url, atomically: true, encoding: .utf8)
XCTAssertEqual(try tracker.decision(forEventAt: url), .rebindAndRecheck)
XCTAssertEqual(try tracker.decision(forEventAt: url), .presentExternalChange)
~~~

- [ ] **Step 2: Verify RED**

Run Task 1's command with --filter ExternalDocumentChangeTrackerTests. Expected: missing-type compile failures.

- [ ] **Step 3: Implement deterministic versions and watcher integration**

Implement DocumentFileVersion with Darwin stat. Remove lastExternalChange and its 1.5-second suppression. Stop watching immediately before atomic save. On success call finishSelfWrite(at:) and start exactly one new watch before reporting success. First Save As follows the same path.

For watcher events, ask the tracker for a decision. On rebindAndRecheck, stop and restart the watch on the normalized current path, then recheck. Present only on presentExternalChange. After Reload or Ignore, accept the current version so a later external edit remains detectable.

- [ ] **Step 4: Verify GREEN**

Run filters ExternalDocumentChangeTrackerTests|SnapshotRequestQueueTests|SerialWriteCoordinatorTests|DocumentDispositionTests|PreparedDocumentTests. All selected tests pass.

- [ ] **Step 5: Commit**

~~~bash
git add macos/Sources/MarkLeaf/Services/ExternalDocumentChangeTracker.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Tests/MarkLeafTests/ExternalDocumentChangeTrackerTests.swift
git commit -m "fix(macos): rebind file watch after atomic saves"
~~~

### Task 4: Present Safe Horizontal Disposition Sheets

**Files:**
- Create: macos/Sources/MarkLeaf/Views/DocumentDispositionSheet.swift
- Create: macos/Tests/MarkLeafTests/DocumentDispositionSheetTests.swift
- Modify: macos/Sources/MarkLeaf/Services/EditorSession.swift:854-892
- Modify: macos/Tests/MarkLeafTests/DocumentDispositionTests.swift

**Interfaces:**
- Produces DocumentDispositionSheetSpec.saved(filename:) and untitled().
- Each spec exposes ordered actions, default/cancel indices, and safe semantic choice mapping.
- Produces DocumentDispositionSheetPresenter.presentSaved(for:filename:completion:) and presentUntitled(for:completion:).

- [ ] **Step 1: Write failing sheet tests**

Assert saved order is 不保存 / 取消 / 保存, untitled order is 删除 / 取消 / 保存…, trailing save is default, middle cancel handles Escape, and an unknown index maps to cancel for both kinds.

~~~swift
let saved = DocumentDispositionSheetSpec.saved(filename: "notes.md")
XCTAssertEqual(saved.actions.map(\.title), [L10n.t("不保存"), L10n.t("取消"), L10n.t("保存")])
XCTAssertEqual(saved.defaultActionIndex, 2)
XCTAssertEqual(saved.cancelActionIndex, 1)
XCTAssertEqual(saved.savedChoice(forActionIndex: 99), .cancel)
~~~

- [ ] **Step 2: Verify RED**

Run Task 1's command with --filter DocumentDispositionSheetTests. Expected: missing-spec compile failure.

- [ ] **Step 3: Implement dedicated AppKit sheet**

Create a sheet content controller using native labels, application icon, and a horizontal NSStackView of NSButton controls. Set trailing Save/Save As as default and Escape as Cancel. End the sheet before completion and guard completion to fire once. Semantic mappings live in the spec and default to cancel.

Replace only the two disposition NSAlert blocks in EditorSession.requestDisposition. Keep the external-modification alert unchanged.

- [ ] **Step 4: Verify GREEN**

Run filters DocumentDispositionSheetTests|DocumentDispositionTests|ApplicationTerminationCoordinatorTests. All selected tests pass.

- [ ] **Step 5: Commit**

~~~bash
git add macos/Sources/MarkLeaf/Views/DocumentDispositionSheet.swift \
  macos/Sources/MarkLeaf/Services/EditorSession.swift \
  macos/Tests/MarkLeafTests/DocumentDispositionSheetTests.swift \
  macos/Tests/MarkLeafTests/DocumentDispositionTests.swift
git commit -m "fix(macos): use horizontal document disposition sheets"
~~~

### Task 5: Full Verification and Manual Acceptance

- [ ] **Step 1: Run the complete Swift suite**

~~~bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
CLANG_MODULE_CACHE_PATH=/tmp/markleaf-lifecycle-full/clang \
SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-lifecycle-full/swiftpm \
swift test --disable-sandbox --package-path macos \
  --scratch-path /tmp/markleaf-lifecycle-full/scratch
~~~

Expected: all tests pass except the documented sandbox-only synthetic AppKit click limitation, if unchanged.

- [ ] **Step 2: Build and manually verify**

~~~bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer \
macos/script/build_and_run.sh --build-only
~~~

1. Edit a saved document, press Command-S, wait two seconds, and confirm no external-change prompt.
2. Atomically modify the file externally, confirm one prompt, choose Ignore, modify again, and confirm another prompt.
3. Save a new untitled document, edit it externally, and confirm monitoring is active.
4. Close modified saved and untitled documents; inspect the two approved horizontal action orders and Escape cancellation.
5. Exercise manual save near automatic close/quit saves and confirm no operation remains stranded.

- [ ] **Step 3: Inspect final state**

~~~bash
git diff --check
git status --short
git log --oneline -6
~~~

Expected: no whitespace errors and only intended lifecycle changes.
