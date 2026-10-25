import {
  applyTypographyPlan, extractProjectionSnapshots, getTypographyProjectionDiagnostics,
  getTypographyProjectionRevision, registerTypographySnapshots, createLayoutWork, typographyWorkLimits,
  type Editor, type MeasurementService, type MeasuredPlan, type LineMeasurement, type MeasuredLine,
  type TypographyProjectionDiagnostics, type ProjectionSnapshot, type LayoutCheckpoint,
  correctTypographyLine, measureProjectedLine,
} from '@markleaf/editor-core'
import { createTypographyJob, type TypographyJob } from './typography-job'

export type TypographyCoordinator = {
  setEnabled(value: boolean): void
  invalidate(reason: 'document' | 'style' | 'width' | 'font' | 'mode'): void
  setComposing(value: boolean): void
  dispose(): void
}
type Entries = ReturnType<typeof extractProjectionSnapshots>
type Item = { snapshot: ProjectionSnapshot; job: TypographyJob; done: boolean; plan?: MeasuredPlan; reason?: string }
type Check = { item: Item; line: number; lines: MeasuredLine[]; widths: LineMeasurement[]; replan: boolean; retrying: boolean; done: boolean }
type Cycle = {
  token: number; revision: number; entries: Entries; items: Item[]; cursor: number; started: number
  phase: 'planning' | 'verify'; checks: Check[]; round: number; changed: boolean
  reasons: Record<string, number>; budgetReasons: Record<string, number>; cacheHits: number
  maxSliceMs: number; maxOperationMs: number; revisions: number
  busyMs: number; frameStarted: number; frameAccounted: number
}

export function createTypographyCoordinator(options: {
  editor: Editor; measurement: MeasurementService; clock: () => number
  requestFrame: (callback: FrameRequestCallback) => number; cancelFrame: (id: number) => void
  onDiagnostics: (value: TypographyProjectionDiagnostics) => void
  onTrace?: (value: Record<string, unknown>) => void
  readProjectedLine?: typeof measureProjectedLine
}): TypographyCoordinator {
  const { editor, measurement, clock, requestFrame, cancelFrame, onDiagnostics } = options
  const root = editor.view.dom, doc = root.ownerDocument, win = doc.defaultView!, fonts = doc.fonts
  let enabled = false, composing = false, disposed = false, epoch = 0, frame = 0, generation = 0
  let quietUntil = 0, nativePhase = false, previous: Entries | undefined, cycle: Cycle | undefined
  let traceCount = 0
  const trace = (event: string, detail: Record<string, unknown> = {}) => {
    // Logging is not a layout input: never force geometry/style reads here.
    if (options.onTrace && traceCount++ < 1500) options.onTrace({ event, time: clock(), generation, ...detail })
  }
  const same = (a: ProjectionSnapshot, b: ProjectionSnapshot) => a.id === b.id && a.revision === b.revision
    && Math.abs(a.width - b.width) < .25 && a.language === b.language && a.text === b.text
    && a.alignment === b.alignment
    && JSON.stringify(a.runs) === JSON.stringify(b.runs)
    && JSON.stringify(JSON.parse(a.environmentKey).slice(3)) === JSON.stringify(JSON.parse(b.environmentKey).slice(3))
  const sameEntries = (a: Entries, b: Entries) => a.length === b.length && a.every((entry, i) => {
    const other = b[i]!
    return entry.eligible && other.eligible ? same(entry.snapshot, other.snapshot)
      : !entry.eligible && !other.eligible && entry.nodeFrom === other.nodeFrom && entry.nodeTo === other.nodeTo && entry.reason === other.reason
  })
  const originalBatch = measurement.onBatch
  const onBatch: NonNullable<MeasurementService['onBatch']> = stats => { originalBatch?.(stats); if (cycle) cycle.cacheHits += stats.cacheHits }
  measurement.onBatch = onBatch
  const live = (c: Cycle) => !disposed && enabled && !composing && generation === c.token && getTypographyProjectionRevision(editor) === c.revision
  function timed<T>(c: Cycle, operation: () => T): T {
    const start = clock()
    try { return operation() } finally { c.maxOperationMs = Math.max(c.maxOperationMs, clock() - start) }
  }
  function recordFrame(c: Cycle) {
    const elapsed = Math.max(0, clock() - c.frameStarted)
    c.maxSliceMs = Math.max(c.maxSliceMs, elapsed)
    c.busyMs += Math.max(0, elapsed - c.frameAccounted); c.frameAccounted = elapsed
  }
  const current = (c: Cycle) => live(c) && sameEntries(c.entries, timed(c, () => extractProjectionSnapshots(editor.view, c.revision, epoch)))
  function cancelCycle() { cycle?.items.forEach(i => i.job.dispose()); cycle = undefined }
  function schedule() { if (!disposed && enabled && !composing && !frame) frame = requestFrame(run) }
  function native() {
    previous = undefined
    if (!nativePhase) { nativePhase = true; registerTypographySnapshots(editor, true, []) }
  }
  function invalidate(reason: 'document' | 'style' | 'width' | 'font' | 'mode') {
    if (disposed) return
    const baseline = cycle?.entries ?? previous
    if (reason === 'style' && baseline && fonts?.status !== 'loading'
      && sameEntries(baseline, extractProjectionSnapshots(editor.view, getTypographyProjectionRevision(editor), epoch))) return
    trace('invalidate', { reason }); generation++
    cancelCycle()
    if (reason === 'font') epoch++
    if (enabled && (reason === 'width' || reason === 'style' || reason === 'font')) {
      quietUntil = clock() + typographyWorkLimits.quietMs
      native()
    }
    schedule()
  }
  function plans(c: Cycle): MeasuredPlan[] {
    const failedParents = new Set(c.items.filter(i => !i.plan).map(i => i.snapshot.nodeFrom))
    return c.items.flatMap(i => i.plan && !failedParents.has(i.snapshot.nodeFrom) ? [i.plan] : [])
  }
  function fail(c: Cycle, item: Item, reason: string) {
    item.plan = undefined; item.reason = reason
    c.reasons[reason] = (c.reasons[reason] ?? 0) + 1
    const budget = item.job.work.exhausted()
    if (reason === 'budget-exceeded') c.budgetReasons[budget ?? 'measurements'] = (c.budgetReasons[budget ?? 'measurements'] ?? 0) + 1
  }
  function finish(c: Cycle) {
    if (!current(c)) { invalidate('style'); return }
    const stats = c.items.map(i => i.job.work.stats())
    const adopted = new Set(plans(c).map(p => p.snapshotId))
    previous = c.entries
    trace('finish', { plans: plans(c).length, reasons: c.reasons })
    recordFrame(c)
    // Aggregate active time includes extraction and transactions. Per-snapshot
    // budgets remain the job ledger, not a share of this generation overhead.
    const activeMs = c.busyMs
    onDiagnostics({ ...getTypographyProjectionDiagnostics(editor), reasons: c.reasons,
      paragraphs: c.items.map(i => ({ id: i.snapshot.id, status: adopted.has(i.snapshot.id) ? 'planned' : 'fallback',
        reason: i.reason ?? (!adopted.has(i.snapshot.id) ? 'hard-break-group' : undefined), work: i.job.work.stats() })),
      measuredLines: stats.reduce((n, x) => n + x.uniqueMeasurements, 0), elapsedMs: activeMs,
      work: { activeMs, waitingMs: Math.max(0, clock() - c.started - activeMs), edgeChecks: stats.reduce((n, x) => n + x.edgeChecks, 0),
        uniqueMeasurements: stats.reduce((n, x) => n + x.uniqueMeasurements, 0), cacheHits: c.cacheHits,
        maxSliceMs: c.maxSliceMs, maxOperationMs: c.maxOperationMs, revisions: c.revisions, budgetReasons: c.budgetReasons },
    })
    if (cycle === c) cancelCycle()
  }
  function beginChecks(c: Cycle) {
    const ids = new Set(plans(c).map(p => p.snapshotId))
    c.checks = c.items.filter(i => ids.has(i.snapshot.id)).map(item => ({ item, line: 0, lines: [], widths: [], replan: false, retrying: false, done: false }))
    c.phase = 'verify'; c.cursor = 0; c.changed = false
  }
  function checkOne(c: Cycle, check: Check, cp: LayoutCheckpoint) {
    const item = check.item, work = item.job.work
    const reject = (reason: string) => { fail(c, item, reason); check.done = true; c.changed = true }
    if (work.exhausted()) { reject('budget-exceeded'); return }
    if (check.retrying) {
      const result = item.job.step(cp)
      if (result.status === 'yielded' || result.status === 'cancelled') return
      if (result.status === 'planned') { item.plan = result.plan; c.changed = true; check.done = true }
      else reject(result.reason)
      return
    }
    const line = item.plan!.lines[check.line++]
    if (line) {
      let actual: number
      try { actual = (options.readProjectedLine ?? measureProjectedLine)(editor.view, line, item.snapshot) }
      catch { reject('measurement-mismatch'); return }
      if (!live(c)) return
      if (work.exhausted()) { reject('budget-exceeded'); return }
      const result = correctTypographyLine(line, actual)
      if (result.status === 'accepted') check.lines.push(line)
      else if (c.round || !Number.isFinite(actual)) { reject('measurement-mismatch'); return }
      else if (result.status === 'corrected') { check.lines.push({ ...line, gaps: result.gaps }); c.changed = true }
      else {
        check.replan = true; check.lines.push(line)
        check.widths.push({ key: line.key, naturalWidth: actual - line.gaps.reduce((n, g) => n + g.adjustment, 0) })
      }
    }
    if (check.line >= item.plan!.lines.length) {
      if (check.replan) { item.job.replan(check.widths); check.retrying = true }
      else { item.plan = { ...item.plan!, lines: check.lines }; check.done = true }
    }
  }
  function run() {
    frame = 0
    if (disposed || !enabled || composing) return
    if (clock() < quietUntil) { schedule(); return }
    quietUntil = 0
    if (fonts?.status === 'loading') {
      native(); onDiagnostics({ ...getTypographyProjectionDiagnostics(editor), reasons: { 'font-pending': 1 }, measuredLines: 0, elapsedMs: 0 }); return
    }
    const frameStart = clock(), deadline = frameStart + typographyWorkLimits.frameMs
    let accounting: Cycle | undefined = cycle
    if (accounting) { accounting.frameStarted = frameStart; accounting.frameAccounted = 0 }
    try {
    if (!cycle) {
      const revision = getTypographyProjectionRevision(editor)
      const extractionStart = clock()
      const entries = extractProjectionSnapshots(editor.view, revision, epoch)
      const extractionMs = clock() - extractionStart
      if (previous && sameEntries(entries, previous)) return
      const c: Cycle = { token: generation, revision, entries, items: [], cursor: 0, started: frameStart,
        phase: 'planning', checks: [], round: 0, changed: false, reasons: {}, budgetReasons: {}, cacheHits: 0,
        maxSliceMs: 0, maxOperationMs: extractionMs, revisions: 0, busyMs: 0, frameStarted: frameStart, frameAccounted: 0 }
      cycle = accounting = c
      // Job construction is lazy with respect to preparation and measurement.
      for (const e of entries) {
        if (!e.eligible) { c.reasons[e.reason] = (c.reasons[e.reason] ?? 0) + 1; continue }
        const work = createLayoutWork(clock)
        c.items.push({ snapshot: e.snapshot, done: false, job: createTypographyJob({ snapshot: e.snapshot, measurement, work,
          onPlanning: options.onTrace ? value => trace('planning', { id: e.snapshot.id, ...value }) : undefined }) })
      }
    }
    const c = cycle
    if (!live(c)) { cancelCycle(); schedule(); return }
    const cpFor = (item: Item): LayoutCheckpoint => ({ work: item.job.work, shouldYield: () => clock() >= deadline, isCancelled: () => !live(c) })
    for (let operations = 0; operations < 4096 && clock() < deadline; operations++) {
      if (!live(c)) { cancelCycle(); schedule(); return }
      const pending = c.phase === 'planning' ? c.items.filter(i => !i.done) : c.checks.filter(v => !v.done)
      if (!pending.length) {
        if (!current(c)) { invalidate('style'); return }
        if (c.phase === 'planning') {
          previous = undefined
          trace('publish', { plans: plans(c).length })
          timed(c, () => registerTypographySnapshots(editor, true, c.entries, plans(c))); nativePhase = false
          if (!plans(c).length) { finish(c); return }
          beginChecks(c); schedule(); return // Actual DOM reads start next frame.
        }
        if (c.changed) {
          timed(c, () => applyTypographyPlan(editor, plans(c), c.revision))
          if (!c.round && plans(c).length) { c.revisions++; c.round++; beginChecks(c); schedule(); return }
        }
        finish(c); return
      }
      const next = pending[c.cursor++ % pending.length]!
      const item = 'job' in next ? next : next.item
      const start = clock()
      item.job.work.run(() => {
        if (c.phase === 'planning') {
          const result = item.job.step(cpFor(item))
          if (result.status === 'planned') { item.plan = result.plan; item.done = true }
          else if (result.status === 'fallback') { fail(c, item, result.reason); item.done = true }
        } else checkOne(c, next as Check, cpFor(item))
      })
      c.maxOperationMs = Math.max(c.maxOperationMs, clock() - start)
    }
    schedule()
    } finally { if (accounting) recordFrame(accounting) }
  }
  const onTransaction = ({ transaction }: { transaction: { docChanged: boolean } }) => { if (transaction.docChanged) invalidate('document') }
  const onFont = () => invalidate('font'), onStart = () => coordinator.setComposing(true), onEnd = () => coordinator.setComposing(false)
  let observedWidth = root.getBoundingClientRect().width
  const onResize = () => {
    const width = root.getBoundingClientRect().width
    if (Math.abs(width - observedWidth) < .25) return
    observedWidth = width; invalidate('width')
  }
  const resize = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(onResize) : undefined
  resize?.observe(root); editor.on('transaction', onTransaction)
  fonts?.addEventListener('loading', onFont); fonts?.addEventListener('loadingdone', onFont); fonts?.addEventListener('loadingerror', onFont)
  if (fonts?.status === 'loading') void fonts.ready.then(() => { if (!disposed) onFont() })
  root.addEventListener('compositionstart', onStart); root.addEventListener('compositionend', onEnd); win.addEventListener('resize', onResize)
  const mutations = new MutationObserver(records => {
    const inputs = records.filter(r => {
      const target = r.target as Element
      if (r.oldValue === target.getAttribute(r.attributeName!)) return false
      if (!root.contains(target) && !target.contains(root)) return false
      if (target.hasAttribute('data-markleaf-typography-line-id')) return false
      if (r.attributeName === 'class' && /markleaf-typography-(v2|quality-fallback)/.test(`${target.className} ${r.oldValue}`)) return false
      return true
    })
    if (inputs.length) invalidate('style')
  })
  const attributes = { attributes: true, attributeOldValue: true, attributeFilter: ['style', 'class', 'lang', 'dir'] }
  mutations.observe(root, { ...attributes, subtree: true })
  for (let ancestor = root.parentElement; ancestor; ancestor = ancestor.parentElement) mutations.observe(ancestor, attributes)
  const coordinator: TypographyCoordinator = {
    setEnabled(value) {
      if (disposed || value === enabled) return
      enabled = value; generation++; previous = undefined; cancelCycle(); quietUntil = 0; nativePhase = false
      if (!value) { if (frame) cancelFrame(frame); frame = 0; registerTypographySnapshots(editor, false, []) }
      else schedule()
    },
    invalidate,
    setComposing(value) {
      if (disposed || value === composing) return
      composing = value; generation++; cancelCycle()
      if (value) { if (frame) cancelFrame(frame); frame = 0 }
      else schedule()
    },
    dispose() {
      if (disposed) return
      disposed = true; generation++; if (frame) cancelFrame(frame); frame = 0; cancelCycle()
      editor.off('transaction', onTransaction); resize?.disconnect(); mutations.disconnect()
      fonts?.removeEventListener('loading', onFont); fonts?.removeEventListener('loadingdone', onFont); fonts?.removeEventListener('loadingerror', onFont)
      root.removeEventListener('compositionstart', onStart); root.removeEventListener('compositionend', onEnd); win.removeEventListener('resize', onResize)
      if (measurement.onBatch === onBatch) measurement.onBatch = originalBatch
      measurement.dispose()
    },
  }
  return coordinator
}
