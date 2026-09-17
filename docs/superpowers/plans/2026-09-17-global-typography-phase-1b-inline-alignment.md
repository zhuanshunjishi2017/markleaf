# Global Typography Phase 1B Inline Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the default-disabled editor projection from explicit line breaks to actual per-line alignment by distributing signed LayoutPlan adjustment across word spaces or grapheme clusters, with responsive width recomputation.

**Architecture:** The pure kernel will expose a signed `adjustment` plus `spaceCount` on each `TypographyLine`. The editor projection will decorate non-final lines with CSS variables: lines containing spaces use `word-spacing`; CJK/direct-mixed lines without spaces use compensated `letter-spacing`. `editor-web` will observe editor width changes and debounce projection recomputation while retaining its explicit command gate and default-off behavior.

**Tech Stack:** TypeScript 5.9, ProseMirror decorations, CSS custom properties, ResizeObserver, Vitest 4, jsdom.

**Spec:** [2026-09-17-global-typography-engine-design.md](../specs/2026-09-17-global-typography-engine-design.md)

## Global Constraints

- Phase 1B remains an experiment: no preference UI, no default enablement, no export projection, no scheduler, and no VS Code integration.
- Typography decorations must continue to leave document JSON, Markdown serialization, copied text, and undo history unchanged.
- Only non-final planned lines are justified; the paragraph-ending line remains naturally aligned.
- Inline code, inline atoms, tables, headings, source mode, and plain-text documents remain fallbacks or disabled.
- The legacy CJK autospace widget remains hidden in planned paragraphs.
- Do not run the full macOS test suite.

---

## File Structure

```text
packages/typography/
├── src/model.ts
├── src/breaker.ts
└── tests/breaker.test.ts

packages/editor-core/
├── src/typography/projection.ts
├── src/styles.css
└── tests/typography-projection.test.ts

packages/editor-web/
└── src/main.ts
```

## Public Contract

```ts
export type TypographyLineAdjustment = {
  type: 'natural' | 'stretch' | 'shrink' | 'overflow';
  // Signed width added by justification: negative shrinks, positive stretches.
  amount: number;
};

export type TypographyLine = {
  // Existing fields...
  adjustment: TypographyLineAdjustment;
  spaceCount: number;
};
```

The editor emits, for every non-final planned line:

```html
<span
  class="markleaf-typography-line markleaf-typography-line-words"
  data-markleaf-typography-line="true"
  style="
    --ml-typography-line-adjust: 3.5px;
    --ml-typography-space-count: 2;
    --ml-typography-spacing-points: 2;
  "
></span>
```

`spaceCount` is 0 for character-spaced lines. `spacingPoints` is `spaceCount` when positive, otherwise `clusterCount - 1`.

---

### Task 1: Add signed adjustment and space count to LayoutPlan lines

**Files:**

- Modify: `packages/typography/src/model.ts`
- Modify: `packages/typography/src/breaker.ts`
- Modify: `packages/typography/tests/breaker.test.ts`

**Interfaces:**

- Consumes: existing `lineAdjustment()` and paragraph glue items.
- Produces:

```ts
TypographyLine.adjustment: TypographyLineAdjustment
TypographyLine.spaceCount: number
TypographyLine.contentTo: number
```

- [x] **Step 1: Write failing kernel assertions**

Modify the narrow multiline test in `packages/typography/tests/breaker.test.ts`:

```ts
const adjustedLines = result.lines.slice(0, -1)
expect(adjustedLines.length).toBeGreaterThan(0)
expect(adjustedLines.every((line) => line.adjustment.type !== 'overflow')).toBe(true)
expect(adjustedLines.every((line) => line.naturalWidth + line.adjustment.amount === line.adjustedWidth)).toBe(true)
expect(adjustedLines.every((line) => Number.isInteger(line.spaceCount))).toBe(true)
expect(adjustedLines.every((line) => line.contentTo <= line.to)).toBe(true)
```

Also add:

```ts
expect(result.lines.at(-1)!.adjustment.type).toBe('natural')
expect(result.lines.at(-1)!.adjustment.amount).toBe(0)
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/breaker.test.ts
```

Expected: FAIL because `adjustment` and `spaceCount` do not exist.

- [x] **Step 3: Extend the line model**

Modify `packages/typography/src/model.ts`:

```ts
export type TypographyLineAdjustment = {
  type: 'natural' | 'stretch' | 'shrink' | 'overflow';
  amount: number;
};

export type TypographyLine = {
  from: number;
  to: number;
  contentTo: number;
  naturalWidth: number;
  adjustedWidth: number;
  adjustment: TypographyLineAdjustment;
  spaceCount: number;
  badness: number;
  breakReason: 'optimal' | 'forced' | 'paragraph-end';
};
```

- [x] **Step 4: Calculate signed adjustment and count spaces**

Modify `makeLine()` in `packages/typography/src/breaker.ts`. After calculating `adjustment`, add:

```ts
const adjustmentAmount = availableWidth - metrics.naturalWidth
const adjustmentType = adjustment.type
let spaceCount = 0

for (let index = fromIndex; index <= toIndex; index += 1) {
  const item = items[index]
  if (item?.kind === 'glue' && item.script === 'space') {
    spaceCount += Array.from(item.text).length
  }
}

const contentTo = items[toIndex]?.to ?? to
```

Return the line as:

```ts
{
      from,
      to,
      contentTo,
      naturalWidth: metrics.naturalWidth,
      adjustedWidth: metrics.naturalWidth + (
        adjustmentType === 'stretch' || adjustmentType === 'shrink'
          ? adjustmentAmount
          : 0
      ),
  adjustment: { type: adjustmentType, amount: adjustmentAmount },
  spaceCount,
  badness: lineBadness,
  breakReason: reason,
}
```

For paragraph-ending lines, keep the existing underfull badness exception, but still report the signed natural delta. `contentTo` excludes line-break spaces after the final content item. The editor only applies adjustments to non-final lines.

- [x] **Step 5: Verify kernel tests**

Run:

```bash
corepack pnpm --dir packages/typography test
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add packages/typography/src/model.ts packages/typography/src/breaker.ts packages/typography/tests/breaker.test.ts
git commit -m "feat(typography): expose line adjustment metadata"
```

---

### Task 2: Project per-line word and character spacing

**Files:**

- Modify: `packages/editor-core/src/typography/projection.ts`
- Modify: `packages/editor-core/src/styles.css`
- Modify: `packages/editor-core/tests/typography-projection.test.ts`

**Interfaces:**

- Consumes: `TypographyLine.adjustment`, `TypographyLine.spaceCount`, paragraph cluster offsets.
- Produces: one inline decoration for each non-final planned line with CSS variables and diagnostic attributes.

- [x] **Step 1: Write failing projection assertions**

Modify the existing “plans line breaks without changing Markdown” test. After the break-widget assertions, add:

```ts
const adjustedLines = editor.view.dom.querySelectorAll('[data-markleaf-typography-line="true"]')
expect(adjustedLines.length).toBe(diagnostics.lineBreaks)
expect(adjustedLines.length).toBeGreaterThan(0)
expect(Array.from(adjustedLines).every((line) => line.classList.contains('markleaf-typography-line'))).toBe(true)
expect(Array.from(adjustedLines).every((line) => line.hasAttribute('data-markleaf-typography-space-count'))).toBe(true)
```

Add a separate test for a CJK-only paragraph:

```ts
it('uses character spacing for CJK lines without spaces', () => {
  const { editor } = setup('中文English中文English中文English中文English')
  setTypographyProjectionEnabled(editor, true, {
    availableWidth: 240,
    measure: fixedMeasure,
  })

  const lines = Array.from(
    editor.view.dom.querySelectorAll<HTMLElement>('[data-markleaf-typography-line="true"]'),
  )
  expect(lines.length).toBeGreaterThan(0)
  expect(lines.every((line) => line.classList.contains('markleaf-typography-line-characters'))).toBe(true)
})
```

- [x] **Step 2: Verify projection tests fail**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
```

Expected: FAIL because line-spacing decorations are not emitted.

- [x] **Step 3: Build line decorations**

Modify `projectionStateFor()` in `packages/editor-core/src/typography/projection.ts`.

Inside the eligible-paragraph loop, replace the current `for (const line of plan.lines.slice(0, -1))` loop with:

```ts
for (const line of plan.lines.slice(0, -1)) {
  const fromOffset = paragraph.clusterOffsets[line.from] ?? 0
  const toOffset = paragraph.clusterOffsets[line.to] ?? paragraph.text.length
  const fromPosition = paragraph.from + fromOffset
  const toPosition = paragraph.from + toOffset
  const contentFromOffset = paragraph.clusterOffsets[line.from] ?? 0
  const contentPosition = paragraph.from + (paragraph.clusterOffsets[line.contentTo] ?? paragraph.text.length)
  const clusterCount = line.contentTo - line.from
  const spacingPoints = line.spaceCount > 0
    ? line.spaceCount
    : Math.max(clusterCount - 1, 1)
  const lineClass = line.spaceCount > 0
    ? 'markleaf-typography-line markleaf-typography-line-words'
    : 'markleaf-typography-line markleaf-typography-line-characters'

  decorations.push(Decoration.inline(
    fromPosition,
    contentPosition,
    {
      class: lineClass,
      'data-markleaf-typography-line': 'true',
      'data-markleaf-typography-space-count': String(line.spaceCount),
      style: [
        `--ml-typography-line-adjust:${line.adjustment.amount}px`,
        `--ml-typography-space-count:${line.spaceCount}`,
        `--ml-typography-spacing-points:${spacingPoints}`,
      ].join(';'),
    },
    {
    key: `typography-line-${paragraph.from}-${line.from}-${line.to}`,
    },
  ))

  decorations.push(Decoration.widget(
    toPosition,
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
```

Remove the now-unused `PlannedParagraph` type if present.

- [x] **Step 4: Add alignment CSS**

Append to `packages/editor-core/src/styles.css`:

```css
.markleaf-typography-line-words {
  word-spacing: calc(
    var(--ml-typography-line-adjust) /
    max(var(--ml-typography-space-count), 1)
  ) !important;
}

.markleaf-typography-line-characters {
  letter-spacing: calc(
    var(--ml-typography-line-adjust) /
    max(var(--ml-typography-spacing-points), 1)
  ) !important;
  margin-right: calc(
    -1 * var(--ml-typography-line-adjust) /
    max(var(--ml-typography-spacing-points), 1)
  ) !important;
}
```

- [x] **Step 5: Verify projection and regressions**

Run:

```bash
corepack pnpm --dir packages/editor-core exec vitest run tests/typography-projection.test.ts
corepack pnpm --dir packages/editor-core exec vitest run tests/interaction-contracts.test.ts
corepack pnpm --dir packages/editor-core typecheck
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add packages/editor-core/src/typography/projection.ts packages/editor-core/src/styles.css packages/editor-core/tests/typography-projection.test.ts
git commit -m "feat(typography): align projected editor lines"
```

---

### Task 3: Recompute projection when editor width changes

**Files:**

- Modify: `packages/editor-web/src/main.ts`

**Interfaces:**

- Consumes: existing `updateTypographyProjection()` and explicit command gate.
- Produces: debounced width-change recomputation.

- [x] **Step 1: Add debounce state and helper**

Near the typography projection state in `packages/editor-web/src/main.ts`, add:

```ts
let typographyResizeTimer = 0

function scheduleTypographyProjectionUpdate(): void {
  window.clearTimeout(typographyResizeTimer)
  typographyResizeTimer = window.setTimeout(() => {
    typographyResizeTimer = 0
    updateTypographyProjection()
  }, 80)
}
```

After `updateTypographyProjection()` is defined, add:

```ts
const typographyResizeObserver = new ResizeObserver(() => {
  scheduleTypographyProjectionUpdate()
})
typographyResizeObserver.observe(editorMount)
window.addEventListener('resize', scheduleTypographyProjectionUpdate)
```

- [x] **Step 2: Avoid redundant work when mode switching replaces the editor**

No new observer should be attached in `replaceEditorDocument`; the existing observer watches the persistent `editorMount`. Do not observe `.markleaf-document` directly.

- [x] **Step 3: Verify syntax and integration**

Run:

```bash
corepack pnpm --dir packages/editor-web typecheck
corepack pnpm --dir packages/editor-web exec vitest run tests/typography-command.test.ts
corepack pnpm build:editor-web
```

Expected: PASS.

- [x] **Step 4: Commit**

```bash
git add packages/editor-web/src/main.ts
git commit -m "feat(typography): recompute projection on width changes"
```

---

### Task 4: Phase 1B integration gate

**Files:**

- No required changes.

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
corepack pnpm build:editor-web
```

Expected: PASS.

- [x] **Step 2: Audit default-off and phase boundary**

Run:

```bash
git diff --check
git status --short
rg -n "typographyProjectionPreferred\\s*=\\s*true" apps packages --glob '!**/dist/**' --glob '!**/node_modules/**'
git diff --stat main...HEAD
```

Expected: clean diff, clean worktree, no default true assignment, and changes limited to shared packages plus documentation.

- [x] **Step 3: Commit only if the gate required corrections**

If corrections were needed:

```bash
git add <changed-files>
git commit -m "fix(typography): stabilize inline projection alignment"
```

If no corrections were needed, do not create an empty commit.

---

## Plan Boundary

This plan does **not** implement:

- CJK punctuation compression or hanging punctuation;
- styled-run-aware measurement;
- idle scheduling or viewport prioritization;
- scroll anchoring;
- user preferences or host persistence;
- PDF/image/HTML export;
- VS Code integration;
- default enablement.
