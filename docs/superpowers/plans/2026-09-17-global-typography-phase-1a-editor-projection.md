# Global Typography Phase 1A Editor Projection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a default-disabled shared ProseMirror typography projection experiment that can turn eligible paragraphs into `LayoutPlan`-driven decorations through an explicit `EditorWeb` command.

**Architecture:** `@markleaf/editor-core` consumes the pure `@markleaf/typography` kernel and exposes a ProseMirror extension with an injectable measurement adapter. The adapter extracts eligible top-level paragraphs, maps grapheme offsets back to ProseMirror positions, and emits non-copying line-break widgets plus diagnostic state. `@markleaf/editor-web` adds a command gate that remains disabled by default, disables itself in source/plain-text modes, and reports diagnostics without introducing host UI or export changes.

**Tech Stack:** TypeScript 5.9, ProseMirror via `@tiptap/pm`, Vitest 4, jsdom, pnpm 11.

**Spec:** [2026-09-17-global-typography-engine-design.md](../specs/2026-09-17-global-typography-engine-design.md)

## Global Constraints

- This is Phase 1A only: no default enablement, no preferences UI, no PDF/image/HTML export changes, no scheduler, and no VS Code integration.
- Typography projections must not change document JSON, Markdown serialization, copied text, or undo history.
- The plugin must be disabled unless an explicit command enables it.
- Source mode and plain-text documents must disable projection.
- Headings, code blocks, tables, front matter, formulas, Mermaid, images, and paragraphs containing non-text inline atoms are out of scope for Phase 1A.
- Top-level ordinary paragraphs are the only eligible blocks in this phase.
- Do not run the full macOS test suite; run only the targeted package suites and typechecks.
- Do not stage unrelated untracked files under `docs/`.

---

## File Structure

```text
packages/editor-core/
├── package.json
├── src/
|   ├── editor.ts
|   ├── index.ts
|   ├── styles.css
|   └── typography/
|       ├── projection.ts
|       ├── projection-measure.ts
|       └── projection-command.ts
└── tests/
    └── typography-projection.test.ts

packages/editor-web/
├── src/
|   ├── main.ts
|   └── protocol.ts
└── tests/
    ├── protocol.test.ts
    └── typography-command.test.ts
```

Responsibilities:

- `projection.ts`: paragraph extraction, ProseMirror offset mapping, decoration state, plugin, and public APIs.
- `projection-measure.ts`: WebView DOM measurement adapter.
- `projection-command.ts`: explicit command parsing and serializable diagnostics.
- `editor.ts`: registers the extension.
- `styles.css`: controlled-paragraph and break-widget styling.
- `editor-web/main.ts`: applies the explicit command and mode gates.
- `editor-web/protocol.ts`: adds the diagnostics message type.

## Public Contracts

These names are consumed by later tasks and must remain exact:

```ts
export type TypographyProjectionStatus =
  | 'disabled'
  | 'planned'
  | 'partial'
  | 'fallback';

export type TypographyProjectionDiagnostics = {
  status: TypographyProjectionStatus;
  eligibleParagraphs: number;
  plannedParagraphs: number;
  fallbackParagraphs: number;
  lineBreaks: number;
};

export type TypographyMeasurement = (
  text: string,
  script: ScriptClass,
) => TextMetrics;

export type TypographyProjectionOptions = {
  availableWidth: number;
  measure: TypographyMeasurement;
};

export type TypographyProjectionCommand = {
  enabled: boolean;
};

function extractTypographyParagraphs(doc: PMNode): Array<{
  from: number;
  to: number;
  text: string;
  clusterOffsets: number[];
  eligible: boolean;
  fallbackReason?: 'unsupported-block' | 'inline-atom' | 'inline-code';
}>;

function parseTypographyProjectionCommand(payload: unknown): TypographyProjectionCommand | null;

function setTypographyProjectionEnabled(
  editor: Editor,
  enabled: boolean,
  options: TypographyProjectionOptions,
): void;

function getTypographyProjectionDiagnostics(editor: Editor): TypographyProjectionDiagnostics;
```

---

### Task 1: Connect the pure kernel and add a disabled plugin skeleton

**Files:**

- Modify: `packages/editor-core/package.json`
- Create: `packages/editor-core/src/typography/projection.ts`
- Modify: `packages/editor-core/src/editor.ts`
- Modify: `packages/editor-core/src/index.ts`
- Create: `packages/editor-core/tests/typography-projection.test.ts`

**Interfaces:**

- Consumes: `LayoutPlan`, `ScriptClass`, `TextMetrics` from `@markleaf/typography`.
- Produces: the disabled `TypographyProjection` extension and `getTypographyProjectionDiagnostics()`.

- [x] **Step 1: Add the local kernel dependency**

Modify `packages/editor-core/package.json` dependencies:

```json
{
  "dependencies": {
    "@markleaf/typography": "link:../typography"
  }
}
```

Keep all existing dependencies unchanged. Run:

```bash
corepack pnpm --dir packages/editor-core install
```

Expected: PASS and `pnpm-lock.yaml` updates.

- [x] **Step 2: Write the disabled-state test**

Create `packages/editor-core/tests/typography-projection.test.ts`:

```ts
import { afterEach, describe, expect, it } from 'vitest'
import {
  createEditor,
  getTypographyProjectionDiagnostics,
} from '../src/index'

const cleanup: Array<() => void> = []

function setup(markdown: string) {
  document.body.innerHTML = '<main id="editor"></main>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
  cleanup.push(() => editor.destroy())
  return { editor, mount }
}

afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  document.body.innerHTML = ''
})

describe('typography projection', () => {
  it('starts disabled and leaves the document untouched', () => {
    const { editor } = setup('中文 English 中文')

    expect(getTypographyProjectionDiagnostics(editor)).toEqual({
      status: 'disabled',
      eligibleParagraphs: 0,
      plannedParagraphs: 0,
      fallbackParagraphs: 0,
      lineBreaks: 0,
    })
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-planned')).toHaveLength(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })
})
```

- [x] **Step 3: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
```

Expected: FAIL because `getTypographyProjectionDiagnostics` does not exist.

- [x] **Step 4: Implement the disabled plugin state**

Create `packages/editor-core/src/typography/projection.ts`:

```ts
import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { Editor } from '@tiptap/core'
import type { ScriptClass, TextMetrics } from '@markleaf/typography'

export type TypographyProjectionStatus =
  | 'disabled'
  | 'planned'
  | 'partial'
  | 'fallback'

export type TypographyProjectionDiagnostics = {
  status: TypographyProjectionStatus
  eligibleParagraphs: number
  plannedParagraphs: number
  fallbackParagraphs: number
  lineBreaks: number
}

export type TypographyMeasurement = (
  text: string,
  script: ScriptClass,
) => TextMetrics

export type TypographyProjectionOptions = {
  availableWidth: number
  measure: TypographyMeasurement
}

type ProjectionMeta = {
  enabled: boolean
  options?: TypographyProjectionOptions
}

type ProjectionState = {
  enabled: boolean
  options?: TypographyProjectionOptions
  diagnostics: TypographyProjectionDiagnostics
  decorations: DecorationSet
}

export const typographyProjectionKey = new PluginKey<ProjectionState>('markleaf-typography-projection')

const disabledState = (): ProjectionState => ({
  enabled: false,
  diagnostics: {
    status: 'disabled',
    eligibleParagraphs: 0,
    plannedParagraphs: 0,
    fallbackParagraphs: 0,
    lineBreaks: 0,
  },
  decorations: DecorationSet.empty,
})

export const TypographyProjection = Extension.create({
  name: 'markleafTypographyProjection',
  addProseMirrorPlugins() {
    return [new Plugin<ProjectionState>({
      key: typographyProjectionKey,
      state: {
        init: disabledState,
        apply: (transaction, previous) => {
          const meta = transaction.getMeta(typographyProjectionKey) as ProjectionMeta | undefined
          if (meta && !meta.enabled) return disabledState()
          return previous.decorations.map(transaction.mapping, transaction.doc)
            ? { ...previous, decorations: previous.decorations.map(transaction.mapping, transaction.doc) }
            : previous
        },
      },
      props: {
        decorations: state => state.decorations,
      },
    })]
  },
})

export function getTypographyProjectionDiagnostics(editor: Editor): TypographyProjectionDiagnostics {
  return typographyProjectionKey.getState(editor.state)?.diagnostics ?? disabledState().diagnostics
}
```

Modify `packages/editor-core/src/editor.ts`:

```ts
import { TypographyProjection } from './typography/projection'
```

Add `TypographyProjection` immediately after `CjkAutoSpacing` in `editorExtensions`.

Modify `packages/editor-core/src/index.ts`:

```ts
export * from './typography/projection'
```

- [x] **Step 5: Verify tests and typecheck pass**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core typecheck
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add packages/editor-core/package.json packages/editor-core/pnpm-lock.yaml packages/editor-core/src/editor.ts packages/editor-core/src/index.ts packages/editor-core/src/typography/projection.ts packages/editor-core/tests/typography-projection.test.ts
git commit -m "feat(typography): add disabled editor projection plugin"
```

---

### Task 2: Extract eligible top-level paragraphs and map grapheme offsets

**Files:**

- Modify: `packages/editor-core/src/typography/projection.ts`
- Modify: `packages/editor-core/tests/typography-projection.test.ts`

**Interfaces:**

- Consumes: ProseMirror `PMNode`, `splitGraphemes()` from `@markleaf/typography`.
- Produces: `ExtractedTypographyParagraph` and `extractTypographyParagraphs()`, with exact fields:

```ts
export type ExtractedTypographyParagraph = {
  from: number
  to: number
  text: string
  clusterOffsets: number[]
  eligible: boolean
  fallbackReason?: 'unsupported-block' | 'inline-atom' | 'inline-code'
}
```

- [x] **Step 1: Write extraction tests**

Append to `packages/editor-core/tests/typography-projection.test.ts`:

```ts
import {
  extractTypographyParagraphs,
  getTypographyProjectionDiagnostics,
} from '../src/index'
import type { PMNode } from '@tiptap/pm/model'

describe('typography paragraph extraction', () => {
  it('extracts top-level paragraphs and maps grapheme offsets to PM positions', () => {
    const { editor } = setup('中文\n\nA👍B')
    const paragraphs = extractTypographyParagraphs(editor.state.doc)

    expect(paragraphs).toHaveLength(2)
    expect(paragraphs[0]).toMatchObject({ from: 1, to: 2, text: '中文', eligible: true })
    expect(paragraphs[0].clusterOffsets).toEqual([0, 1, 2])
    expect(paragraphs[1]).toMatchObject({ from: 4, to: 7, text: 'A👍B', eligible: true })
    expect(paragraphs[1].clusterOffsets).toEqual([0, 1, 3, 4])
  })

  it('marks unsupported blocks and inline atoms as fallbacks', () => {
    const { editor } = setup('# Title\n\nbefore ![image](image.png) after')
    const paragraphs = extractTypographyParagraphs(editor.state.doc)

    expect(paragraphs[0]).toMatchObject({
      eligible: false,
      fallbackReason: 'unsupported-block',
    })
    expect(paragraphs[1]).toMatchObject({
      eligible: false,
      fallbackReason: 'inline-atom',
    })
  })
})
```

If `PMNode` is not needed directly in the test file, omit that import. The projection function accepts `editor.state.doc`.

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
```

Expected: FAIL because `extractTypographyParagraphs` does not exist.

- [x] **Step 3: Implement extraction**

Add imports in `projection.ts`:

```ts
import type { Node as PMNode } from '@tiptap/pm/model'
import { splitGraphemes } from '@markleaf/typography'
```

Add the public type and implementation:

```ts
export type ExtractedTypographyParagraph = {
  from: number
  to: number
  text: string
  clusterOffsets: number[]
  eligible: boolean
  fallbackReason?: 'unsupported-block' | 'inline-atom' | 'inline-code'
}

export function extractTypographyParagraphs(doc: PMNode): ExtractedTypographyParagraph[] {
  const paragraphs: ExtractedTypographyParagraph[] = []

  doc.forEach((node, offset) => {
    const from = offset + 1
    const to = offset + node.nodeSize - 1
    const paragraph: ExtractedTypographyParagraph = {
      from,
      to,
      text: node.textContent,
      clusterOffsets: [0],
      eligible: node.type.name === 'paragraph',
      fallbackReason: node.type.name === 'paragraph' ? undefined : 'unsupported-block',
    }

    if (paragraph.eligible) {
      let hasInlineAtom = false
      let hasInlineCode = false
      node.forEach(child => {
        if (!child.isText) hasInlineAtom = true
        if (child.marks.some(mark => mark.type.name === 'code')) hasInlineCode = true
      })
      if (hasInlineAtom) {
        paragraph.eligible = false
        paragraph.fallbackReason = 'inline-atom'
      } else if (hasInlineCode) {
        paragraph.eligible = false
        paragraph.fallbackReason = 'inline-code'
      }
    }

    if (paragraph.eligible) {
      for (const cluster of splitGraphemes(paragraph.text)) {
        const previous = paragraph.clusterOffsets.at(-1) ?? 0
        paragraph.clusterOffsets.push(previous + cluster.length)
      }
    }

    paragraphs.push(paragraph)
  })

  return paragraphs
}
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/editor-core/src/typography/projection.ts packages/editor-core/tests/typography-projection.test.ts
git commit -m "feat(typography): extract eligible editor paragraphs"
```

---

### Task 3: Build LayoutPlan decorations for enabled paragraphs

**Files:**

- Modify: `packages/editor-core/src/typography/projection.ts`
- Modify: `packages/editor-core/tests/typography-projection.test.ts`

**Interfaces:**

- Consumes: `extractTypographyParagraphs()`, `layoutParagraph()` from `@markleaf/typography`.
- Produces: enabled-state decorations, diagnostics, and `setTypographyProjectionEnabled()`.

- [x] **Step 1: Write enabled projection tests**

Append to `packages/editor-core/tests/typography-projection.test.ts`:

```ts
import {
  setTypographyProjectionEnabled,
  getMarkdown,
} from '../src/index'

const fixedMeasure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('enabled typography projection', () => {
  it('plans line breaks without changing Markdown', () => {
    const { editor } = setup('中文 English 中文 English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 120,
      measure: fixedMeasure,
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('planned')
    expect(diagnostics.eligibleParagraphs).toBe(1)
    expect(diagnostics.plannedParagraphs).toBe(1)
    expect(diagnostics.lineBreaks).toBeGreaterThan(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-planned')).toHaveLength(1)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break').length).toBeGreaterThan(0)
    expect(getMarkdown(editor)).toBe('中文 English 中文 English')
  })

  it('returns to disabled state and removes decorations', () => {
    const { editor } = setup('中文 English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 120,
      measure: fixedMeasure,
    })
    setTypographyProjectionEnabled(editor, false)

    expect(getTypographyProjectionDiagnostics(editor).status).toBe('disabled')
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })
})
```

- [x] **Step 2: Verify the enabled test fails**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
```

Expected: FAIL because `setTypographyProjectionEnabled` does not exist.

- [x] **Step 3: Implement enabled projection**

Modify imports in `projection.ts`:

```ts
import { layoutParagraph } from '@markleaf/typography'
import type { LayoutPlan } from '@markleaf/typography'
import { Decoration } from '@tiptap/pm/view'
```

Add internal types and builder:

```ts
type PlannedParagraph = {
  from: number
  plan: LayoutPlan
}

function projectionStateFor(
  doc: PMNode,
  enabled: boolean,
  options?: TypographyProjectionOptions,
): ProjectionState {
  if (!enabled || !options || !Number.isFinite(options.availableWidth) || options.availableWidth <= 0) {
    return disabledState()
  }

  const decorations: Decoration[] = []
  const paragraphs = extractTypographyParagraphs(doc)
  let planned = 0
  let fallback = 0
  let lineBreaks = 0

  for (const paragraph of paragraphs) {
    if (!paragraph.eligible) {
      fallback += 1
      continue
    }

    const plan = layoutParagraph({
      text: paragraph.text,
      availableWidth: options.availableWidth,
      measure: options.measure,
    })

    if (plan.fallback || plan.lines.length === 0) {
      fallback += 1
      continue
    }

    planned += 1
    lineBreaks += plan.lines.length - 1
    decorations.push(Decoration.node(
      paragraph.from - 1,
      paragraph.to + 1,
      { class: 'markleaf-typography-planned' },
    ))

    for (const line of plan.lines.slice(0, -1)) {
      const position = paragraph.from + (paragraph.clusterOffsets[line.to] ?? paragraph.text.length)
      decorations.push(Decoration.widget(
        position,
        () => {
          const element = document.createElement('span')
          element.className = 'markleaf-typography-break'
          element.setAttribute('aria-hidden', 'true')
          element.setAttribute('contenteditable', 'false')
          return element
        },
        {
          side: 1,
          ignoreSelection: true,
          marks: [],
          key: `typography-break-${paragraph.from}-${line.to}`,
        },
      ))
    }
  }

  const status: TypographyProjectionStatus = planned === 0
    ? 'fallback'
    : fallback === 0 ? 'planned' : 'partial'

  return {
    enabled: true,
    options,
    diagnostics: {
      status,
      eligibleParagraphs: paragraphs.filter(paragraph => paragraph.eligible).length,
      plannedParagraphs: planned,
      fallbackParagraphs: fallback,
      lineBreaks,
    },
    decorations: DecorationSet.create(doc, decorations),
  }
}
```

Add the public setter:

```ts
export function setTypographyProjectionEnabled(
  editor: Editor,
  enabled: boolean,
  options?: TypographyProjectionOptions,
): void {
  editor.view.dispatch(editor.state.tr
    .setMeta(typographyProjectionKey, { enabled, options })
    .setMeta('addToHistory', false))
}
```

Replace the plugin `apply` implementation with:

```ts
apply: (transaction, previous) => {
  const meta = transaction.getMeta(typographyProjectionKey) as ProjectionMeta | undefined
  if (meta) {
    return projectionStateFor(transaction.doc, meta.enabled, meta.options)
  }
  if (!previous.enabled || !previous.options) return previous
  if (transaction.docChanged) {
    return projectionStateFor(transaction.doc, true, previous.options)
  }
  return {
    ...previous,
    decorations: previous.decorations.map(transaction.mapping, transaction.doc),
  }
}
```

- [x] **Step 4: Verify projection tests pass**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core typecheck
```

Expected: PASS.

- [x] **Step 5: Run shared editing regression contract**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/interaction-contracts.test.ts
```

Expected: PASS; plugin disabled state must not alter existing interactions.

- [x] **Step 6: Commit**

```bash
git add packages/editor-core/src/typography/projection.ts packages/editor-core/tests/typography-projection.test.ts
git commit -m "feat(typography): project layout plans as editor decorations"
```

---

### Task 4: Guard unsupported content and document changes

**Files:**

- Modify: `packages/editor-core/tests/typography-projection.test.ts`

This task intentionally changes only tests. If a test reveals a defect in Task 3, fix `projection.ts` in the same commit.

**Interfaces:**

- Consumes: the completed projection state machine.
- Produces: regression coverage for inline fallback and transaction remapping.

- [x] **Step 1: Add fallback and update tests**

Append to `packages/editor-core/tests/typography-projection.test.ts`:

```ts
it('marks paragraphs with inline code as fallbacks', () => {
  const { editor } = setup('中文 `code` 中文')
  setTypographyProjectionEnabled(editor, true, {
    availableWidth: 120,
    measure: fixedMeasure,
  })

  const diagnostics = getTypographyProjectionDiagnostics(editor)
  expect(diagnostics.status).toBe('fallback')
  expect(diagnostics.fallbackParagraphs).toBe(1)
  expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
})

it('recomputes decorations after document changes', () => {
  const { editor } = setup('中文 English')
  setTypographyProjectionEnabled(editor, true, {
    availableWidth: 120,
    measure: fixedMeasure,
  })
  const before = getTypographyProjectionDiagnostics(editor).lineBreaks

  editor.commands.insertContentAt(editor.state.doc.content.size, '\n\n中文 English 中文 English')
  const after = getTypographyProjectionDiagnostics(editor)

  expect(after.eligibleParagraphs).toBe(2)
  expect(after.lineBreaks).toBeGreaterThan(before)
})

it('keeps typography projections out of undo history', () => {
  const { editor } = setup('中文 English')
  const before = getMarkdown(editor)
  setTypographyProjectionEnabled(editor, true, {
    availableWidth: 120,
    measure: fixedMeasure,
  })
  editor.commands.undo()

  expect(getMarkdown(editor)).toBe(before)
  expect(editor.view.dom.querySelectorAll('.markleaf-typography-break').length).toBeGreaterThan(0)
})
```

- [x] **Step 2: Run the safety suite**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core typecheck
```

Expected: PASS.

- [x] **Step 3: Fix any projection defect and verify red/green intent**

If a new assertion passes without code change, retain it as regression coverage. If it fails, correct `projection.ts`, rerun the test, and ensure the final targeted suite passes.

- [x] **Step 4: Commit**

```bash
git add packages/editor-core/src/typography/projection.ts packages/editor-core/tests/typography-projection.test.ts
git commit -m "test(typography): guard editor projection fallbacks"
```

---

### Task 5: Add controlled projection CSS

**Files:**

- Modify: `packages/editor-core/src/styles.css`

**Interfaces:**

- Consumes: classes emitted by `TypographyProjection`.
- Produces: controlled no-natural-wrap behavior and non-copying break widgets.

- [x] **Step 1: Add typography projection styles**

Append to `packages/editor-core/src/styles.css`:

```css
/* Typography Phase 1A: explicit LayoutPlan projection. Disabled by default;
   class is added only through the explicit setTypographyProjection command. */
.markleaf-typography-planned {
  white-space: nowrap !important;
  overflow-wrap: normal !important;
  word-break: normal !important;
  text-align: left !important;
}

.markleaf-typography-break {
  display: block;
  width: 0;
  height: 0;
  margin: 0;
  padding: 0;
  border: 0;
  overflow: hidden;
  pointer-events: none;
  user-select: none;
}
```

- [x] **Step 2: Verify projection styling does not regress tests**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core exec vitest run tests/presentation-contracts.test.ts
```

Expected: PASS.

- [x] **Step 3: Commit**

```bash
git add packages/editor-core/src/styles.css
git commit -m "feat(typography): style controlled editor projection"
```

---

### Task 6: Add WebView measurement and explicit command plumbing

**Files:**

- Create: `packages/editor-core/src/typography/projection-measure.ts`
- Create: `packages/editor-core/src/typography/projection-command.ts`
- Modify: `packages/editor-core/src/index.ts`
- Modify: `packages/editor-web/src/protocol.ts`
- Modify: `packages/editor-web/src/main.ts`
- Create: `packages/editor-web/tests/typography-command.test.ts`

**Interfaces:**

- Consumes: `TypographyProjectionOptions`, diagnostics, and extraction context.
- Produces:

```ts
function createDomTypographyMeasurement(
  getReference: () => HTMLElement | null,
): TypographyMeasurement;

function parseTypographyProjectionCommand(payload: unknown): TypographyProjectionCommand | null;

type TypographyProjectionCommand = {
  enabled: boolean;
};
```

- [x] **Step 1: Write command parsing tests**

Create `packages/editor-web/tests/typography-command.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { parseTypographyProjectionCommand } from '@markleaf/editor-core'

describe('typography projection command', () => {
  it('accepts explicit on and off commands', () => {
    expect(parseTypographyProjectionCommand({ command: 'setTypographyProjection', text: '1' })).toEqual({ enabled: true })
    expect(parseTypographyProjectionCommand({ command: 'setTypographyProjection', text: '0' })).toEqual({ enabled: false })
  })

  it('rejects missing or invalid commands', () => {
    expect(parseTypographyProjectionCommand(undefined)).toBeNull()
    expect(parseTypographyProjectionCommand({ command: 'other', text: '1' })).toBeNull()
    expect(parseTypographyProjectionCommand({ command: 'setTypographyProjection', text: 'yes' })).toBeNull()
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/editor-web exec vitest run tests/typography-command.test.ts
```

Expected: FAIL because the parser is not exported.

- [x] **Step 3: Implement command parsing**

Create `packages/editor-core/src/typography/projection-command.ts`:

```ts
export type TypographyProjectionCommand = {
  enabled: boolean
}

export function parseTypographyProjectionCommand(payload: unknown): TypographyProjectionCommand | null {
  if (!payload || typeof payload !== 'object') return null
  const record = payload as Record<string, unknown>
  if (record.command !== 'setTypographyProjection') return null
  if (record.text === '1') return { enabled: true }
  if (record.text === '0') return { enabled: false }
  return null
}
```

- [x] **Step 4: Implement the WebView measurement adapter**

Create `packages/editor-core/src/typography/projection-measure.ts`:

```ts
import type { ScriptClass, TextMetrics } from '@markleaf/typography'
import type { TypographyMeasurement } from './projection'

export function createDomTypographyMeasurement(
  getReference: () => HTMLElement | null,
): TypographyMeasurement {
  const host = document.createElement('div')
  host.className = 'markleaf-typography-measure'
  host.setAttribute('aria-hidden', 'true')
  host.append(document.createElement('span'))
  document.body.append(host)

  return (text: string, script: ScriptClass): TextMetrics => {
    const reference = getReference()
    const referenceStyle = reference ? window.getComputedStyle(reference) : null
    if (referenceStyle) {
      host.style.fontFamily = referenceStyle.fontFamily
      host.style.fontSize = referenceStyle.fontSize
      host.style.fontWeight = referenceStyle.fontWeight
      host.style.fontStyle = referenceStyle.fontStyle
      host.style.lineHeight = referenceStyle.lineHeight
      host.style.letterSpacing = referenceStyle.letterSpacing
    }

    const child = host.firstElementChild as HTMLElement
    child.textContent = text
    const width = child.getBoundingClientRect().width

    if (text === '') {
      const fontSize = Number.parseFloat(referenceStyle?.fontSize ?? '16') || 16
      return { width: 0, stretch: fontSize * 0.25, shrink: fontSize * 0.125 }
    }
    if (script === 'space') {
      return { width, stretch: width * 0.5, shrink: width * 0.25 }
    }
    if (script === 'cjk') {
      return { width, stretch: width * 0.04, shrink: width * 0.02 }
    }
    return { width, stretch: 0, shrink: 0 }
  }
}
```

Modify `packages/editor-core/src/index.ts`:

```ts
export * from './typography/projection-command'
export * from './typography/projection-measure'
```

- [x] **Step 5: Add the diagnostics message type**

Modify `EditorMessage['type']` in `packages/editor-web/src/protocol.ts` by adding:

```ts
| 'typographyProjectionChanged'
```

Keep all existing types unchanged.

- [x] **Step 6: Wire the command gate in `main.ts`**

Extend the imports from `@markleaf/editor-core`:

```ts
import {
  createDomTypographyMeasurement,
  getTypographyProjectionDiagnostics,
  parseTypographyProjectionCommand,
  setTypographyProjectionEnabled,
} from '@markleaf/editor-core'
```

Add state and helper near the source-mode state declarations:

```ts
let typographyProjectionPreferred = false
let typographyProjectionEnabled = false
const typographyMeasurement = createDomTypographyMeasurement(
  () => document.querySelector<HTMLElement>('.markleaf-document') ?? editorMount,
)

function typographyProjectionAllowed(): boolean {
  return !sourceMode && documentType === 'markdown'
}

function updateTypographyProjection(): void {
  const enabled = typographyProjectionPreferred && typographyProjectionAllowed()
  const content = document.querySelector<HTMLElement>('.markleaf-document') ?? editorMount
  setTypographyProjectionEnabled(editor, enabled, {
    availableWidth: content.clientWidth || editorMount.clientWidth,
    measure: typographyMeasurement,
  })
  typographyProjectionEnabled = enabled
  send('typographyProjectionChanged', getTypographyProjectionDiagnostics(editor))
}
```

In the command handler, before the generic `executeEditorCommand` fallback, add:

```ts
if (payload.command === 'setTypographyProjection') {
  const command = parseTypographyProjectionCommand(payload)
  if (command) {
    typographyProjectionPreferred = command.enabled
    updateTypographyProjection()
    if (message.requestId) send('commandResult', { success: true }, message.requestId)
    break
  }
}
```

Call `updateTypographyProjection()`:

1. after document load completes;
2. after plain-text/markdown `setDocumentType` switching completes;
3. when entering source mode;
4. after leaving source mode and the visual editor has been recreated.

If an existing code path already calls `sendEditorState()` at those boundaries, insert `updateTypographyProjection()` immediately before it.

- [x] **Step 7: Add measurement host CSS**

Append to `packages/editor-core/src/styles.css`:

```css
.markleaf-typography-measure {
  position: absolute;
  top: -10000px;
  left: -10000px;
  visibility: hidden;
  pointer-events: none;
  white-space: pre;
}
```

- [x] **Step 8: Verify command, protocol, and bundle integration**

Run:

```bash
corepack pnpm --dir packages/editor-web exec vitest run tests/typography-command.test.ts
corepack pnpm --dir packages/editor-web exec vitest run tests/protocol.test.ts
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core typecheck
corepack pnpm --dir packages/editor-web typecheck
corepack pnpm build:editor-web
```

Expected: PASS.

- [x] **Step 9: Commit**

```bash
git add packages/editor-core/src/index.ts packages/editor-core/src/styles.css packages/editor-core/src/typography/projection-command.ts packages/editor-core/src/typography/projection-measure.ts packages/editor-web/src/main.ts packages/editor-web/src/protocol.ts packages/editor-web/tests/typography-command.test.ts
git commit -m "feat(typography): add gated editor projection command"
```

---

### Task 7: Phase 1A integration gate

**Files:**

- No required changes.

**Interfaces:**

- Consumes: all Phase 1A APIs and build outputs.
- Produces: verified branch state ready for review.

- [x] **Step 1: Run targeted suites**

Run:

```bash
corepack pnpm --dir packages/typography test
corepack pnpm --dir packages/typography typecheck
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core exec vitest run tests/interaction-contracts.test.ts
corepack pnpm --dir packages/editor-core exec vitest run tests/presentation-contracts.test.ts
corepack pnpm --dir packages/editor-web exec vitest run tests/typography-command.test.ts
corepack pnpm --dir packages/editor-web exec vitest run tests/protocol.test.ts
corepack pnpm --dir packages/editor-core typecheck
corepack pnpm --dir packages/editor-web typecheck
```

Expected: PASS.

- [x] **Step 2: Verify no default product behavior changed**

Run:

```bash
git diff --check
git status --short
rg -n "setTypographyProjectionPreferred|typographyProjectionPreferred\\s*=\\s*true" apps packages --glob '!**/dist/**' --glob '!**/node_modules/**'
```

Expected: clean diff; only unrelated pre-existing untracked files remain; no host calls the command with `1`, so the product remains disabled by default.

- [x] **Step 3: Inspect the phase boundary**

Run:

```bash
git diff --stat main...HEAD
```

Expected: changes are limited to `packages/editor-core`, `packages/editor-web`, and this plan/documentation. No `apps/macos` UI, `apps/windows` UI, or export behavior changes.

- [x] **Step 4: Commit any final gate corrections**

If verification required changes, stage only those files and commit:

```bash
git add <changed-files>
git commit -m "fix(typography): stabilize editor projection gate"
```

If no changes are required, do not create an empty commit.

---

## Plan Boundary

This plan intentionally does **not** implement:

- user preferences or menu UI;
- host persistence of typography settings;
- visible viewport scheduling or idle work;
- scroll anchoring;
- CJK punctuation compression or hanging decorations;
- inline spacing adjustments;
- export projection;
- VS Code integration;
- default enablement.

Those belong to later Phase 1B+ plans after this projection experiment is manually reviewed in the running macOS and Windows applications.
