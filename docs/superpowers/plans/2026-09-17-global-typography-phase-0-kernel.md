# Global Typography Phase 0 Kernel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure, host-independent `@markleaf/typography` kernel that converts text and deterministic metrics into Knuth–Plass `LayoutPlan` results, without changing product behavior.

**Architecture:** Create a new pure TypeScript package with no DOM, ProseMirror, Node, macOS, or Windows dependencies. The package implements script/punctuation classification, metric and badness calculations, paragraph item construction, global line breaking, plan assembly, and bounded caches. Later plans will integrate this kernel with ProseMirror, WebView measurement, exports, settings, and host applications.

**Tech Stack:** TypeScript 5.9, ES2022, Vitest 4, pnpm 11.

**Spec:** [2026-09-17-global-typography-engine-design.md](../specs/2026-09-17-global-typography-engine-design.md)

## Global Constraints

- This is Phase 0 only: no editor, export, settings, or product behavior changes.
- Do not modify `packages/editor-core/src/typography.ts`; later integration will add a separate adapter.
- Do not import DOM, ProseMirror, `node:*`, macOS APIs, Windows APIs, or application settings.
- All public layout functions must be deterministic for the same input.
- User-visible text must never be changed by layout functions.
- Grapheme clusters, emoji surrogate pairs, and combining marks must not be split.
- Every fallback must return a stable reason code.
- Run only the new package tests and the repository typecheck; do not run the full macOS test suite.
- Existing untracked files under `docs/plans/` and `docs/superpowers/` must not be staged unless this plan creates them.

---

## File Structure

```text
packages/typography/
├── package.json
├── tsconfig.json
├── vitest.config.ts
└── src/
|   ├── index.ts
|   ├── model.ts
|   ├── script.ts
|   ├── punctuation.ts
|   ├── metrics.ts
|   ├── paragraph.ts
|   ├── breaker.ts
|   ├── plan.ts
|   └── cache.ts
└── tests/
    ├── model.test.ts
    ├── script.test.ts
    ├── punctuation.test.ts
    ├── metrics.test.ts
    ├── paragraph.test.ts
    ├── breaker.test.ts
    ├── plan.test.ts
    └── cache.test.ts
```

Responsibilities:

- `model.ts`: public input/output types and deterministic IDs.
- `script.ts`: character and grapheme classification.
- `punctuation.ts`: CJK punctuation, kinsoku, compression, and hanging eligibility.
- `metrics.ts`: line geometry, adjustment, badness, and demerits.
- `paragraph.ts`: converts text plus fake/real metrics into layout items and break candidates.
- `breaker.ts`: Knuth–Plass dynamic programming.
- `plan.ts`: validates and assembles `LayoutPlan`.
- `cache.ts`: bounded LRU caches.

## Types Used by Every Task

Later tasks consume these exact names from `src/model.ts`.

```ts
export type ScriptClass =
  | 'cjk'
  | 'latin'
  | 'digit'
  | 'space'
  | 'punctuation'
  | 'symbol'
  | 'unknown';

export type PunctuationCompression =
  | 'none'
  | 'half'
  | 'adjacent'
  | 'terminal';

export type LayoutItem =
  | {
      kind: 'box';
      text: string;
      script: ScriptClass;
      from: number;
      to: number;
      width: number;
    }
  | {
      kind: 'glue';
      text: string;
      script: ScriptClass;
      from: number;
      to: number;
      width: number;
      stretch: number;
      shrink: number;
    }
  | {
      kind: 'penalty';
      from: number;
      to: number;
      penalty: number;
      flagged: boolean;
    };

export type BreakCandidate = {
  itemIndex: number;
  sourceTo: number;
  penalty: number;
  flagged: boolean;
};

export type TypographyLine = {
  from: number;
  to: number;
  naturalWidth: number;
  adjustedWidth: number;
  badness: number;
  breakReason: 'optimal' | 'forced' | 'paragraph-end';
};

export type TypographyFallbackReason =
  | 'no-legal-break'
  | 'badness-exceeded'
  | 'atom-wider-than-line';

export type LayoutPlan = {
  lines: TypographyLine[];
  fallback: boolean;
  fallbackReason?: TypographyFallbackReason;
};

export type TextMetrics = {
  width: number;
  stretch: number;
  shrink: number;
};
```

---

### Task 1: Create the isolated typography package

**Files:**

- Create: `packages/typography/package.json`
- Create: `packages/typography/tsconfig.json`
- Create: `packages/typography/vitest.config.ts`
- Create: `packages/typography/src/model.ts`
- Create: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/model.test.ts`
- Modify: `package.json`

**Interfaces:**

- Consumes: nothing.
- Produces: the package `@markleaf/typography`, test command `test`, and the public types in “Types Used by Every Task”.

- [x] **Step 1: Write the package test**

Create `packages/typography/tests/model.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import type { LayoutPlan } from '../src/model'

describe('typography package', () => {
  it('exposes the public model types through a plan-shaped value', () => {
    const plan: LayoutPlan = {
      lines: [],
      fallback: false,
    }

    expect(plan).toEqual({ lines: [], fallback: false })
  })
})
```

- [x] **Step 2: Add package configuration**

Create `packages/typography/package.json`:

```json
{
  "name": "@markleaf/typography",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "packageManager": "pnpm@11.9.0",
  "exports": {
    ".": "./src/index.ts"
  },
  "scripts": {
    "typecheck": "tsc --noEmit",
    "test": "vitest run"
  },
  "devDependencies": {
    "typescript": "5.9.3",
    "vitest": "4.0.15"
  }
}
```

Create `packages/typography/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "noEmit": true,
    "lib": ["ES2022"],
    "types": ["vitest/globals"]
  },
  "include": ["src", "tests", "vitest.config.ts"]
}
```

Create `packages/typography/vitest.config.ts`:

```ts
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'node',
  },
})
```

- [x] **Step 3: Add the failing public entrypoint**

Create `packages/typography/src/index.ts`:

```ts
export * from './model'
```

Do not create `model.ts` yet.

- [x] **Step 4: Install dependencies and verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography install
corepack pnpm --dir packages/typography test
```

Expected: FAIL because `../src/model` does not exist.

- [x] **Step 5: Implement the public model**

Create `packages/typography/src/model.ts` with the complete type block from “Types Used by Every Task”.

- [x] **Step 6: Verify tests and typecheck pass**

Run:

```bash
corepack pnpm --dir packages/typography test
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 7: Register the root test command**

Modify root `package.json` by adding these two keys inside the existing `scripts` object. Keep every existing script unchanged:

```json
  "test:typography": "corepack pnpm --dir packages/typography test",
  "typecheck:typography": "corepack pnpm --dir packages/typography typecheck"
```

The resulting adjacent lines will look like this:

```json
  "typecheck:editor-core": "corepack pnpm --dir packages/editor-core typecheck",
  "test:editor-core": "corepack pnpm --dir packages/editor-core test",
  "test:typography": "corepack pnpm --dir packages/typography test",
  "typecheck:typography": "corepack pnpm --dir packages/typography typecheck"
```

```json
  "test:editor-web": "corepack pnpm --dir packages/editor-web test"
}
```

Run:

```bash
corepack pnpm test:typography
corepack pnpm typecheck:typography
```

Expected: PASS.

- [x] **Step 8: Commit**

```bash
git add package.json packages/typography/package.json packages/typography/tsconfig.json packages/typography/vitest.config.ts packages/typography/src/index.ts packages/typography/src/model.ts packages/typography/tests/model.test.ts packages/typography/pnpm-lock.yaml
git commit -m "feat(typography): add pure layout kernel package"
```

---

### Task 2: Classify scripts and preserve grapheme clusters

**Files:**

- Create: `packages/typography/src/script.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/script.test.ts`

**Interfaces:**

- Consumes: `ScriptClass` from `model.ts`.
- Produces:

```ts
function classifyCharacter(character: string): ScriptClass;
function splitGraphemes(text: string): string[];
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/script.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { classifyCharacter, splitGraphemes } from '../src/script'

describe('script classification', () => {
  it('classifies CJK, Latin, digits, spaces, and punctuation', () => {
    expect(classifyCharacter('中')).toBe('cjk')
    expect(classifyCharacter('A')).toBe('latin')
    expect(classifyCharacter('7')).toBe('digit')
    expect(classifyCharacter(' ')).toBe('space')
    expect(classifyCharacter('，')).toBe('punctuation')
    expect(classifyCharacter('+')).toBe('symbol')
  })

  it('keeps surrogate pairs and combining marks intact', () => {
    expect(splitGraphemes('👍abc')).toEqual(['👍', 'a', 'b', 'c'])
    expect(splitGraphemes('é')).toEqual(['é'])
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/script.test.ts
```

Expected: FAIL because `src/script.ts` does not exist.

- [x] **Step 3: Implement classification**

Create `packages/typography/src/script.ts`:

```ts
import type { ScriptClass } from './model'

const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' })

const cjkPattern =
  /[\u2e80-\u2eff\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uac00-\ud7af\u{20000}-\u{2fa1f}]/u

export function classifyCharacter(character: string): ScriptClass {
  if (/\s/u.test(character)) return 'space'
  if (cjkPattern.test(character)) return 'cjk'
  if (/[A-Za-z]/u.test(character)) return 'latin'
  if (/[0-9]/u.test(character)) return 'digit'
  if (/\p{P}/u.test(character)) return 'punctuation'
  if (/\p{S}/u.test(character)) return 'symbol'
  return 'unknown'
}

export function splitGraphemes(text: string): string[] {
  return Array.from(segmenter.segment(text), (segment) => segment.segment)
}
```

Modify `src/index.ts`:

```ts
export * from './model'
export * from './script'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/script.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/script.ts packages/typography/src/index.ts packages/typography/tests/script.test.ts
git commit -m "feat(typography): classify scripts and graphemes"
```

---

### Task 3: Implement CJK punctuation and kinsoku rules

**Files:**

- Create: `packages/typography/src/punctuation.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/punctuation.test.ts`

**Interfaces:**

- Consumes: `PunctuationCompression`, `ScriptClass`.
- Produces:

```ts
const lineStartForbidden: ReadonlySet<string>;
const lineEndForbidden: ReadonlySet<string>;
function compressionMode(character: string): PunctuationCompression;
function canHang(character: string): boolean;
function canBreakBefore(character: string): boolean;
function canBreakAfter(character: string): boolean;
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/punctuation.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import {
  canBreakAfter,
  canBreakBefore,
  canHang,
  compressionMode,
} from '../src/punctuation'

describe('CJK punctuation', () => {
  it('forbids closing punctuation at line start', () => {
    expect(canBreakBefore('，')).toBe(false)
    expect(canBreakBefore('。')).toBe(false)
    expect(canBreakBefore('）')).toBe(false)
    expect(canBreakBefore('中')).toBe(true)
  })

  it('forbids opening punctuation at line end', () => {
    expect(canBreakAfter('（')).toBe(false)
    expect(canBreakAfter('“')).toBe(false)
    expect(canBreakAfter('中')).toBe(true)
  })

  it('classifies compression and hanging', () => {
    expect(compressionMode('，')).toBe('half')
    expect(compressionMode('。')).toBe('terminal')
    expect(canHang('，')).toBe(true)
    expect(canHang('。')).toBe(true)
    expect(canHang('中')).toBe(false)
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/punctuation.test.ts
```

Expected: FAIL because `src/punctuation.ts` does not exist.

- [x] **Step 3: Implement punctuation tables**

Create `packages/typography/src/punctuation.ts`:

```ts
import type { PunctuationCompression } from './model'

export const lineStartForbidden = new Set([
  '!', '%', ')', ',', '.', ':', ';', '?', ']', '}', '’', '”',
  '、', '。', '，', '：', '；', '？', '！', '）', '】', '》',
  '〉', '』', '」', '～', '…', '—',
])

export const lineEndForbidden = new Set([
  '(', '[', '{', '‘', '“',
  '（', '【', '《', '〈', '『', '「',
])

const halfCompression = new Set([
  '，', '、', '。', '．', '：', '；', '！', '？',
])

const terminalCompression = new Set([
  '。', '！', '？',
])

const hanging = new Set([
  '，', '、', '。', '．', '：', '；', '！', '？',
])

export function compressionMode(character: string): PunctuationCompression {
  if (terminalCompression.has(character)) return 'terminal'
  if (halfCompression.has(character)) return 'half'
  return 'none'
}

export function canHang(character: string): boolean {
  return hanging.has(character)
}

export function canBreakBefore(character: string): boolean {
  return !lineStartForbidden.has(character)
}

export function canBreakAfter(character: string): boolean {
  return !lineEndForbidden.has(character)
}
```

Modify `src/index.ts`:

```ts
export * from './model'
export * from './script'
export * from './punctuation'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/punctuation.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/punctuation.ts packages/typography/src/index.ts packages/typography/tests/punctuation.test.ts
git commit -m "feat(typography): add CJK punctuation rules"
```

---

### Task 4: Calculate adjustment, badness, and demerits

**Files:**

- Create: `packages/typography/src/metrics.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/metrics.test.ts`

**Interfaces:**

- Consumes: numeric widths only.
- Produces:

```ts
type LineAdjustment =
  | { type: 'natural'; amount: 0 }
  | { type: 'stretch'; amount: number }
  | { type: 'shrink'; amount: number }
  | { type: 'overflow'; amount: number };

function lineAdjustment(input: {
  availableWidth: number;
  naturalWidth: number;
  stretchWidth: number;
  shrinkWidth: number;
}): LineAdjustment;

function badness(input: {
  availableWidth: number;
  naturalWidth: number;
  stretchWidth: number;
  shrinkWidth: number;
}): number;

function demerits(input: { badness: number; penalty: number; flagged: boolean }): number;
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/metrics.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { badness, demerits, lineAdjustment } from '../src/metrics'

describe('line metrics', () => {
  it('uses stretch when a line is loose', () => {
    expect(lineAdjustment({
      availableWidth: 104,
      naturalWidth: 100,
      stretchWidth: 8,
      shrinkWidth: 2,
    })).toEqual({ type: 'stretch', amount: 4 })
  })

  it('uses shrink when a line is tight', () => {
    expect(lineAdjustment({
      availableWidth: 98,
      naturalWidth: 100,
      stretchWidth: 8,
      shrinkWidth: 4,
    })).toEqual({ type: 'shrink', amount: 2 })
  })

  it('scores natural lines as zero and overflow as maximum badness', () => {
    expect(badness({
      availableWidth: 100,
      naturalWidth: 100,
      stretchWidth: 4,
      shrinkWidth: 2,
    })).toBe(0)
    expect(badness({
      availableWidth: 90,
      naturalWidth: 100,
      stretchWidth: 4,
      shrinkWidth: 2,
    })).toBe(10_000)
  })

  it('increases demerits for bad and flagged breaks', () => {
    const natural = demerits({ badness: 0, penalty: 0, flagged: false })
    const poor = demerits({ badness: 2_000, penalty: 0, flagged: false })
    const flagged = demerits({ badness: 0, penalty: 0, flagged: true })

    expect(poor).toBeGreaterThan(natural)
    expect(flagged).toBeGreaterThan(natural)
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/metrics.test.ts
```

Expected: FAIL because `src/metrics.ts` does not exist.

- [x] **Step 3: Implement deterministic metric functions**

Create `packages/typography/src/metrics.ts`:

```ts
export type LineAdjustment =
  | { type: 'natural'; amount: 0 }
  | { type: 'stretch'; amount: number }
  | { type: 'shrink'; amount: number }
  | { type: 'overflow'; amount: number }

export function lineAdjustment(input: {
  availableWidth: number
  naturalWidth: number
  stretchWidth: number
  shrinkWidth: number
}): LineAdjustment {
  const delta = input.availableWidth - input.naturalWidth
  if (Math.abs(delta) < 0.0001) return { type: 'natural', amount: 0 }
  if (delta > 0) {
    if (input.stretchWidth <= 0) return { type: 'overflow', amount: -delta }
    return { type: 'stretch', amount: Math.min(delta, input.stretchWidth) }
  }
  const shortage = -delta
  if (input.shrinkWidth <= 0) return { type: 'overflow', amount: shortage }
  return { type: 'shrink', amount: Math.min(shortage, input.shrinkWidth) }
}

export function badness(input: {
  availableWidth: number
  naturalWidth: number
  stretchWidth: number
  shrinkWidth: number
}): number {
  const adjustment = lineAdjustment(input)
  if (adjustment.type === 'natural') return 0
  if (adjustment.type === 'overflow') return 10_000
  const available = adjustment.type === 'stretch'
    ? input.stretchWidth
    : input.shrinkWidth
  if (available <= 0) return 10_000
  return Math.min(10_000, Math.round(100 * (adjustment.amount / available) ** 3))
}

export function demerits(input: {
  badness: number
  penalty: number
  flagged: boolean
}): number {
  const badnessCost = (input.badness + 100) ** 2
  const penaltyCost = input.penalty < 0 ? (-input.penalty) ** 2 : 0
  const flagCost = input.flagged ? 300 : 0
  return badnessCost + penaltyCost + flagCost
}
```

Modify `src/index.ts`:

```ts
export * from './model'
export * from './script'
export * from './punctuation'
export * from './metrics'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/metrics.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/metrics.ts packages/typography/src/index.ts packages/typography/tests/metrics.test.ts
git commit -m "feat(typography): add line badness model"
```

---

### Task 5: Build paragraph items and legal break candidates

**Files:**

- Create: `packages/typography/src/paragraph.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/paragraph.test.ts`

**Interfaces:**

- Consumes: `ScriptClass`, `TextMetrics`, `LayoutItem`, `BreakCandidate`, `classifyCharacter`, `splitGraphemes`, punctuation functions.
- Produces:

```ts
type ParagraphMeasurements = (text: string, script: ScriptClass) => TextMetrics;

function buildParagraph(input: {
  text: string;
  availableWidth: number;
  measure: ParagraphMeasurements;
}): { items: LayoutItem[]; breaks: BreakCandidate[] };
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/paragraph.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { buildParagraph } from '../src/paragraph'

const measure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('paragraph construction', () => {
  it('creates boxes, word glue, and legal CJK breaks', () => {
    const paragraph = buildParagraph({
      text: '中文 English 中文',
      availableWidth: 1000,
      measure,
    })

    expect(paragraph.items.some((item) => item.kind === 'glue')).toBe(true)
    expect(paragraph.items.some((item) => (
      item.kind === 'glue' && item.text === ''
    ))).toBe(true)
    expect(paragraph.breaks.length).toBeGreaterThan(3)
  })

  it('does not break before closing punctuation', () => {
    const paragraph = buildParagraph({
      text: '中文，',
      availableWidth: 1000,
      measure,
    })

    expect(paragraph.breaks.some((candidate) => candidate.sourceTo === 2)).toBe(false)
  })

  it('reports an oversized atom as no-legal-break input', () => {
    const paragraph = buildParagraph({
      text: 'supercalifragilistic',
      availableWidth: 20,
      measure: (text) => ({ width: text.length * 10, stretch: 0, shrink: 0 }),
    })

    expect(paragraph.breaks).toEqual([])
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/paragraph.test.ts
```

Expected: FAIL because `src/paragraph.ts` does not exist.

- [x] **Step 3: Implement deterministic paragraph construction**

Create `packages/typography/src/paragraph.ts`:

```ts
import type { BreakCandidate, LayoutItem, ScriptClass, TextMetrics } from './model'
import { classifyCharacter, splitGraphemes } from './script'
import { canBreakAfter, canBreakBefore } from './punctuation'

export type ParagraphMeasurements = (
  text: string,
  script: ScriptClass,
) => TextMetrics

function appendBox(
  items: LayoutItem[],
  text: string,
  script: ScriptClass,
  from: number,
  to: number,
  measure: ParagraphMeasurements,
): void {
  items.push({
    kind: 'box',
    text,
    script,
    from,
    to,
    width: measure(text, script).width,
  })
}

export function buildParagraph(input: {
  text: string
  availableWidth: number
  measure: ParagraphMeasurements
}): { items: LayoutItem[]; breaks: BreakCandidate[] } {
  const items: LayoutItem[] = []
  const breaks: BreakCandidate[] = []
  const clusters = splitGraphemes(input.text)
  let index = 0

  while (index < clusters.length) {
    const cluster = clusters[index]!
    const script = classifyCharacter(cluster)
    const from = index

    if (script === 'space') {
      let end = index
      let text = ''
      while (end < clusters.length && classifyCharacter(clusters[end]!) === 'space') {
        text += clusters[end]!
        end += 1
      }
      const metrics = input.measure(text, 'space')
      items.push({
        kind: 'glue',
        text,
        script: 'space',
        from,
        to: end,
        width: metrics.width,
        stretch: metrics.stretch,
        shrink: metrics.shrink,
      })
      if (items.length >= 2) {
        breaks.push({
          itemIndex: items.length - 2,
          sourceTo: end,
          penalty: 0,
          flagged: false,
        })
      }
      index = end
      continue
    }

    if (script === 'latin' || script === 'digit') {
      let end = index
      let text = ''
      while (
        end < clusters.length &&
        ['latin', 'digit'].includes(classifyCharacter(clusters[end]!))
      ) {
        text += clusters[end]!
        end += 1
      }
      const metrics = input.measure(text, script)
      if (metrics.width > input.availableWidth) {
        appendBox(items, text, script, from, end, input.measure)
        return { items, breaks: [] }
      }
      const previous = items.at(-1)
      if (
        previous?.kind === 'box'
        && previous.script === 'cjk'
      ) {
        const boundary = input.measure('', 'space')
        items.push({
          kind: 'glue',
          text: '',
          script: 'space',
          from: end,
          to: end,
          width: boundary.width,
          stretch: boundary.stretch,
          shrink: boundary.shrink,
        })
      }
      appendBox(items, text, script, from, end, input.measure)
      index = end
      continue
    }

    const metrics = input.measure(cluster, script)
    const previous = items.at(-1)
    if (
      previous?.kind === 'box'
      && (previous.script === 'latin' || previous.script === 'digit')
    ) {
      const boundary = input.measure('', 'space')
      items.push({
        kind: 'glue',
        text: '',
        script: 'space',
        from: index,
        to: index,
        width: boundary.width,
        stretch: boundary.stretch,
        shrink: boundary.shrink,
      })
    }
    if (metrics.width > input.availableWidth) {
      appendBox(items, cluster, script, from, index + 1, input.measure)
      return { items, breaks: [] }
    }
    appendBox(items, cluster, script, from, index + 1, input.measure)

    const next = clusters[index + 1]
    if (next && canBreakAfter(cluster) && canBreakBefore(next)) {
      breaks.push({
        itemIndex: items.length - 1,
        sourceTo: index + 1,
        penalty: script === 'cjk' ? -50 : -100,
        flagged: script === 'cjk',
      })
    }

    index += 1
  }

  return { items, breaks }
}
```

Modify `src/index.ts`:

```ts
export * from './model'
export * from './script'
export * from './punctuation'
export * from './metrics'
export * from './paragraph'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/paragraph.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/paragraph.ts packages/typography/src/index.ts packages/typography/tests/paragraph.test.ts
git commit -m "feat(typography): build paragraph break candidates"
```

---

### Task 6: Implement Knuth–Plass dynamic programming

**Files:**

- Create: `packages/typography/src/breaker.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/breaker.test.ts`

**Interfaces:**

- Consumes: `LayoutItem`, `BreakCandidate`, `TypographyLine`, `badness`, `demerits`.
- Produces:

```ts
function breakParagraph(input: {
  items: LayoutItem[];
  breaks: BreakCandidate[];
  availableWidth: number;
  maxBadness?: number;
}): { lines: TypographyLine[]; fallback: boolean; fallbackReason?: 'no-legal-break' | 'badness-exceeded' };
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/breaker.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { breakParagraph } from '../src/breaker'
import { buildParagraph } from '../src/paragraph'

function build(text: string, width: number) {
  return buildParagraph({
    text,
    availableWidth: width,
    measure: (value) => ({
      width: value.length * 10,
      stretch: value.trim() === '' ? 4 : 0,
      shrink: value.trim() === '' ? 2 : 0,
    }),
  })
}

describe('Knuth–Plass breaker', () => {
  it('fits a paragraph on one optimal line', () => {
    const paragraph = build('中文 English', 1000)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 1000,
    })

    expect(result.fallback).toBe(false)
    expect(result.lines).toHaveLength(1)
    expect(result.lines[0]).toMatchObject({
      from: 0,
      to: 10,
      badness: 0,
      breakReason: 'paragraph-end',
    })
  })

  it('chooses global breaks for a narrow paragraph', () => {
    const paragraph = build('中文 English 中文 English', 120)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 120,
    })

    expect(result.fallback).toBe(false)
    expect(result.lines.length).toBeGreaterThan(1)
    expect(result.lines.every((line) => line.naturalWidth <= 120)).toBe(true)
  })

  it('falls back when no legal break exists', () => {
    const paragraph = build('supercalifragilistic', 20)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 20,
    })

    expect(result).toEqual({
      lines: [],
      fallback: true,
      fallbackReason: 'no-legal-break',
    })
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/breaker.test.ts
```

Expected: FAIL because `src/breaker.ts` does not exist.

- [x] **Step 3: Implement dynamic programming**

Create `packages/typography/src/breaker.ts`:

```ts
import type { BreakCandidate, LayoutItem, TypographyFallbackReason, TypographyLine } from './model'
import { badness, demerits, lineAdjustment } from './metrics'

type Path = {
  breakIndex: number
  sourceTo: number
  totalDemerits: number
  previous: Path | null
  penalty: number
  flagged: boolean
}

function lineMetrics(items: LayoutItem[], fromIndex: number, toIndex: number) {
  let naturalWidth = 0
  let stretchWidth = 0
  let shrinkWidth = 0

  for (let index = fromIndex; index <= toIndex; index += 1) {
    const item = items[index]
    if (!item || item.kind === 'penalty') continue
    naturalWidth += item.width
    if (item.kind === 'glue') {
      stretchWidth += item.stretch
      shrinkWidth += item.shrink
    }
  }

  return { naturalWidth, stretchWidth, shrinkWidth }
}

function textEndPosition(items: LayoutItem[]): number {
  return items.at(-1)?.to ?? 0
}

function makeLine(
  items: LayoutItem[],
  fromIndex: number,
  toIndex: number,
  from: number,
  to: number,
  availableWidth: number,
  penalty: number,
  flagged: boolean,
  reason: TypographyLine['breakReason'],
) {
  const metrics = lineMetrics(items, fromIndex, toIndex)
  const adjustment = lineAdjustment({
    availableWidth,
    ...metrics,
  })
  const lineBadness =
    reason === 'paragraph-end' && metrics.naturalWidth <= availableWidth
      ? 0
      : badness({
          availableWidth,
          ...metrics,
        })

  return {
    line: {
      from,
      to,
      naturalWidth: metrics.naturalWidth,
      adjustedWidth: metrics.naturalWidth
        + (adjustment.type === 'stretch' || adjustment.type === 'shrink'
          ? adjustment.amount
          : 0),
      badness: lineBadness,
      breakReason: reason,
    } satisfies TypographyLine,
    demerits: demerits({ badness: lineBadness, penalty, flagged }),
  }
}

export function breakParagraph(input: {
  items: LayoutItem[]
  breaks: BreakCandidate[]
  availableWidth: number
  maxBadness?: number
}): { lines: TypographyLine[]; fallback: boolean; fallbackReason?: TypographyFallbackReason } {
  const maxBadness = input.maxBadness ?? 8_000
  const candidates: Path[] = [{
    breakIndex: -1,
    sourceTo: 0,
    totalDemerits: 0,
    previous: null,
    penalty: 0,
    flagged: false,
  }]

  for (const candidate of input.breaks) {
    const item = input.items[candidate.itemIndex]
    if (!item) continue

    let best: { path: Path; cost: number } | null = null
    for (const previous of candidates) {
      const fromIndex = previous.breakIndex + 1
      const line = makeLine(
        input.items,
        fromIndex,
        candidate.itemIndex,
        previous.sourceTo,
        candidate.sourceTo,
        input.availableWidth,
        candidate.penalty,
        candidate.flagged && previous.flagged,
        'optimal',
      )
      if (line.line.badness > maxBadness) continue
      const cost = previous.totalDemerits + line.demerits
      if (!best || cost < best.cost) best = { path: previous, cost }
    }

    if (best) {
      candidates.push({
        breakIndex: candidate.itemIndex,
        sourceTo: candidate.sourceTo,
        totalDemerits: best.cost,
        previous: best.path,
        penalty: candidate.penalty,
        flagged: candidate.flagged,
      })
    }
  }

  const textEnd = textEndPosition(input.items)
  let bestFinal: { path: Path; cost: number } | null = null
  for (const previous of candidates) {
    const fromIndex = previous.breakIndex + 1
    if (fromIndex > input.items.length) continue
    const line = makeLine(
      input.items,
      fromIndex,
      input.items.length - 1,
      previous.sourceTo,
      textEnd,
      input.availableWidth,
      -10_000,
      false,
      'paragraph-end',
    )
    if (line.line.badness > maxBadness) continue
    const cost = previous.totalDemerits + line.demerits
    if (!bestFinal || cost < bestFinal.cost) {
      bestFinal = { path: previous, cost }
    }
  }

  if (!bestFinal) {
    return {
      lines: [],
      fallback: true,
      fallbackReason: input.breaks.length === 0 ? 'no-legal-break' : 'badness-exceeded',
    }
  }

  const lines: TypographyLine[] = []
  let cursor: Path | null = bestFinal.path

  while (cursor.previous) {
    const previous = cursor.previous
    const line = makeLine(
      input.items,
      previous.breakIndex + 1,
      cursor.breakIndex,
      previous.sourceTo,
      cursor.sourceTo,
      input.availableWidth,
      cursor.penalty,
      cursor.flagged && previous.flagged,
      'optimal',
    )
    lines.unshift(line.line)
    cursor = previous
  }

  const finalLine = makeLine(
    input.items,
    bestFinal.path.breakIndex + 1,
    input.items.length - 1,
    bestFinal.path.sourceTo,
    textEnd,
    input.availableWidth,
    -10_000,
    false,
    'paragraph-end',
  )
  lines.push(finalLine.line)

  return {
    lines,
    fallback: false,
  }
}
```

Modify `src/index.ts`:

```ts
export * from './model'
export * from './script'
export * from './punctuation'
export * from './metrics'
export * from './paragraph'
export * from './breaker'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/breaker.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/breaker.ts packages/typography/src/index.ts packages/typography/tests/breaker.test.ts
git commit -m "feat(typography): add global line breaker"
```

---

### Task 7: Assemble and validate LayoutPlans

**Files:**

- Create: `packages/typography/src/plan.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/plan.test.ts`

**Interfaces:**

- Consumes: `buildParagraph`, `breakParagraph`.
- Produces:

```ts
function layoutParagraph(input: {
  text: string;
  availableWidth: number;
  maxBadness?: number;
  measure: ParagraphMeasurements;
}): LayoutPlan;
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/plan.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { layoutParagraph } from '../src/plan'

const measure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('layout plan', () => {
  it('produces a successful deterministic plan', () => {
    const first = layoutParagraph({
      text: '中文 English 中文',
      availableWidth: 120,
      measure,
    })
    const second = layoutParagraph({
      text: '中文 English 中文',
      availableWidth: 120,
      measure,
    })

    expect(first.fallback).toBe(false)
    expect(first).toEqual(second)
    expect(first.lines[0]).toMatchObject({ from: 0, to: 2 })
  })

  it('produces a deterministic fallback plan', () => {
    const plan = layoutParagraph({
      text: 'supercalifragilistic',
      availableWidth: 20,
      measure,
    })

    expect(plan).toEqual({
      lines: [],
      fallback: true,
      fallbackReason: 'no-legal-break',
    })
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/plan.test.ts
```

Expected: FAIL because `src/plan.ts` does not exist.

- [x] **Step 3: Implement plan assembly**

Create `packages/typography/src/plan.ts`:

```ts
import type { LayoutPlan } from './model'
import { breakParagraph } from './breaker'
import { buildParagraph, type ParagraphMeasurements } from './paragraph'

export function layoutParagraph(input: {
  text: string
  availableWidth: number
  maxBadness?: number
  measure: ParagraphMeasurements
}): LayoutPlan {
  if (!Number.isFinite(input.availableWidth) || input.availableWidth <= 0) {
    return {
      lines: [],
      fallback: true,
      fallbackReason: 'no-legal-break',
    }
  }

  const paragraph = buildParagraph({
    text: input.text,
    availableWidth: input.availableWidth,
    measure: input.measure,
  })
  const broken = breakParagraph({
    items: paragraph.items,
    breaks: paragraph.breaks,
    availableWidth: input.availableWidth,
    maxBadness: input.maxBadness,
  })

  return {
    lines: broken.lines,
    fallback: broken.fallback,
    fallbackReason: broken.fallbackReason,
  }
}
```

Modify `src/index.ts`:

```ts
export * from './model'
export * from './script'
export * from './punctuation'
export * from './metrics'
export * from './paragraph'
export * from './breaker'
export * from './plan'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/plan.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/plan.ts packages/typography/src/index.ts packages/typography/tests/plan.test.ts
git commit -m "feat(typography): assemble layout plans"
```

---

### Task 8: Add bounded LRU caches

**Files:**

- Create: `packages/typography/src/cache.ts`
- Modify: `packages/typography/src/index.ts`
- Create: `packages/typography/tests/cache.test.ts`

**Interfaces:**

- Consumes: generic keys and values.
- Produces:

```ts
class LruCache<K, V> {
  constructor(maxEntries: number);
  get(key: K): V | undefined;
  set(key: K, value: V): void;
  has(key: K): boolean;
  clear(): void;
  get size(): number;
}
```

- [x] **Step 1: Write failing tests**

Create `packages/typography/tests/cache.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { LruCache } from '../src/cache'

describe('LruCache', () => {
  it('evicts the least recently used entry', () => {
    const cache = new LruCache<string, number>(2)
    cache.set('a', 1)
    cache.set('b', 2)
    expect(cache.get('a')).toBe(1)
    cache.set('c', 3)

    expect(cache.has('b')).toBe(false)
    expect(cache.get('a')).toBe(1)
    expect(cache.get('c')).toBe(3)
  })

  it('refreshes recency on set', () => {
    const cache = new LruCache<string, number>(2)
    cache.set('a', 1)
    cache.set('b', 2)
    cache.set('a', 10)
    cache.set('c', 3)

    expect(cache.has('a')).toBe(true)
    expect(cache.get('a')).toBe(10)
    expect(cache.has('b')).toBe(false)
  })
})
```

- [x] **Step 2: Verify the test fails**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/cache.test.ts
```

Expected: FAIL because `src/cache.ts` does not exist.

- [x] **Step 3: Implement LRU behavior**

Create `packages/typography/src/cache.ts`:

```ts
export class LruCache<K, V> {
  private readonly entries = new Map<K, V>()

  constructor(private readonly maxEntries: number) {
    if (!Number.isInteger(maxEntries) || maxEntries <= 0) {
      throw new RangeError('maxEntries must be a positive integer')
    }
  }

  get size(): number {
    return this.entries.size
  }

  has(key: K): boolean {
    return this.entries.has(key)
  }

  get(key: K): V | undefined {
    const value = this.entries.get(key)
    if (value !== undefined) {
      this.entries.delete(key)
      this.entries.set(key, value)
    }
    return value
  }

  set(key: K, value: V): void {
    this.entries.delete(key)
    this.entries.set(key, value)
    if (this.entries.size > this.maxEntries) {
      const oldest = this.entries.keys().next().value
      if (oldest !== undefined) this.entries.delete(oldest)
    }
  }

  clear(): void {
    this.entries.clear()
  }
}
```

Modify `src/index.ts` by adding:

```ts
export * from './cache'
```

- [x] **Step 4: Verify tests pass**

Run:

```bash
corepack pnpm --dir packages/typography exec vitest run tests/cache.test.ts
corepack pnpm --dir packages/typography typecheck
```

Expected: PASS.

- [x] **Step 5: Commit**

```bash
git add packages/typography/src/cache.ts packages/typography/src/index.ts packages/typography/tests/cache.test.ts
git commit -m "feat(typography): add bounded layout caches"
```

---

### Task 9: Kernel regression and integration gate

**Files:**

- Modify: `packages/typography/tests/breaker.test.ts`

**Interfaces:**

- Consumes: all public kernel APIs.
- Produces: regression coverage for mixed text, fallback, determinism, and grapheme safety.

- [x] **Step 1: Add cross-module regression tests**

Append to `packages/typography/tests/breaker.test.ts`:

```ts
it('does not split emoji', () => {
  const paragraph = buildParagraph({
    text: '中文👍English',
    availableWidth: 80,
    measure: (value) => ({ width: value.length * 12, stretch: 0, shrink: 0 }),
  })
  const result = breakParagraph({
    items: paragraph.items,
    breaks: paragraph.breaks,
    availableWidth: 80,
  })

  const boxText = paragraph.items
    .filter((item) => item.kind === 'box')
    .map((item) => item.kind === 'box' ? item.text : '')
    .join('')

  expect(boxText).toContain('👍')
  expect(result.fallback).toBe(false)
})

it('returns identical plans for repeated input', () => {
  const first = layoutParagraph({
    text: '中文 English 中文 English',
    availableWidth: 140,
    measure,
  })
  const second = layoutParagraph({
    text: '中文 English 中文 English',
    availableWidth: 140,
    measure,
  })

  expect(first).toEqual(second)
})
```

Add imports at the top:

```ts
import { layoutParagraph } from '../src/plan'
```

If `measure` is not available in this file, define:

```ts
const measure = (value: string) => ({
  width: value.length * 10,
  stretch: value.trim() === '' ? 4 : 0,
  shrink: value.trim() === '' ? 2 : 0,
})
```

- [x] **Step 2: Run the complete new package suite**

Run:

```bash
corepack pnpm --dir packages/typography test
corepack pnpm --dir packages/typography typecheck
corepack pnpm typecheck:editor-core
```

Expected: PASS. No existing editor-core behavior is changed.

- [x] **Step 3: Verify the git scope**

Run:

```bash
git status --short
```

Expected: only files created or modified by this plan are staged or modified. Existing unrelated untracked docs remain untouched.

- [x] **Step 4: Commit**

```bash
git add packages/typography/tests/breaker.test.ts
git commit -m "test(typography): cover kernel regressions"
```

---

## Plan Boundary

This plan implements only spec Phase 0. It intentionally does **not** implement:

- ProseMirror extraction or decoration application;
- WebView measurement adapters;
- editor UI, settings, or diagnostics;
- PDF, image, or HTML export projection;
- scheduler, scroll anchoring, or degradation policy;
- Windows/macOS host contracts.

Those are separate implementation plans because each has independent review and rollout risks.
