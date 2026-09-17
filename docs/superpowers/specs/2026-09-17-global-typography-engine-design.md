# Global Typography Engine Design

- Date: 2026-09-17
- Status: Approved for implementation planning
- Products: MarkLeaf macOS and MarkLeaf Windows
- Scope: Shared native-application editor path

## 1. Summary

MarkLeaf will add a shared global typography engine for high-quality justified text. The engine will replace browser greedy line breaking for eligible body paragraphs in visual editing, preview, HTML export, PDF export, and image export.

The selected architecture is a typography projection layer. ProseMirror remains the source of truth for the document model, selection, undo/redo, IME, and editing commands. The engine produces a deterministic `LayoutPlan`, and the editor renders that plan through ProseMirror decorations. Export pipelines reuse the same typography decisions for the same document snapshot, settings, font context, and target width.

The first release targets the shared `EditorWeb` path used by macOS and Windows. VS Code is intentionally deferred.

## 2. Goals

1. Implement paragraph-level global line breaking inspired by Knuth–Plass / TeX instead of browser greedy wrapping.
2. Produce the same typographic decisions from a shared TypeScript kernel on macOS and Windows.
3. Preserve document text, selection, IME, undo/redo, copy, paste, find/replace, and drag/drop behavior.
4. Keep user Markdown clean; all line breaks and spacing adjustments are display-level projections.
5. Share paragraph layout decisions between visual editing, preview, HTML export, PDF export, and image export.
6. Optimize CJK and mixed CJK/Latin text, including punctuation compression, forbidden line starts and ends, hanging punctuation, and CJK/Latin spacing.
7. Support incremental rendering and graceful fallback for long documents and unusual content.
8. Keep the engine testable independently from ProseMirror, WebView, macOS, and Windows.

## 3. Non-Goals

The first version will not:

- replace ProseMirror with a fully custom text renderer;
- draw body text on canvas;
- justify source mode;
- justify code blocks, formulas, Mermaid diagrams, or front matter;
- justify table cells;
- support RTL or vertical writing modes;
- promise pixel-identical rendering when macOS and Windows use different fonts;
- expose low-level constants such as badness or glue width in normal preferences;
- integrate into VS Code;
- alter user Markdown text.

Source mode remains a monospaced source view. Code and math internals remain governed by their existing renderers.

## 4. Product Scope

### 4.1 Host Scope

```text
macOS WKWebView  ─┐
                  ├── EditorWeb ── @markleaf/editor-core ── @markleaf/typography
Windows WebView2 ─┘
```

VS Code is deferred. The typography package remains host-independent so VS Code can adopt it later with its own measurement adapter.

### 4.2 Surface Scope

The engine applies to visual editing, preview/reading state, HTML export, PDF export, and image export using the shared HTML render path.

It applies to normal paragraphs and paragraphs inside lists, blockquotes, and alerts/callouts.

It does not apply to source mode, code blocks, inline code, formulas, Mermaid, front matter, table cells, headings by default, first-version footnote definitions, RTL paragraphs, vertical writing, or paragraphs with explicit non-justified alignment.

### 4.3 Export Scope

For the same snapshot, settings, font context, and target width, exports must use the same layout decisions.

PDF and image exports use a concrete target width and should be strongly consistent. The first HTML export mode is frozen typography HTML: it fixes a content width and embeds explicit line-break and spacing projections. Responsive HTML may be added later.

## 5. Architecture

### 5.1 High-Level Flow

```text
Markdown document model
  ↓
ProseMirror editing transaction
  ↓
Typography document projection
  ↓
Inline run extraction
  ↓
Font metrics + Knuth–Plass + CJK rules
  ↓
LayoutPlan
  ↓
ProseMirror decorations → visual editor
  ↓
Same plan rules → HTML / PDF / image export
```

The document model is never modified by typography. `LayoutPlan` is display state only.

### 5.2 Selected Approach

The approved approach is a typography decoration/projection layer.

ProseMirror remains responsible for the document model, schema, transactions, selection, undo/redo, IME, copy/paste, drag/drop, commands, collaboration state, and source-mode switching.

The typography layer is responsible for extracting paragraph runs, requesting measurements, computing line breaking and CJK adjustments, generating `LayoutPlan`, generating editor decorations, and generating export HTML projections.

Rejected alternatives:

- a fully custom text renderer has the highest layout freedom but unacceptable editing risk;
- preview/export-only typography does not satisfy real-time unified editing;
- direct CSS tuning cannot implement global Knuth–Plass line breaking.

## 6. Package Boundary

Create a new pure TypeScript package:

```text
packages/typography
└── @markleaf/typography
```

Suggested structure:

```text
packages/typography/src/
├── model.ts
├── script.ts
├── metrics.ts
├── breaker.ts
├── cjk.ts
├── plan.ts
└── cache.ts
```

| Module | Responsibility |
|---|---|
| `model.ts` | Paragraph, run, break unit, atom, plan, and adjustment types |
| `script.ts` | Script, punctuation, grapheme, and forbidden-break classification |
| `metrics.ts` | Metric and glue models |
| `breaker.ts` | Knuth–Plass dynamic programming |
| `cjk.ts` | CJK/Latin spacing, kinsoku, punctuation compression, hanging |
| `plan.ts` | Deterministic plan construction and validation |
| `cache.ts` | Metric and paragraph plan caches |

The package must not import DOM, ProseMirror, Node APIs, macOS APIs, Windows APIs, or host settings code.

`@markleaf/editor-core` adds integration adapters:

```text
packages/editor-core/src/typography/
├── prosemirror-extract.ts
├── prosemirror-apply.ts
├── export-plan.ts
└── settings.ts
```

The existing `packages/editor-core/src/typography.ts` continues to resolve CSS typography style templates and should not absorb the layout engine.

## 7. Input Model

A paragraph is converted into an ordered run sequence:

```ts
type TypographyRun =
  | TextRun
  | InlineAtomRun
  | BoundaryRun;
```

### 7.1 TextRun

`TextRun` contains continuous text and its font context.

Font context includes font family, font size, font weight, font style, letter spacing, word spacing, font features, language, and active marks such as bold, italic, link, and inline code.

### 7.2 InlineAtomRun

`InlineAtomRun` represents content that must not be internally split, including images, formulas, footnote marks, atomic inline nodes, and special generated content.

Inline code is measured as text but treated conservatively; the first version does not break inside inline code.

### 7.3 BoundaryRun

`BoundaryRun` records semantic changes that affect breaking or adjustment:

- CJK/Latin transitions;
- mark boundaries;
- link boundaries;
- script transitions;
- punctuation boundaries;
- protected content boundaries.

### 7.4 Source Positions

Every run and break candidate stores a ProseMirror source position range. Decorations and export projections map back to these ranges. Typography must never invent a text position that cannot be mapped back to the document.

## 8. Measurement

The kernel receives measurements through a host-provided adapter:

```ts
interface MeasurementAdapter {
  measureRun(run: TextRun): RunMetrics;
  measureInlineAtom(run: InlineAtomRun): AtomMetrics;
  resolveFontContext(run: TypographyRun): FontContext;
  availableWidth(paragraph: ParagraphContext): number;
}
```

The WebView host implements the adapter using hidden DOM / Range measurement. Measurements are batched to avoid repeated forced layout.

### 8.1 RunMetrics

```ts
type RunMetrics = {
  naturalWidth: number;
  stretchWidth: number;
  shrinkWidth: number;
  canBreakBefore: boolean;
  canBreakAfter: boolean;
  forbiddenBefore: boolean;
  forbiddenAfter: boolean;
};
```

Different content classes have different flexibility:

- ordinary word spaces can stretch and shrink;
- CJK/Latin gaps have controlled stretch and shrink;
- CJK punctuation can compress in defined contexts;
- inline atoms are rigid;
- inline code is rigid or conservatively break-free in the first version;
- headings are not justified by default.

### 8.2 Metric Cache

```text
MetricsCache key =
  fontContextHash +
  textCluster +
  script +
  punctuationMode
```

The cache is bounded and LRU-managed.

macOS and Windows share the algorithm but may produce different metric values because installed fonts and text shaping differ. The product goal is rule-level consistency, not pixel-identical rendering.

## 9. Knuth–Plass Core

Each paragraph is converted into a TeX-style sequence:

```text
Box     = unbreakable grapheme cluster, word, or inline atom
Glue    = space, CJK/Latin gap, compressible punctuation zone
Penalty = forbidden break, hyphenation point, protected boundary
```

The engine uses dynamic programming to minimize total paragraph demerits instead of choosing each line greedily.

### 9.1 Break Candidate

Each break candidate records:

- source position;
- penalty;
- flag;
- natural width;
- stretch width;
- shrink width;
- forbidden-break status;
- break reason.

### 9.2 Line Fitting

For a candidate line:

```text
badness = f(availableWidth, naturalWidth, stretchWidth, shrinkWidth)
```

The engine considers stretching, shrinking, and maximum tolerated badness. Demerits penalize high badness, forbidden punctuation placement, flagged or special breaks, short lines, awkward inline atom boundaries, consecutive similar fitness transitions, and avoidable breaks near protected content.

### 9.3 Fallback

A paragraph falls back to native CSS behavior when:

- measurement fails;
- an atom is wider than the available line;
- no legal break exists;
- the paragraph exceeds configured length or time limits;
- DOM verification finds overflow;
- writing direction is unsupported;
- paragraph alignment is explicitly non-justified;
- required styles are incompatible.

Fallback is explicit and deterministic. Every fallback records a stable reason code.

## 10. CJK Rules

### 10.1 CJK/Latin Spacing

The engine inserts virtual glue between CJK and Latin/digit content. This glue participates in global line optimization.

When typography controls a paragraph, the older CJK autospace widget is disabled for that paragraph to prevent duplicate spacing. Fallback paragraphs continue using the existing autospace behavior.

### 10.2 Kinsoku

The first version includes line-start and line-end forbidden rules for CJK punctuation.

Examples:

- line-start forbidden: `，。、；：？！）】》”’…—`;
- line-end forbidden: `（【《“‘`;
- ellipses and dashes are treated as protected sequences.

Forbidden rules are modeled as penalties. The engine never deletes, replaces, or reorders text.

### 10.3 Punctuation Compression

Full-width punctuation is not treated as a fixed square. Punctuation has semantic metric ranges:

```ts
type PunctuationMetrics = {
  naturalWidth: number;
  minWidth: number;
  maxWidth: number;
  canHang: boolean;
  compressionMode: 'none' | 'half' | 'adjacent' | 'terminal';
};
```

Supported cases include terminal punctuation at line end, adjacent closing punctuation, punctuation after CJK content, punctuation near Latin content, and hanging sentence punctuation when enabled.

### 10.4 Hanging

Hanging punctuation is display-only and configurable. Recommended settings are `off`, sentence punctuation only, and all supported hanging marks. The default is conservative: off globally, or enabled only by typography style templates that explicitly request it.

## 11. LayoutPlan

The kernel returns a deterministic plan:

```ts
type LayoutPlan = {
  paragraphId: string;
  revision: number;
  lines: TypographyLine[];
  adjustments: TypographyAdjustment[];
  fallback: boolean;
  fallbackReason?: TypographyFallbackReason;
};
```

### 11.1 TypographyLine

```ts
type TypographyLine = {
  sourceFrom: number;
  sourceTo: number;
  naturalWidth: number;
  adjustedWidth: number;
  badness: number;
  breakReason: 'optimal' | 'forced' | 'fallback' | 'paragraph-end';
};
```

### 11.2 Adjustment Types

Adjustments are semantic, not CSS:

```ts
type TypographyAdjustment =
  | CjkLatinSpacing
  | PunctuationCompression
  | HangingPunctuation
  | InterWordStretch
  | InterCharacterShrink
  | ProtectedBoundary;
```

The editor adapter translates adjustments into ProseMirror decorations. The export adapter translates the same adjustments into HTML classes and CSS variables.

## 12. ProseMirror Integration

### 12.1 TypographyPlugin

Add a dedicated ProseMirror plugin:

```text
TypographyPlugin
├── observes transactions;
├── invalidates affected paragraphs;
├── maps unaffected decorations;
├── schedules layout work;
├── stores plan/cache state;
└── publishes DecorationSet.
```

The plugin only produces display state.

### 12.2 Decoration Types

The editor projection uses:

1. line-break widgets;
2. inline adjustment decorations;
3. controlled-paragraph state decorations.

Line-break widgets are empty, `aria-hidden`, `contenteditable=false`, non-copying, zero document positions, and excluded from undo history.

Controlled paragraphs disable browser natural wrapping so the only visual line breaks come from the typography engine.

### 12.3 Transaction Rules

After each transaction:

- unchanged paragraphs keep their cached plan;
- moved paragraphs map decorations through `tr.mapping`;
- changed paragraphs are invalidated;
- settings/font/width changes invalidate affected caches;
- undo/redo matches by content hash and may reuse a prior plan.

Typography never creates document transactions and therefore never enters undo history.

### 12.4 IME Safety

When composition starts, freeze the active paragraph, do not replace decorations around candidate text, and keep the previous stable projection or fall back locally. Resume typography after composition ends.

This protects Chinese, Japanese, and other IME workflows.

### 12.5 Selection and Copy Safety

Selection remains based on ProseMirror document positions. Break widgets do not occupy document positions.

Copy semantics must satisfy:

- copied text does not contain inserted break characters;
- copied Markdown does not contain typography markup;
- visual line breaks do not become hard Markdown line breaks;
- punctuation remains unchanged in text.

### 12.6 DOM Verification

After applying a plan, the editor adapter verifies actual DOM line geometry.

If any line exceeds the available width by a configured tolerance, the paragraph immediately falls back.

This prevents the worst failure mode where the engine assumes a line is complete but the browser inserts an additional natural break.

## 13. Editor Projection

### 13.1 Break Widget

Conceptual DOM:

```html
<span
  class="ml-typography-break"
  aria-hidden="true"
  contenteditable="false"
></span>
```

CSS semantics:

- visually forces the next content to a new line;
- has no text content;
- has no pointer interaction;
- does not participate in accessibility;
- does not appear in serialized Markdown.

### 13.2 Adjustment DOM

Adjustments use stable class names and CSS variables rather than ad-hoc inline styles.

Examples:

```html
<span class="ml-type-punct-compress adjacent">，</span>
<span
  class="ml-type-glue-stretch"
  style="--ml-type-adjust: 1.8px"
> </span>
```

The engine never replaces text to achieve spacing.

### 13.3 Paragraph State

ProseMirror does not provide stable paragraph IDs, so the typography layer maintains:

```ts
type ParagraphTypographyState = {
  contentHash: string;
  styleHash: string;
  availableWidth: number;
  language: string;
  plan: LayoutPlan;
  decorations: DecorationSet;
};
```

Identity is based on normalized fragment, marks, style context, width, language, and settings version. Typography IDs are not written into user documents.

### 13.4 Compatibility

Typography decorations must coexist with find highlights, selection decorations, block handles, focus mode, CJK autospace, footnote tooltips, link tooltips, source-mode switching, and explicit paragraph alignment.

Rules:

- source mode suspends the plugin;
- table cells are skipped;
- explicit center/right alignment is skipped;
- CJK autospace is disabled only for controlled paragraphs;
- incompatible `writing-mode`, `white-space`, or alignment styles trigger fallback;
- typography must not overwrite semantic highlights.

## 14. Export Integration

### 14.1 Deterministic Equation

```text
TypographySnapshot +
TypographySettings +
FontContext +
TargetWidth
= LayoutPlan
```

If editor width and export target width differ, their line breaks may differ, but both must be produced by the same kernel and rules.

### 14.2 Export Coordinator

Add:

```text
TypographyExportCoordinator
├── freezes TypographySnapshot;
├── computes target-width plans;
├── applies plans to prepared export HTML;
├── disables duplicate CJK autospace;
└── hands frozen HTML to exporters.
```

Pipeline:

```text
ProseMirror HTML
  ↓
renderEditorHtmlForExport()
  ↓
TypographyExportCoordinator.applyPlans()
  ↓
applyExportPagination()
  ↓
PDF / PNG / JPG / HTML
```

Typography runs before pagination. Pagination remains a block-level concern and does not re-break paragraph lines.

### 14.3 Export Targets

#### PDF

PDF receives a plan computed for the PDF content width. Exported text remains real, selectable, searchable text.

Requirements:

- headings remain locatable for outline generation;
- text extraction order matches source semantics;
- copied punctuation remains unchanged;
- forbidden line rules are respected;
- lines do not overflow the content area;
- existing page header/footer behavior remains intact.

#### Image

Image export uses the same prepared HTML projection with its own concrete width. For the same target width and settings, image and PDF line decisions must match.

#### HTML

The first HTML mode is frozen typography HTML:

- fixed exported content width;
- explicit line-break projections;
- embedded adjustment classes and CSS variables;
- same visual decisions as PDF/image for the same width.

Responsive HTML is a later option and is not required for the first release.

### 14.4 Export Ordering

Apply typography after existing semantic transforms:

```text
heading anchors
  ↓
image path resolution
  ↓
front matter removal
  ↓
footnote structure
  ↓
formula / Mermaid rendering
  ↓
typography projection
  ↓
pagination
```

Legacy CJK autospace remains active only for fallback paragraphs.

### 14.5 Export Fallback

```text
success → frozen typography;
measurement failure → paragraph fallback;
timeout → paragraph fallback;
DOM overflow verification failure → paragraph fallback;
outline mapping failure → heading paragraph fallback.
```

A failed paragraph must not silently produce a visually overflowing line.

## 15. Performance Architecture

### 15.1 Scheduler

Add a central scheduler:

```text
TypographyScheduler
├── task queue;
├── priority resolver;
├── frame budget controller;
├── stale revision filter;
└── fallback controller.
```

Priorities:

| Priority | Scope | Purpose |
|---|---|---|
| P0 | active input paragraph | minimize cursor-local latency |
| P1 | visible paragraphs | preserve reading quality |
| P2 | viewport-adjacent paragraphs | prepare scrolling |
| P3 | offscreen paragraphs | background improvement |
| P4 | export snapshot | deterministic export preparation |

The scheduler uses a per-frame time budget and yields before blocking interaction. Only the latest revision for a paragraph is useful; stale work is discarded.

### 15.2 Caches

Use bounded caches for:

1. run metrics;
2. paragraph plans;
3. editor decorations;
4. environment versions.

Paragraph plan key:

```text
contentHash +
styleHash +
availableWidth +
language +
typographySettingsVersion
```

Environment invalidation keys include font family, font size, line height, language, width, settings, and engine version.

### 15.3 Incremental Granularity

The first version recalculates at paragraph granularity.

Knuth–Plass is paragraph-global; partial intra-paragraph recalculation would compromise correctness. Other paragraphs remain cached unless their content or environment changes.

### 15.4 Long Documents

Do not introduce DOM virtualization in the first release.

Instead:

- keep all paragraphs in ProseMirror;
- compute visible paragraphs first;
- precompute adjacent paragraphs;
- compute offscreen paragraphs during idle time;
- leave distant paragraphs in fallback for very large documents;
- cancel all pending work when a tab/document closes.

### 15.5 Scroll Stability

Before applying a batch:

1. record a visible document anchor;
2. apply plans;
3. compare visual anchor movement;
4. compensate scrollTop when necessary.

Do not alter visible line counts during active scrolling. Batch offscreen updates while scroll is idle.

### 15.6 Memory and Degradation

Use LRU and configured limits for metrics and plans. Preserve plans for the current viewport, cursor paragraph, and imminent export range.

Degradation sequence:

```text
normal
  → visible-only
  → focused-paragraph-only
  → full native fallback
```

Degradation is automatic when latency, frame overruns, queue depth, memory, or fallback rates exceed configured limits.

The first version keeps layout computation on the WebView main thread with cooperative slicing. A Worker may be considered later because the kernel is pure, but measurement still requires the main thread.

## 16. Settings

### 16.1 User Settings

Expose a small, understandable surface:

```text
排版增强
├── 开启 / 关闭 / 跟随样式模板

对齐策略
├── 平衡 / 稳健 / 紧凑

中文标点
├── 标准压缩

悬挂行末标点
├── 关闭 / 仅句读 / 全部支持项

自动调整中文与西文间距
├── 开启 / 关闭
```

Complex constants remain internal.

### 16.2 Defaults

| Stage | Default |
|---|---|
| development | off |
| internal validation | off, manual diagnostic switch |
| stable release | on for eligible Markdown visual paragraphs |

Fallback must always remain automatic.

### 16.3 Document Override

Add a current-document override:

```text
排版增强
├── 跟随设置
├── 当前文档开启
└── 当前文档关闭
```

This override must not modify Markdown.

### 16.4 Style Template Integration

Typography style templates may declare optional variables:

```css
.markleaf-style-book {
  --ml-typography-mode: typographic;
  --ml-cjk-punctuation: compress;
  --ml-cjk-hanging: sentence;
}
```

Possible variables:

```css
--ml-typography-mode: native | typographic;
--ml-cjk-punctuation: none | standard | compact;
--ml-cjk-hanging: none | sentence | all;
--ml-cjk-latin-gap: standard | compact;
```

Precedence:

```text
explicit paragraph alignment
  > current document override
  > style template declaration
  > global setting
  > engine default
```

## 17. Testing Strategy

### 17.1 Kernel Golden Tests

Use deterministic fake metrics and assert complete plans.

Cover:

- Chinese-only paragraphs;
- Latin-only paragraphs;
- mixed CJK/Latin;
- digits;
- long words;
- repeated punctuation;
- line-start forbidden marks;
- line-end forbidden marks;
- nested brackets;
- quotes;
- ellipses;
- dashes;
- spaces;
- no-break spaces;
- links;
- inline code;
- bold/italic boundaries;
- narrow widths;
- oversized atoms;
- impossible paragraphs;
- fallback cases.

Snapshots include boundaries, badness, adjustments, source offsets, and fallback reasons.

### 17.2 Script and Rule Tests

Test grapheme safety, punctuation classification, forbidden rules, compression eligibility, CJK/Latin gaps, surrogate pairs, emoji, and combining marks.

### 17.3 ProseMirror Projection Tests

Verify:

- plan application;
- document JSON unchanged;
- Markdown serialization unchanged;
- decoration mapping;
- cache reuse;
- undo/redo behavior;
- selection continuity;
- copy cleanliness;
- paste cleanliness;
- composition freeze/resume;
- find highlight coexistence;
- block-handle compatibility;
- fallback reason propagation.

### 17.4 Host Tests

macOS targeted contract tests cover settings propagation, font/width invalidation, source-mode suspension, export plan injection, and diagnostics.

Windows targeted contract tests cover the same settings schema, WebView2 measurement adapter, CJK fallback behavior, PDF plan application, and image export.

VS Code remains build-compatible but is not functionally integrated.

### 17.5 Export Tests

HTML:

- explicit break elements exist only for planned paragraphs;
- source text remains unchanged;
- punctuation remains unchanged;
- fallback paragraphs do not receive incorrect break projections.

PDF:

- text is extractable;
- headings remain locatable;
- outline mapping works;
- line rules are respected;
- no overflow;
- header/footer behavior remains correct.

Image:

- same-width line decisions match PDF;
- scale does not change line breaking;
- dark/light themes do not truncate text.

### 17.6 Performance Benchmarks

Use fixed corpora:

1. short Chinese document;
2. mixed CJK/Latin technical document;
3. 100-paragraph document;
4. 1000-paragraph document;
5. one 50,000-character paragraph;
6. list/quote/link-heavy document;
7. image/formula/inline-code-heavy document;
8. table-heavy document.

Track first layout time, average paragraph layout time, active-input latency, viewport stabilization, idle full-layout completion, input delay, memory growth, cache hit rate, fallback ratio, and forced layout count.

CI reports regressions but does not assert absolute real-device timing.

### 17.7 Manual Acceptance

macOS:

- Chinese IME input;
- mixed-language editing;
- undo/redo;
- mouse drag selection;
- Shift-arrow selection;
- double/triple click;
- copy to external editors;
- paste from external editors;
- find/replace;
- long-document scrolling;
- window resizing;
- theme/font/style changes;
- source-mode switching;
- HTML/PDF/image export.

Windows:

- WebView2 IME input;
- Microsoft Pinyin and Japanese IME;
- high-DPI scaling;
- missing-font fallback;
- long-document scrolling;
- PDF/image export;
- resizing;
- dark/light switching;
- CJK font fallback.

## 18. Rollout

### Phase 0 — Kernel

Add `@markleaf/typography`, kernel tests, and deterministic fixtures. No product behavior changes.

### Phase 1 — Editor Projection Experiment

Add `TypographyPlugin`, diagnostics, and settings plumbing. Default off. Support eligible normal paragraphs first.

### Phase 2 — Export Integration

Add PDF/image target-width plans and frozen HTML export. Improve fallback reporting and compare old/new exports.

### Phase 3 — Default Candidate

Expose settings broadly, enable by default after validation, retain document override, and monitor performance/fallback metrics.

### Phase 4 — Style Ecosystem and VS Code

Allow typography-aware style templates. Reevaluate VS Code integration once host contracts are stable.

## 19. Release Gate

Do not enable by default until:

- kernel golden tests pass;
- ProseMirror projection tests pass;
- macOS and Windows manual IME checklists pass;
- long-document scrolling is stable;
- copy contains no extra characters;
- PDF text extraction and heading location work;
- HTML/PDF/image exports agree for the same width;
- input latency has no perceptible regression;
- every fallback has a stable reason;
- users can disable the feature globally and per document.

## 20. Risks

| Risk | Mitigation |
|---|---|
| Browser adds an extra natural break | controlled paragraph CSS plus DOM width verification and fallback |
| IME regression | freeze during composition and manual CJK/Japanese acceptance |
| Selection regression | widgets remain zero-position and selection is document-position based |
| Copy pollution | empty, non-copying widgets and serialization tests |
| Long-document latency | scheduler, viewport priority, caches, and automatic degradation |
| Memory growth | bounded LRU caches and offscreen plan eviction |
| Export/browser reflow | frozen export projection and post-apply verification |
| PDF outline regression | preserve heading semantics and test title text mapping |
| Theme conflicts | resolved-style inspection, explicit native declarations, and fallback |
| Cross-platform metric differences | shared rules but platform-real measurement; no false pixel-identity claim |

## 21. Implementation Sequence

1. Create the pure typography package and deterministic model.
2. Implement script classification and fake-metric test fixtures.
3. Implement Knuth–Plass line breaking and golden tests.
4. Implement CJK/Latin spacing, kinsoku, compression, and hanging models.
5. Implement `LayoutPlan` validation and cache.
6. Add ProseMirror extraction.
7. Add WebView measurement adapter.
8. Add `TypographyPlugin` decoration projection.
9. Add transaction mapping, IME guards, and fallback verification.
10. Add export coordinator and PDF/image projection.
11. Add frozen HTML export.
12. Add settings, document override, and diagnostics.
13. Add macOS and Windows host contracts.
14. Run performance corpora and manual acceptance.
15. Prepare staged rollout.

## 22. Definition of Done

The project is complete when a document edited on macOS or Windows can use real-time shared typography, export through PDF/image/frozen HTML with consistent decisions, fall back safely for unsupported or failing cases, preserve clean Markdown and clean copy semantics, and expose a simple user control with automatic degradation and diagnostics.
