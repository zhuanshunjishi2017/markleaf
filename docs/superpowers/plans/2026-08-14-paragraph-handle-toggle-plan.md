# Paragraph Block Handle Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a persisted macOS preference that controls paragraph block-handle decorations, defaulting to enabled and applying immediately to open editors.

**Architecture:** Store `showParagraphBlockHandle` in `AppSettings`, expose it in the existing Editor preferences page, and send a document-independent command to each `EditorSession` WebView. The EditorWeb block-handle extension keeps its plugin but gates widget/node decorations behind a mutable visibility flag.

**Tech Stack:** Swift/AppKit, Codable JSON settings, WKWebView bridge, TypeScript/Tiptap/ProseMirror, XCTest, Vitest.

## Global Constraints

- The new setting defaults to enabled so existing users retain current behavior.
- Missing JSON keys must decode as enabled for backward compatibility.
- Settings changes apply immediately to current documents and persist through the existing `settings.json` path.
- No paragraph commands are removed when the visual handle is hidden.

---

### Task 1: Add settings model coverage

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/AppSettings.swift`
- Modify: `macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift`

**Interfaces:** `AppSettings.showParagraphBlockHandle: Bool`, default `true`.

- [ ] **Step 1: Write the failing tests** — assert a JSON object without the key decodes to `true`, and explicit `false` survives Codable round-trip.
- [ ] **Step 2: Run the focused XCTest and verify RED** — run the existing MarkLeaf test command for `AppSettingsFollowSystemTests`; expect the missing-member failure.
- [ ] **Step 3: Implement the Codable field** — add the default property and `decodeIfPresent(... ) ?? true` in `init(from:)`.
- [ ] **Step 4: Run the focused XCTest and verify GREEN** — all new and existing settings tests pass.
- [ ] **Step 5: Commit**

```bash
git add macos/Sources/MarkLeaf/Services/AppSettings.swift macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift
git commit -m "feat(macos): persist paragraph block handle preference"
```

### Task 2: Add the preference UI and live host propagation

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`
- Modify: `macos/Sources/MarkLeaf/Services/AppWindowManager.swift` only if the existing settings-change loop does not reach every open session

**Interfaces:** `EditorSession.applyPreferences()` and `applyPostLoadSettings()` send `setBlockHandleVisible` with `"1"` or `"0"`.

- [ ] **Step 1: Write the failing UI/propagation test** — assert the checkbox initializes from the setting and the session applies the visibility command through the existing command path.
- [ ] **Step 2: Run the focused test and verify RED** — expect the checkbox and command to be absent.
- [ ] **Step 3: Implement the checkbox and host command** — create `blockHandleCheck`, place it under Editor → Visual, save it in `controlChanged()`, and send the command on preference application and document load.
- [ ] **Step 4: Run focused and existing macOS tests** — preference, startup, and editor-session tests must pass with default-enabled behavior unchanged.
- [ ] **Step 5: Commit**

```bash
git add macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift macos/Sources/MarkLeaf/Services/EditorSession.swift macos/Tests/MarkLeafTests
git commit -m "feat(macos): expose paragraph block handle preference"
```

### Task 3: Gate EditorWeb decorations behind the setting

**Files:**
- Modify: `src/EditorWeb/src/editor.ts`
- Modify: `src/EditorWeb/src/main.ts`
- Create or modify: `src/EditorWeb/tests/block-handle.test.ts`

**Interfaces:** `setBlockHandleVisible(editor: Editor, visible: boolean): void` updates visibility and dispatches a transaction so decorations recompute.

- [ ] **Step 1: Write failing Vitest tests** — hidden state produces no `.ml-block-handle`, re-enabling restores the widget at the caret, and the host command accepts `"1"`/`"0"` without changing document content.
- [ ] **Step 2: Run focused Vitest and verify RED** — run `pnpm vitest run tests/block-handle.test.ts`; expect the missing setter/command failure.
- [ ] **Step 3: Implement the visibility gate** — add a default-true module flag, return `DecorationSet.empty` when false, export the setter, and handle `setBlockHandleVisible` in `main.ts` before document-dependent commands.
- [ ] **Step 4: Run focused and full EditorWeb tests** — run the new file, then `pnpm vitest run` from `src/EditorWeb`.
- [ ] **Step 5: Commit**

```bash
git add src/EditorWeb/src/editor.ts src/EditorWeb/src/main.ts src/EditorWeb/tests/block-handle.test.ts
git commit -m "feat(editor): toggle paragraph block handle decorations"
```

### Task 4: Verify persisted behavior end to end

- [ ] **Step 1:** Run all macOS Swift tests.
- [ ] **Step 2:** Run `cd src/EditorWeb && pnpm vitest run` and confirm every test file passes.
- [ ] **Step 3:** Manually toggle Preferences → Editor → 显示段落块句柄 off/on and verify immediate change, restart persistence, and restoration without reopening the document.
- [ ] **Step 4:** Commit only a smallest, verified test-fixture correction if a regression requires one.
