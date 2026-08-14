# Read-Only Changelog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 帮助 → 更新内容 opens the changelog in a new window in read-only mode, so it can be viewed/copied but never edited or saved.

**Architecture:** Add an `isReadOnly` flag to the document pipeline (`PreparedDocument` → `EditorSession` → frontend `loadDocument`). The frontend renders read-only visual/source editors and rejects mutating commands; the native side skips save/close prompts, disables mutating menu items, and routes the changelog into a dedicated new window.

**Tech Stack:** Swift/AppKit (EditorSession, AppWindowManager, NativeMenuBuilder), TypeScript (Tiptap/ProseMirror + CodeMirror 6), Vitest/jsdom.

## Global Constraints

- macOS 13+; keep existing WKWebView selection strategy (no `drawSelection()` in source mode).
- `loadDocument` payload stays backward compatible: `readOnly` defaults to false and older hosts/frontends ignore it.
- Preserve the visible source-mode caret: read-only source editor uses `EditorState.readOnly.of(true)`, never `editable(false)`.
- Do not write the changelog into recent files, `lastFile`, recovery snapshots, or external-file watch.
- No new dependencies.

---

### Task 1: Frontend read-only rendering + command gate

**Files:**
- Modify: `src/EditorWeb/src/editor.ts` (`createEditor`, `replaceEditorDocument`)
- Modify: `src/EditorWeb/src/source-editor.ts` (`SourceEditor` constructor, `buildExtensions`)
- Modify: `src/EditorWeb/src/main.ts` (`readOnly` state, `loadDocument`, `setSourceMode`, command whitelist, `sendCommandState`)
- Test: `src/EditorWeb/tests/read-only.test.ts` (new), `src/EditorWeb/tests/source-editor.test.ts`

**Interfaces:**
- `createEditor(element: HTMLElement, content = '', readOnly = false): Editor`
- `replaceEditorDocument(editor, element, content, readOnly = false): Editor`
- `new SourceEditor(parent, content, onChange, indentWidth = 2, readOnly = false)`

- [ ] **Step 1: Write failing tests** (`tests/read-only.test.ts`)

```ts
import { describe, expect, it } from 'vitest'
import { EditorState } from '@codemirror/state'
import { createEditor, replaceEditorDocument } from '../src/editor'
import { SourceEditor } from '../src/source-editor'

describe('read-only documents', () => {
  it('renders the visual editor as non-editable', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, '# Title', true)
    expect(editor.isEditable).toBe(false)
    editor.destroy()
  })

  it('keeps the source editor read-only while retaining the state facet', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const source = new SourceEditor(el, 'leaf', () => {}, 2, true)
    expect(source.view.state.facet(EditorState.readOnly)).toBe(true)
    source.destroy()
  })

  it('replaces the document without losing read-only state', () => {
    const el = document.createElement('div')
    document.body.append(el)
    const editor = createEditor(el, 'a')
    const replacement = replaceEditorDocument(editor, el, 'b', true)
    expect(replacement.isEditable).toBe(false)
    replacement.destroy()
  })
})
```

- [ ] **Step 2: Run tests** — expect RED (signatures don't accept `readOnly`).
- [ ] **Step 3: Implement** — add the `readOnly` params above; `editable: !readOnly` in `createEditor`; `EditorState.readOnly.of(true)` in `buildExtensions` when readOnly.
- [ ] **Step 4: main.ts wiring**

```ts
let readOnly = false
// loadDocument:
readOnly = payload?.readOnly === true
editor = replaceEditorDocument(editor, editorMount, payload.markdown, readOnly)
// setSourceMode / plain text creation:
sourceEditor = new SourceEditor(sourceMount, text, markSourceChanged, sourceIndentWidth, readOnly)
```

Add a read-only command whitelist at the top of the command handler:

```ts
const READ_ONLY_ALLOWED_COMMANDS = new Set([
  'find', 'toggleSourceMode', 'setStyle', 'setSourceSelection',
  'findText', 'findNext', 'findPrev', 'findClose',
  'setLanguage', 'setSourceIndent', 'setAutoHideScrollbar',
  'setBlockHandleVisible', 'exportSelection', 'exportDocument',
])
// in handleMessage, command branch:
if (readOnly && !READ_ONLY_ALLOWED_COMMANDS.has(payload.command)) {
  if (message.requestId) send('commandResult', { success: false }, message.requestId)
  break
}
```

And include `readOnly` in `sendCommandState()`.

- [ ] **Step 5: Run `pnpm test`** — GREEN (92 existing + 3 new).
- [ ] **Step 6: Commit** `feat(editor): add read-only document rendering`

### Task 2: Native read-only document pipeline

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift` (`PreparedDocument`, `loadDocument`, `requestDisposition`, `saveDocument`, `saveDocumentAs`, `loadPreparedDocument`, `commandStateChanged` mapping)
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift` (只读 translations)
- Test: `macos/Tests/MarkLeafTests/PreparedDocumentTests.swift`

**Interfaces:**
- `struct PreparedDocument { let url: URL; let markdown: String; let isReadOnly: Bool }` (memberwise default `isReadOnly = false`)
- `EditorSession.loadDocument(markdown: String, fileURL: URL?, readOnly: Bool = false)`
- `EditorSession.isReadOnly: Bool` (private(set))

- [ ] **Step 1: Write failing tests** in `PreparedDocumentTests`

```swift
func testReadOnlyDefaultsToFalse() {
    let doc = PreparedDocument(url: URL(fileURLWithPath: "/tmp/a.md"), markdown: "x")
    XCTAssertFalse(doc.isReadOnly)
}

func testReadOnlyFlagIsPreserved() {
    let doc = PreparedDocument(url: URL(fileURLWithPath: "/tmp/a.md"), markdown: "x", isReadOnly: true)
    XCTAssertTrue(doc.isReadOnly)
}
```

- [ ] **Step 2: Compile/run XCTest when toolchain available** — RED (field missing).
- [ ] **Step 3: Implement** — add field, thread `readOnly` through `loadDocument`, skip external watch/recovery timer when readOnly, add `"readOnly"` to the payload, force `canUndo/canRedo` false when readOnly, skip recents in `loadPreparedDocument` for read-only docs, short-circuit `requestDisposition` to `.proceed`, and guard `saveDocument`/`saveDocumentAs`.
- [ ] **Step 4: Commit** `feat(macos): support read-only documents end to end`

### Task 3: Open changelog in a read-only new window

**Files:**
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift` (`openChangelog`)
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift` (menu gating)

**Interfaces:**
- `EditorSession.readOnlyBlockedCommands: Set<String>` (static)
- `AppWindowManager.openChangelog()` reuses `newWindow(preparedDocument:)`

- [ ] **Step 1: Implement** — in `openChangelog`, after copying the cached copy, focus an existing read-only changelog window or build `PreparedDocument(url: target, markdown: …, isReadOnly: true)` and call `newWindow(preparedDocument:)`; fall back to status text on read/copy failure.
- [ ] **Step 2: Menu gating** — in `MenuRouter.validateMenuItem`, `return false` for `readOnlyBlockedCommands` when the active session is read-only; same guard in `performMenuCommand`/`performMenuCommand` routing and `EditorSession.execute` call sites that mutate.
- [ ] **Step 3: Commit** `feat(macos): open changelog in a read-only window`

### Task 4: Verify in the installed app

- [ ] `pnpm test` (green) and `pnpm build`
- [ ] Rebuild via `macos/script/build_and_run.sh --build-only`, install to `/Applications`, launch
- [ ] 帮助 → 更新内容: new window opens, no save prompt on close, 保存 menu disabled, typing produces no document change
- [ ] Commit any follow-up fixes
