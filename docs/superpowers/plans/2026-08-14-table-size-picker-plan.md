# Table Size Picker Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace fixed `3×3` table insertion with one reusable Word-style row/column picker for the top Table menu and editor context menu.

**Architecture:** Keep insertion native at the menu layer with a testable size model and an AppKit custom grid view. Both menu builders create the same picker item and call `EditorSession.execute("insertTable", text: "rows,cols")`; EditorWeb validates the payload and invokes Tiptap’s table command with a header row.

**Tech Stack:** Swift/AppKit `NSMenuItem.view`, `NSView` tracking and mouse events, Swift XCTest, TypeScript/Tiptap, Vitest.

## Global Constraints

- The picker is available from both the top Table menu and editor context menu.
- The visible grid covers `1×1` through `10×10`; custom input handles larger positive integer sizes.
- Hover shows a rectangular selection and live localized size text.
- Clicking inserts immediately; Escape/cancel leaves the document unchanged.
- The first row remains a header row; invalid or missing dimensions fall back to `3×3`.

---

### Task 1: Add the pure size model and tests

**Files:**
- Create: `macos/Sources/MarkLeaf/Views/TableSizePickerModel.swift`
- Create: `macos/Tests/MarkLeafTests/TableSizePickerModelTests.swift`

**Interfaces:** `TableSize(rows: Int, columns: Int)`, `TableSizePickerModel.visibleLimit`, `.defaultSize`, `.clamped(rows:columns:)`, and `.parse(_:)`.

- [ ] **Step 1:** Write tests for default `3×3`, valid `1×1`/`10×10`, malformed or non-positive custom input, and valid sizes above the visible grid.
- [ ] **Step 2:** Run the focused test target and verify RED because the model types do not exist.
- [ ] **Step 3:** Implement positive-integer validation; keep visible-grid clamping separate from custom-size parsing so `12×14` is not reduced.
- [ ] **Step 4:** Run `TableSizePickerModelTests` and verify GREEN.
- [ ] **Step 5: Commit**

```bash
git add macos/Sources/MarkLeaf/Views/TableSizePickerModel.swift macos/Tests/MarkLeafTests/TableSizePickerModelTests.swift
git commit -m "feat(macos): add table size validation model"
```

### Task 2: Build the reusable AppKit grid picker

**Files:**
- Create: `macos/Sources/MarkLeaf/Views/TableSizePickerView.swift`
- Create: `macos/Tests/MarkLeafTests/TableSizePickerViewTests.swift`

**Interfaces:** `TableSizePickerView: NSView`, `onSelect: ((TableSize) -> Void)?`, `onCancel: (() -> Void)?`, and `init(initialSize:visibleLimit:)`.

- [ ] **Step 1:** Write tests that a point in row 7/column 8 updates the selection, click invokes `onSelect`, and Escape invokes `onCancel`.
- [ ] **Step 2:** Run the focused test and verify RED because the view does not exist.
- [ ] **Step 3:** Draw the 10×10 grid, add tracking, map pointer coordinates to row/column, invalidate hover changes, render the live title, and make the view first responder for Escape.
- [ ] **Step 4:** Add “自定义表格…” with two integer fields; keep the picker open for invalid input and select only after valid confirmation.
- [ ] **Step 5:** Run picker tests and verify GREEN.
- [ ] **Step 6: Commit**

```bash
git add macos/Sources/MarkLeaf/Views/TableSizePickerView.swift macos/Tests/MarkLeafTests/TableSizePickerViewTests.swift
git commit -m "feat(macos): add interactive table size picker"
```

### Task 3: Integrate the picker into native and context menus

**Files:**
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`
- Modify: `macos/Tests/MarkLeafTests/EditorContextMenuTests.swift`

**Interfaces:** `EditorSession.insertTable(rows: Int, columns: Int)` calls `execute("insertTable", text: "rows,columns")`; both menu paths use the shared picker factory.

- [ ] **Step 1:** Write tests for `"7,8"` command encoding and for both menu construction paths exposing a picker rather than a fixed command item.
- [ ] **Step 2:** Run focused tests and verify RED against the current plain `insertTable` item.
- [ ] **Step 3:** Implement the helper, replace both plain insert items, and preserve existing in-table/out-of-table validation.
- [ ] **Step 4:** Add localized grid title, custom-table, rows, columns, confirm, and cancel strings for the existing supported languages.
- [ ] **Step 5:** Run focused context-menu/localization tests and verify GREEN.
- [ ] **Step 6: Commit**

```bash
git add macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift macos/Sources/MarkLeaf/Services/EditorSession+ContextMenu.swift macos/Sources/MarkLeaf/Services/EditorSession.swift macos/Sources/MarkLeaf/Services/L10n.swift macos/Tests/MarkLeafTests/EditorContextMenuTests.swift
git commit -m "feat(macos): use table size picker in menus"
```

### Task 4: Accept dimensions in EditorWeb

**Files:**
- Modify: `src/EditorWeb/src/editor.ts`
- Modify: `src/EditorWeb/src/main.ts`
- Create or modify: `src/EditorWeb/tests/table-insert.test.ts`

**Interfaces:** `executeEditorCommand(editor, "insertTable", text)` parses `text` as `rows,columns`.

- [ ] **Step 1:** Write tests for `"7,8"`, missing text, malformed text, and zero dimensions; valid input creates the requested table and invalid input creates default `3×3`.
- [ ] **Step 2:** Run `pnpm vitest run tests/table-insert.test.ts` and verify RED because the command ignores text.
- [ ] **Step 3:** Parse two positive integers, cap hostile values to a safe upper bound, and call `chain.insertTable({ rows, cols, withHeaderRow: true })`; invalid text uses `3×3`.
- [ ] **Step 4:** Run the focused file, then `cd src/EditorWeb && pnpm vitest run`.
- [ ] **Step 5: Commit**

```bash
git add src/EditorWeb/src/editor.ts src/EditorWeb/tests/table-insert.test.ts
git commit -m "feat(editor): insert tables with selected dimensions"
```

### Task 5: End-to-end verification

- [ ] **Step 1:** Run all Swift tests.
- [ ] **Step 2:** Run all EditorWeb tests.
- [ ] **Step 3:** Manually verify both menu paths, hover `7×8`, click insertion, header row, Escape cancellation, and custom dimensions above `10×10`.
- [ ] **Step 4:** Verify the paragraph-handle toggle remains immediate and persistent.
- [ ] **Step 5:** Commit only verified test-fixture adjustments if needed.
