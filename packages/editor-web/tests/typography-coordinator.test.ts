import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, getMarkdown, extractProjectionSnapshots, getTypographyProjectionRevision, createTypographyMeasurementService, type TypographyProjectionDiagnostics } from '@markleaf/editor-core'
import { createTypographyCoordinator } from '../src/typography-coordinator'
import * as core from '@markleaf/editor-core'
const disposers: Array<() => void> = []
afterEach(() => { disposers.splice(0).forEach(f => f()); document.body.innerHTML = ''; Reflect.deleteProperty(document, 'fonts'); vi.unstubAllGlobals(); vi.restoreAllMocks() })
function setup(readProjectedLine?: Parameters<typeof createTypographyCoordinator>[0]['readProjectedLine'], fonts?: EventTarget, detailedTrace = true) {
  if (fonts) Object.defineProperty(document, 'fonts', { configurable: true, value: fonts })
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中文中文', false)
  disposers.push(() => editor.destroy())
  editor.view.dom.querySelectorAll('p').forEach(p => { p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect) })
  let elapsed = 0, measureCost = 0
  const batches: number[] = []
  const probes: string[] = []
  const frames = new Map<number, FrameRequestCallback>(); let next = 0
  const reports: TypographyProjectionDiagnostics[] = []
  const traces: Record<string, unknown>[] = []
  const coordinator = createTypographyCoordinator({ editor, clock: () => elapsed,
    requestFrame: f => { frames.set(++next, f); return next }, cancelFrame: id => { frames.delete(id) },
    measurement: { dispose() {}, measureBatch(snapshot, lines) { probes.push(snapshot.id); batches.push(lines.length); elapsed += measureCost; return lines.map(l => ({ key: l.key, naturalWidth: [...snapshot.text.slice(l.from, l.contentTo)].length * 16 })) } },
    onDiagnostics: d => reports.push(d), onTrace: detailedTrace ? d => traces.push(d) : undefined, readProjectedLine: readProjectedLine ?? ((_view, line) => line.targetWidth),
  })
  disposers.unshift(() => coordinator.dispose())
  const flush = () => { for (let i = 0; frames.size && i < 300; i++) { elapsed += 16; const copy = [...frames.values()]; frames.clear(); copy.forEach(f => f(elapsed)) } expect(frames.size).toBe(0) }
  const tick = (ms = 16) => { elapsed += ms; const first = [...frames.entries()][0]!; frames.delete(first[0]); first[1](elapsed) }
  return { editor, coordinator, frames, reports, traces, flush, tick, batches, probes, now: () => elapsed, spend: (ms: number) => { elapsed += ms }, cost: (value: number) => { measureCost = value }, slow: () => { measureCost = 129 } }
}
describe('typography coordinator', () => {
  it('doesNotUseExtraGeometryOrStyleReadsForDiagnosticTracing', () => {
    const geometry = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
    const styles = vi.spyOn(window, 'getComputedStyle')
    const run = (tracing: boolean) => {
      const s = setup(undefined, undefined, tracing)
      const initialGeometry = geometry.mock.calls.length, initialStyles = styles.mock.calls.length
      s.coordinator.setEnabled(true); s.flush()
      return { reads: [geometry.mock.calls.length - initialGeometry, styles.mock.calls.length - initialStyles], s }
    }
    const on = run(true), off = run(false)
    expect(on.s.traces.length).toBeGreaterThan(0)
    expect(off.s.traces).toHaveLength(0)
    expect(on.reads).toEqual(off.reads)
    expect(off.s.reports.at(-1)?.plannedParagraphs).toBe(1)
  })
  it('preparesSlotsUnderTheSharedFrameDeadlineInsteadOfPrescanningEveryParagraph', () => {
    const s = setup(), extract = core.extractProjectionSnapshots
    s.editor.commands.setContent('中文'.repeat(30))
    const p = s.editor.view.dom.querySelector('p')!
    p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    // Make real slot preparation expensive, without replacing its behavior:
    // each font-size read consumes 0.25ms on the same injectable work clock.
    vi.spyOn(core, 'extractProjectionSnapshots').mockImplementation((...args) => extract(...args).map(e => !e.eligible ? e : ({ ...e,
      snapshot: { ...e.snapshot, runs: e.snapshot.runs.map(run => ({ ...run, css: new Proxy(run.css, {
        get(target, key, receiver) { if (key === 'font-size') s.spend(.25); return Reflect.get(target, key, receiver) },
      }) })) },
    })))
    s.coordinator.setEnabled(true)
    const before = s.now()
    s.tick(0)
    expect(s.now() - before).toBeLessThanOrEqual(4.5)
    expect(s.reports).toHaveLength(0)
    expect(s.frames.size).toBe(1)
  })
  it('doesNotTreatAnAuthorAlignmentChangeAsColorOnly', () => {
    const s = setup()
    s.coordinator.setEnabled(true); s.flush()
    const p = s.editor.view.dom.querySelector('p')!
    p.style.textAlign = 'justify'
    s.coordinator.invalidate('style')
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    expect(s.frames.size).toBe(1)
    s.flush()
    expect(s.reports).toHaveLength(2)
  })
  it('sharesOneFrameDeadlineAndRotatesBetweenParagraphJobs', () => {
    const s = setup()
    s.editor.commands.setContent({ type: 'doc', content: [1, 2].map(() => ({ type: 'paragraph', content: [{ type: 'text', text: '中文中文' }] })) })
    s.editor.view.dom.querySelectorAll('p').forEach(p => { p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect) })
    const before = s.editor.getJSON(), selection = s.editor.state.selection.toJSON()
    s.cost(4); s.coordinator.setEnabled(true)
    s.tick(); expect(s.probes).toHaveLength(1)
    s.tick(); expect(s.probes).toHaveLength(2)
    expect(new Set(s.probes).size).toBe(2)
    expect(s.reports).toHaveLength(0)
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    s.flush()
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(2)
    expect(s.editor.getJSON()).toEqual(before)
    expect(s.editor.state.selection.toJSON()).toEqual(selection)
  })
  it.each(['document', 'style', 'width', 'font', 'mode', 'disable', 'dispose', 'composition'] as const)('cancelsAnInFlightPlanningTokenOn%s', action => {
    const s = setup()
    s.cost(4); s.coordinator.setEnabled(true); s.tick()
    const oldToken = s.traces.find(t => t.event === 'planning')?.generation ?? 1
    expect(s.reports).toHaveLength(0)
    if (action === 'disable') s.coordinator.setEnabled(false)
    else if (action === 'dispose') s.coordinator.dispose()
    else if (action === 'composition') s.coordinator.setComposing(true)
    else if (action === 'document') {
      s.editor.commands.insertContent('新')
      const p = s.editor.view.dom.querySelector('p')!
      p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    } else {
      if (action === 'style' || action === 'width') s.editor.view.dom.querySelector('p')!.getBoundingClientRect = () => ({ width: 64 } as DOMRect)
      s.coordinator.invalidate(action)
    }
    s.flush()
    expect(s.traces.filter(t => t.event === 'publish').every(t => t.generation !== oldToken)).toBe(true)
    if (action === 'dispose') expect(s.reports).toHaveLength(0)
    else {
      if (action === 'disable') s.coordinator.setEnabled(true)
      if (action === 'composition') s.coordinator.setComposing(false)
      s.flush()
      expect(s.reports).toHaveLength(1)
    }
  })
  it('reportsEveryHardBreakSiblingAsFallbackWhenOnlyOneSegmentFails', () => {
    const s = setup()
    s.editor.commands.setContent({ type: 'doc', content: [{ type: 'paragraph', content: [
      { type: 'text', text: '中文中文' }, { type: 'hardBreak' }, { type: 'text', text: 'ABCDEFG' },
    ] }] })
    const p = s.editor.view.dom.querySelector('p')!
    p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    s.coordinator.setEnabled(true); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(0)
    expect(s.reports.at(-1)?.paragraphs?.map(p => p.status)).toEqual(['fallback', 'fallback'])
    expect(s.reports.at(-1)?.paragraphs?.every(p => p.reason)).toBe(true)
  })
  it('includesTheFinalPublishingSliceAndTransactionInTimingDiagnostics', () => {
    const s = setup()
    s.editor.on('transaction', ({ transaction }) => {
      if (transaction.getMeta('typography-managed')?.snapshots.length) s.spend(7)
    })
    s.coordinator.setEnabled(true); s.flush()
    expect(s.reports.at(-1)?.work?.maxSliceMs).toBeGreaterThanOrEqual(7)
    expect(s.reports.at(-1)?.work?.maxOperationMs).toBeGreaterThanOrEqual(7)
    // The cycle starts in its first frame; only the next 16ms frame wait is
    // inside its lifetime. The 7ms transaction must not be counted as wait.
    expect(s.reports.at(-1)?.work?.waitingMs).toBe(16)
  })
  it.each([
    { error: .4, correctOnRetry: false, planned: 1, publications: 1 },
    { error: .8, correctOnRetry: true, planned: 1, publications: 2 },
    { error: .8, correctOnRetry: false, planned: 0, publications: 3 },
  ])('boundsActualDomCorrectionAndPublication $error $correctOnRetry', fixture => {
    let nonfinalReads = 0, publications = 0
    const s = setup((_view, line) => line.breakReason === 'paragraph-end' ? line.naturalWidth
      : line.targetWidth - (++nonfinalReads > 1 && fixture.correctOnRetry ? 0 : fixture.error))
    s.editor.on('transaction', ({ transaction }) => {
      if (transaction.getMeta('typography-managed')?.snapshots.length) publications++
    })
    s.coordinator.setEnabled(true); s.flush()
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(fixture.planned)
    expect(publications).toBe(fixture.publications)
    expect(s.frames.size).toBe(0)
    const before = s.reports.length
    s.coordinator.invalidate('style'); s.flush()
    expect(s.reports).toHaveLength(before)
  })
  it.each(['document', 'style', 'width', 'font', 'mode', 'disable', 'dispose', 'composition'] as const)('cancelsPendingVerificationOn%sWithoutAnOldRevision', action => {
    const s = setup((_view, line) => { s.spend(5); return line.targetWidth })
    s.editor.commands.setContent('中文'.repeat(5))
    const p = s.editor.view.dom.querySelector('p')!
    p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    s.coordinator.setEnabled(true)
    for (let i = 0; !s.editor.view.dom.querySelector('.markleaf-typography-v2') && i < 100; i++) s.tick()
    const oldToken = s.traces.find(t => t.event === 'publish')!.generation
    s.tick(); expect(s.reports).toHaveLength(0)
    if (action === 'disable') s.coordinator.setEnabled(false)
    else if (action === 'dispose') s.coordinator.dispose()
    else if (action === 'composition') s.coordinator.setComposing(true)
    else if (action === 'document') {
      s.editor.commands.insertContent('新')
      const next = s.editor.view.dom.querySelector('p')!
      next.style.fontSize = '16px'; next.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    } else {
      if (action === 'style' || action === 'width') p.getBoundingClientRect = () => ({ width: 64 } as DOMRect)
      s.coordinator.invalidate(action)
    }
    s.flush()
    expect(s.traces.filter(t => t.event === 'finish' && t.generation === oldToken)).toHaveLength(0)
    expect(s.traces.filter(t => t.event === 'publish' && t.generation === oldToken)).toHaveLength(1)
    if (action === 'dispose') expect(s.reports).toHaveLength(0)
    else {
      if (action === 'disable') s.coordinator.setEnabled(true)
      if (action === 'composition') s.coordinator.setComposing(false)
      s.flush(); expect(s.reports).toHaveLength(1)
    }
  })
  it('yieldsInsideVerificationWithoutPublishingPartialCorrections', () => {
    let reads = 0
    const s = setup((_view, line) => { reads++; s.spend(5); return line.targetWidth })
    s.editor.commands.setContent('中文'.repeat(8))
    const p = s.editor.view.dom.querySelector('p')!
    p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    s.coordinator.setEnabled(true)
    for (let i = 0; !s.editor.view.dom.querySelector('.markleaf-typography-v2') && i < 100; i++) s.tick()
    s.tick()
    expect(reads).toBe(1)
    expect(s.reports).toHaveLength(0)
    s.flush()
    expect(reads).toBe(8)
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(1)
  })
  it('doesNotPublishARevisionCancelledHalfwayThroughVerification', () => {
    const s = setup((_view, line) => { s.spend(5); return line.targetWidth })
    s.editor.commands.setContent('中文'.repeat(5))
    const p = s.editor.view.dom.querySelector('p')!
    p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    s.coordinator.setEnabled(true)
    for (let i = 0; !s.editor.view.dom.querySelector('.markleaf-typography-v2') && i < 100; i++) s.tick()
    s.tick(); s.coordinator.setEnabled(false); s.flush()
    expect(s.reports).toHaveLength(0)
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
  })
  it('continuesOneParagraphAcrossFramesInsteadOfFailingAt32ms', () => {
    const s = setup()
    s.cost(20); s.coordinator.setEnabled(true)
    s.tick()
    expect(s.reports).toHaveLength(0)
    expect(s.frames.size).toBe(1)
    s.tick(1000) // Waiting is not active work.
    s.flush()
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(1)
    expect(s.reports.at(-1)?.reasons?.['budget-exceeded']).toBeUndefined()
    expect(s.reports.at(-1)?.paragraphs?.[0]?.status).toBe('planned')
    expect(s.reports.at(-1)?.paragraphs?.[0]?.work.activeMs).toBeGreaterThan(32)
    expect(s.reports.at(-1)?.paragraphs?.[0]?.work.activeMs).toBeLessThanOrEqual(128)
  })
  it('doesNotRecomputeGeometryForAPureColorChange', async () => {
    const s = setup()
    // Use inherited font/geometry: PM may legitimately recreate a paragraph
    // after the setup's manual DOM style mutation, losing a per-node stub.
    s.editor.view.dom.style.fontSize = '16px'
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 32 } as DOMRect)
    await Promise.resolve()
    s.coordinator.setEnabled(true); s.flush()
    const before = s.reports.length, batches = s.batches.length
    s.editor.view.dom.style.color = 'red'
    await Promise.resolve(); s.flush()
    expect(s.reports).toHaveLength(before)
    expect(s.batches).toHaveLength(batches)
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).not.toBeNull()
  })
  it('finishesVerificationWhenAnInheritedClassIsReassertedWithoutChanging', async () => {
    const html = document.documentElement, oldClass = html.getAttribute('class')
    html.setAttribute('class', 'typography-theme')
    disposers.push(() => { if (oldClass === null) html.removeAttribute('class'); else html.setAttribute('class', oldClass) })
    const s = setup()
    s.coordinator.setEnabled(true); s.tick()
    html.setAttribute('class', 'typography-theme')
    await Promise.resolve()
    s.tick()
    expect(s.reports, JSON.stringify(s.traces)).toHaveLength(1)
    expect(s.frames.size).toBe(0)
    expect(s.reports[0]?.plannedParagraphs).toBe(1)
  })
  it('finishesVerificationDespiteOffscreenMeasurementAndUnrelatedUiMutations', async () => {
    const s = setup()
    const measurement = createTypographyMeasurementService(document)
    disposers.push(() => measurement.dispose())
    s.coordinator.setEnabled(true); s.tick()
    const entry = extractProjectionSnapshots(s.editor.view, getTypographyProjectionRevision(s.editor), 0).find(e => e.eligible)!
    if (!entry.eligible) throw new Error('Expected paragraph snapshot')
    // Real measurement writes styles under body, then detaches its spans before
    // MutationObserver delivery. These are outputs, not author/theme inputs.
    measurement.measureBatch(entry.snapshot, [{ key: 'probe', from: 0, to: 2, contentTo: 2 }], [])
    const overlay = document.createElement('div'); document.body.append(overlay)
    overlay.style.top = '10px'
    await Promise.resolve()
    s.tick()
    expect(s.reports).toHaveLength(1)
    expect(s.frames.size).toBe(0)
    expect(s.reports[0]?.plannedParagraphs).toBe(1)
  })
  it('stillReflowsForAuthorStylesAndInheritedAncestorStyles', async () => {
    const s = setup()
    s.coordinator.setEnabled(true); s.flush(); await Promise.resolve(); s.flush()
    s.editor.view.dom.style.fontSize = '20px'
    await Promise.resolve()
    expect(s.frames.size).toBeGreaterThan(0)
    s.flush(); await Promise.resolve(); s.flush()
    const before = s.reports.length
    document.body.lang = 'en'
    await Promise.resolve()
    expect(s.frames.size).toBeGreaterThan(0)
    s.flush()
    expect(s.reports.length).toBeGreaterThan(before)
  })
  it('staysNativeThroughAResizeBurstAndResumesOnlyAfter150msAtTheFinalWidth', () => {
    const s = setup(), before = getMarkdown(s.editor)
    s.coordinator.setEnabled(true); s.flush()
    const revision = getTypographyProjectionRevision(s.editor)
    const selection = s.editor.state.selection.toJSON(), canUndo = s.editor.can().undo()
    const p = s.editor.view.dom.querySelector('p')!
    const batches = s.batches.length, reports = s.reports.length
    let clears = 0
    s.editor.on('transaction', ({ transaction }) => {
      const managed = transaction.getMeta('typography-managed')
      if (managed?.enabled && managed.snapshots.length === 0) clears++
    })
    // Returning to the original width must not reuse the now-cleared display.
    for (const width of [64, 48, 32]) {
      p.getBoundingClientRect = () => ({ width } as DOMRect)
      s.coordinator.invalidate('width')
      expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
      s.tick(149)
      expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
      expect(s.batches).toHaveLength(batches)
      expect(s.reports).toHaveLength(reports)
    }
    // Other invalidations must not bypass the pending resize quiet period.
    s.coordinator.invalidate('style'); s.tick(0)
    expect(s.batches).toHaveLength(batches)
    expect(clears).toBe(1)
    s.tick(1); s.flush()
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(1)
    expect(s.reports).toHaveLength(reports + 1)
    expect(getMarkdown(s.editor)).toBe(before)
    expect(getTypographyProjectionRevision(s.editor)).toBe(revision)
    expect(s.editor.state.selection.toJSON()).toEqual(selection)
    expect(s.editor.can().undo()).toBe(canUndo)
  })
  it('doesNotRestoreAfterTheQuietPeriodUntilCompositionEnds', () => {
    const s = setup()
    s.coordinator.setEnabled(true); s.flush()
    s.coordinator.invalidate('width'); s.tick(149)
    s.coordinator.setComposing(true); s.spend(1)
    expect(s.frames.size).toBe(0)
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    s.coordinator.setComposing(false); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).not.toBeNull()
  })
  it.each(['disable', 'dispose'] as const)('cancelsPendingResizeRecoveryOn%s', action => {
    const s = setup()
    s.coordinator.setEnabled(true); s.flush()
    s.coordinator.invalidate('width'); s.tick(149)
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    const batches = s.batches.length, reports = s.reports.length
    if (action === 'disable') s.coordinator.setEnabled(false)
    else s.coordinator.dispose()
    expect(s.frames.size).toBe(0)
    s.coordinator.invalidate('width'); s.flush()
    expect(s.batches).toHaveLength(batches)
    expect(s.reports).toHaveLength(reports)
    if (action === 'disable') {
      s.coordinator.setEnabled(true); s.flush()
      expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).not.toBeNull()
    }
  })
  it('doesNotReuseTheCompletedCacheAfterAnInterruptedReplacementWasPublished', () => {
    const s = setup()
    s.coordinator.setEnabled(true); s.flush()
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(1)
    const p = s.editor.view.dom.querySelector('p')!
    p.getBoundingClientRect = () => ({ width: 64 } as DOMRect)
    s.coordinator.invalidate('width'); s.tick(150) // New plan displayed, verification pending.
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).not.toBeNull()
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
    p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    s.coordinator.invalidate('width'); s.flush()
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(1)
  })
  it('usesOneNativePhaseForMetricStylesAndSurvivesReturningToTheOriginalStyle', () => {
    const s = setup()
    s.editor.commands.setContent({ type: 'doc', content: [1, 2].map(() => ({ type: 'paragraph', content: [{ type: 'text', text: '中文中文' }] })) })
    const paragraphs = [...s.editor.view.dom.querySelectorAll('p')]
    paragraphs.forEach(p => { p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect) })
    s.coordinator.setEnabled(true); s.flush()
    const original = s.editor.view.dom.innerHTML
    s.cost(4)
    paragraphs.forEach(p => { p.getBoundingClientRect = () => ({ width: 64 } as DOMRect) })
    s.coordinator.invalidate('style'); s.tick() // Yield with one paragraph still pending.
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    paragraphs.forEach(p => { p.getBoundingClientRect = () => ({ width: 32 } as DOMRect) })
    s.coordinator.invalidate('style') // Interrupt, returning to the cached environment.
    s.flush()
    expect(s.editor.view.dom.innerHTML).toBe(original)
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-v2')).toHaveLength(2)
    expect(getMarkdown(s.editor)).toBe('中文中文\n\n中文中文')
  })
  it('doesNotRestartVerificationForItsOwnHeightChanges', () => {
    let notify!: () => void
    vi.stubGlobal('ResizeObserver', class {
      constructor(callback: () => void) { notify = callback }
      observe() {} disconnect() {}
    })
    let reads = 0
    const s = setup((_view, line) => { reads++; return line.targetWidth })
    s.coordinator.setEnabled(true); s.tick()
    const projected = s.editor.view.dom.querySelector('.markleaf-typography-v2')!
    expect(projected).not.toBeNull()
    const measurements = s.batches.length
    // Browser delivers a resize after projection changes line count/height,
    // before the next-frame verification. Available width has not changed.
    notify(); window.dispatchEvent(new Event('resize')); s.tick()
    expect(reads).toBeGreaterThan(0)
    expect(s.batches).toHaveLength(measurements)
    expect(s.frames.size).toBe(0)
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(1)
    // Real horizontal resizing must still invalidate and recompute line breaks.
    s.editor.view.dom.getBoundingClientRect = () => ({ width: 64 } as DOMRect)
    projected.getBoundingClientRect = () => ({ width: 64 } as DOMRect)
    notify(); s.flush()
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
    expect(s.batches.length).toBeGreaterThan(measurements)
  })
  it('waitsForCompositionAndKeepsSourceUnchanged', () => {
    const s = setup(), before = getMarkdown(s.editor)
    s.coordinator.setComposing(true); s.coordinator.setEnabled(true); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    s.coordinator.invalidate('font'); s.coordinator.invalidate('width'); s.flush()
    s.coordinator.setComposing(false); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).not.toBeNull()
    expect(getMarkdown(s.editor)).toBe(before)
  })
  it('dropsOldRevisionBeforePublishing', () => {
    const s = setup()
    s.coordinator.setEnabled(true)
    s.editor.commands.insertContent('新')
    s.flush()
    expect(getMarkdown(s.editor)).toContain('新')
    expect(s.reports.at(-1)?.plannedParagraphs).toBeGreaterThanOrEqual(0)
    s.coordinator.dispose(); expect(s.frames.size).toBe(0)
  })
  it('stopsOnBudgetAndDoesNotRetryIdenticalFailure', () => {
    const s = setup(); s.slow(); s.coordinator.setEnabled(true); s.flush()
    expect(s.reports.at(-1)?.reasons?.['budget-exceeded']).toBe(1)
    const count = s.reports.length
    s.coordinator.invalidate('style'); s.flush()
    expect(s.reports).toHaveLength(count)
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
  })
  it('validatesActualGeometryAndStopsRepeatedCorrection', () => {
    const s = setup((_view, line) => line.targetWidth - .75)
    s.coordinator.setEnabled(true); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    expect(s.reports.at(-1)?.reasons?.['measurement-mismatch']).toBeGreaterThan(0)
  })
  it('reportsUnsupportedOnlyDocuments', () => {
    const s = setup()
    s.editor.commands.setContent({ type: 'doc', content: [
      { type: 'heading', attrs: { level: 1 }, content: [{ type: 'text', text: '标题' }] },
      { type: 'paragraph', attrs: { textAlign: 'left' }, content: [{ type: 'text', text: '正文' }] },
    ] })
    s.editor.view.dom.querySelectorAll('p').forEach(p => { p.style.textAlign = 'left' })
    expect(extractProjectionSnapshots(s.editor.view, getTypographyProjectionRevision(s.editor), 0).some(e => e.eligible)).toBe(false)
    s.coordinator.setEnabled(true); s.flush()
    expect(s.reports.at(-1)?.reasons?.['unsupported-block']).toBe(1)
  })
  it('neverPublishesAChangedWidthBeforeValidation', () => {
    const s = setup()
    s.editor.commands.setContent({ type: 'doc', content: [1, 2].map(() => ({ type: 'paragraph', content: [{ type: 'text', text: '中文中文' }] })) })
    const paragraphs = [...s.editor.view.dom.querySelectorAll('p')]
    paragraphs.forEach(p => { p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect) })
    s.cost(4)
    s.coordinator.setEnabled(true)
    s.tick() // First paragraph measured, continuation queued, nothing committed.
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    paragraphs.forEach(p => { p.getBoundingClientRect = () => ({ width: 64 } as DOMRect) })
    s.coordinator.invalidate('width')
    s.tick() // The obsolete continuation must not publish either paragraph.
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    s.flush()
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(2)
    expect(s.editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })
  it('stillValidatesAfterAnEquivalentWidthInvalidation', () => {
    let reads = 0
    const s = setup((_view, line) => { reads++; return line.targetWidth })
    s.coordinator.setEnabled(true); s.tick()
    s.coordinator.invalidate('width'); s.flush()
    expect(reads).toBeGreaterThan(0)
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(1)
  })
  it('usesNativeLayoutWhileFontsLoadAndHandlesLateCompletionAfterDispose', async () => {
    let resolve!: () => void
    const fonts = Object.assign(new EventTarget(), { status: 'loaded', ready: new Promise<void>(r => { resolve = r }) })
    const s = setup(undefined, fonts)
    s.coordinator.setEnabled(true); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).not.toBeNull()
    fonts.status = 'loading'; fonts.dispatchEvent(new Event('loading')); s.flush()
    expect(s.editor.view.dom.querySelector('.markleaf-typography-v2')).toBeNull()
    expect(s.reports.at(-1)?.reasons?.['font-pending']).toBe(1)
    fonts.status = 'loaded'; fonts.dispatchEvent(new Event('loadingerror')); s.flush()
    expect(s.reports.at(-1)?.plannedParagraphs).toBe(1)
    s.coordinator.dispose(); const count = s.reports.length
    fonts.dispatchEvent(new Event('loadingdone')); resolve(); await Promise.resolve()
    s.flush(); expect(s.reports).toHaveLength(count)
  })
  it('boundsSynchronousMeasurementChunks', () => {
    const s = setup()
    s.editor.commands.setContent('中文'.repeat(8))
    const p = s.editor.view.dom.querySelector('p')!
    p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect)
    s.coordinator.setEnabled(true); s.flush()
    expect(s.batches.length).toBeGreaterThan(1)
    expect(Math.max(...s.batches)).toBeLessThanOrEqual(16)
  })
  it('yieldsAfterASpaceMeasurementExceedsBudget', () => {
    const s = setup()
    s.editor.commands.setContent({ type: 'doc', content: [1, 2].map(() => ({ type: 'paragraph', content: [{ type: 'text', text: 'A B C D' }] })) })
    s.editor.view.dom.querySelectorAll('p').forEach(p => { p.style.fontSize = '16px'; p.getBoundingClientRect = () => ({ width: 32 } as DOMRect) })
    s.slow(); s.coordinator.setEnabled(true); s.tick()
    expect(s.batches).toHaveLength(1)
    expect(s.frames.size).toBe(1)
    s.flush(); expect(s.reports.at(-1)?.reasons?.['budget-exceeded']).toBe(2)
  })
})
