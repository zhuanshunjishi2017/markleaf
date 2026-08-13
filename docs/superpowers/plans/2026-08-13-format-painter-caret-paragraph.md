# Format Painter Caret Paragraph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make caret-based format painter capture the caret's inline style and apply the result to an entire caret paragraph, including empty paragraphs.

**Architecture:** Keep the existing `FormatPainterController` API. Add a focused helper for resolving caret marks from adjacent text or stored marks, and keep the existing block-range transaction path for non-empty targets while adding stored-mark handling for empty targets.

**Tech Stack:** TypeScript, Tiptap/ProseMirror, Vitest, Vite.

## Global Constraints

- Source with no selection uses caret inline marks plus paragraph/heading type.
- Target with no selection formats the whole paragraph/heading and restores the caret.
- Unsupported list/table/image blocks remain invalid targets.
- One-shot painter disarms only after a successful paint.

---

### Task 1: Add failing caret edge-case tests

**Files:**
- Modify: `src/EditorWeb/tests/format-painter.test.ts`

- [ ] **Step 1: Test caret source at the end of a marked run**

Create `**source** plain` and place the caret immediately after `source`. Assert `captureFormat(editor)?.marks.bold` is `true`, then paint a plain target caret and assert the full target paragraph is bold.

- [ ] **Step 2: Test empty caret target retains marks for future typing**

Create `**source**\n\n` (an empty second paragraph), arm from `source`, place the caret in the empty paragraph, apply the painter, insert `typed`, and assert the resulting Markdown contains `**typed**`.

- [ ] **Step 3: Run the focused tests and verify the new tests fail**

Run `pnpm test -- format-painter.test.ts`. Expected: the marked-run edge case and/or empty-target typing assertion fails against the current implementation.

---

### Task 2: Implement robust caret source and target behavior

**Files:**
- Modify: `src/EditorWeb/src/format-painter.ts`

- [ ] **Step 1: Resolve marks at a caret**

Add a helper that reads `editor.state.storedMarks` first when present, then the text node immediately before the caret, then the text node immediately after the caret, and finally `editor.isActive` for each supported mark. Return the same five-boolean mark object used by `FormatPainterSnapshot`.

- [ ] **Step 2: Use the helper only for empty source selections**

Keep existing uniform-range validation for non-empty selections. Replace the direct `editor.isActive` calls in the empty-selection branch with the helper.

- [ ] **Step 3: Preserve marks on empty target paragraphs**

When the target range is empty after resolving the current text block, apply the captured marks as stored marks after the block command, then restore the original caret. Do not change text or the one-shot disarm behavior.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run `pnpm test -- format-painter.test.ts`. Expected: all format painter tests pass, including the new caret edge cases.

- [ ] **Step 5: Commit the implementation**

```bash
git add src/EditorWeb/src/format-painter.ts src/EditorWeb/tests/format-painter.test.ts
git commit -m "fix(editor): complete caret format painter behavior"
```

---

### Task 3: Verify production assets and real app behavior

**Files:**
- No additional source files.

- [ ] **Step 1: Run the complete EditorWeb test suite**

Run `pnpm test` from `src/EditorWeb`; expected 80+ tests pass with zero failures.

- [ ] **Step 2: Run the EditorWeb production build**

Run `pnpm build`; expected TypeScript and Vite build complete successfully.

- [ ] **Step 3: Package and install MarkLeaf**

Run the existing macOS `build_and_run.sh --build-only` with the supported SDK/module-cache environment, install the resulting bundle with `ditto --norsrc`, and verify its code signature.

- [ ] **Step 4: Smoke-test the real editor**

With a temporary Markdown fixture, place a caret in a formatted source paragraph, activate “格式刷”, place a caret in a plain target paragraph, and confirm the target paragraph adopts the source heading/marks while its text and caret remain intact. Remove the temporary fixture afterward.
