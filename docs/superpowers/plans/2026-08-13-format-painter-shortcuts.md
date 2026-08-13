# Format Painter Shortcuts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Office-style format painter shortcuts to MarkLeaf: `⌘⇧C` captures the current format and `⌘⇧V` applies the armed format to the current selection or paragraph.

**Architecture:** Keep format-painter state and application in the existing EditorWeb `FormatPainterController`. Add a distinct `formatPainterApply` command so native menu shortcuts can invoke an immediate application without changing the existing mouse/toolbar toggle behavior. Register both commands in the native Format menu and list them in the shortcut reference window.

**Tech Stack:** TypeScript, Tiptap/ProseMirror, Vitest, Swift/AppKit, XCTest-compatible menu tests, SwiftPM macOS build.

## Global Constraints

- `⌘⇧C` captures; `⌘⇧V` applies and disarms after a successful application.
- Applying without an armed painter is a no-op and reports failure.
- Existing `⌘C`/`⌘V` copy and paste behavior remains unchanged.
- Source mode does not arm or apply the format painter.

---

### Task 1: Add immediate format-painter application command

**Files:**
- Modify: `src/EditorWeb/src/main.ts`
- Test: `src/EditorWeb/tests/format-painter.test.ts`

**Interfaces:**
- Consumes: `FormatPainterController.arm` and `applyOnSelection`.
- Produces: host command `formatPainterApply`, which applies the armed snapshot to the current target and disarms on success.

- [x] **Step 1: Write the failing command test** for arming a source selection, selecting a target, calling `executeFormatPainterApply`, and confirming the target is formatted and the painter is disarmed.
- [x] **Step 2: Run the focused Vitest test** and verify the new command/test fails before implementation.
- [x] **Step 3: Handle `formatPainterApply` in `main.ts`**, rejecting source mode and unarmed state, invoking `applyOnSelection(editor)`, refreshing cursor/state, and returning `commandResult`.
- [x] **Step 4: Run the focused Vitest test** and verify it passes.

### Task 2: Register native menu shortcuts

**Files:**
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift` (the existing `EditorSession.performMenuCommand` extension)
- Modify: `macos/Tests/MarkLeafTests/FormatPainterMenuTests.swift`

**Interfaces:**
- Consumes: existing `formatPainter` command and new `formatPainterApply` command.
- Produces: Format menu items `格式刷` (`⌘⇧C`) and `应用格式刷` (`⌘⇧V`).

- [x] **Step 1: Add failing menu tests** asserting represented commands, key equivalents, and modifier masks.
- [x] **Step 2: Run the focused Swift test/probe**; the XCTest filter is unavailable in this CommandLineTools environment because SwiftPM cannot resolve its manifest sandbox, so the product build is the compilation gate.
- [x] **Step 3: Add both native menu items** and dispatch `formatPainterApply` through the existing `EditorSession.performMenuCommand` extension.
- [x] **Step 4: Run the product build and inspect the live Format menu**; both shortcut menu entries are present.

### Task 3: Update shortcut reference and verify the application

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/ShortcutWindowController.swift`

- [x] **Step 1: Add both shortcut rows** to the native shortcut reference.
- [x] **Step 2: Run all frontend tests and production build** from `src/EditorWeb` (86/86 tests pass).
- [x] **Step 3: Run `./script/build_and_run.sh --build-only`**, install the resulting app, and verify ad-hoc code signing.
- [ ] **Step 4: Run `git diff --check`, inspect status, and commit the completed feature.**
