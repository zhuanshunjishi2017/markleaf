import { createMeasuredPlanningTask, prepareGapSlots, typographyWorkLimits,
  type ProjectionSnapshot, type MeasurementService, type LayoutWork, type LayoutCheckpoint,
  type GapSlot, type LineMeasurement, type LineRequest, type MeasuredPlan,
  type DiagnosticReason, type LayoutBudgetReason, type MeasuredPlanningTask } from '@markleaf/editor-core'

export type JobResult = { status: 'yielded' } | { status: 'cancelled' }
  | { status: 'fallback'; reason: DiagnosticReason; budgetReason?: LayoutBudgetReason }
  | { status: 'planned'; plan: MeasuredPlan; slots: readonly GapSlot[]; measurements: ReadonlyMap<string, LineMeasurement> }
export type TypographyJob = {
  readonly work: LayoutWork
  step(checkpoint: LayoutCheckpoint): JobResult
  replan(values: readonly LineMeasurement[]): void
  dispose(): void
}

export function createTypographyJob(options: {
  snapshot: ProjectionSnapshot; supportedEdges?: ReadonlySet<string>; measurement: MeasurementService; work: LayoutWork
  onPlanning?: (value: { status: string; requests?: readonly LineRequest[] }) => void
}): TypographyJob {
  const { snapshot, work, measurement } = options
  const spaceWidths = new Map<number, number>(), measurements = new Map<string, LineMeasurement>()
  let slots: readonly GapSlot[] = [], planner: MeasuredPlanningTask | undefined
  let pending: readonly LineRequest[] = [], pendingIndex = 0, batchSize: number = typographyWorkLimits.initialBatch
  let acquisition: 'estimated' | 'reachable' = 'estimated'
  let probingSpaces = true, disposed = false, replanned = false, terminal: JobResult | undefined
  function* prepare(): Generator<void, void> {
    const spaces: LineRequest[] = []
    for (let from = 0; from < snapshot.text.length; from++) {
      yield
      if (snapshot.text[from] === ' ') spaces.push({ from, to: from + 1, contentTo: from + 1, key: `space:${from}` })
    }
    pending = spaces
  }
  let preparation: Generator<void, unknown> = prepare()
  let spacesPrepared = false, slotsPrepared = false
  const budgetFailure = (): JobResult => ({ status: 'fallback', reason: 'budget-exceeded', budgetReason: work.exhausted() ?? 'measurements' })
  return {
    work,
    step(checkpoint) {
      if (disposed || checkpoint.isCancelled()) return { status: 'cancelled' }
      if (terminal) return terminal
      if (work.exhausted()) return terminal = budgetFailure()
      if (checkpoint.shouldYield()) return { status: 'yielded' }
      if (!spacesPrepared || !slotsPrepared && !probingSpaces) {
        for (let n = 0; n < 64; n++) {
          if (checkpoint.isCancelled()) return { status: 'cancelled' }
          if (work.exhausted()) return terminal = budgetFailure()
          if (checkpoint.shouldYield()) return { status: 'yielded' }
          const next = preparation.next()
          if (next.done) {
            if (!spacesPrepared) spacesPrepared = true
            else {
              // The shared DOM recipe supports every current slot shape. A
              // restricted consumer may still provide an explicit subset.
              const prepared = next.value as readonly GapSlot[]
              slots = options.supportedEdges ? prepared.filter(s => options.supportedEdges!.has(s.id)) : prepared
              slotsPrepared = true
              planner = createMeasuredPlanningTask({ snapshot, slots, measurements, excludedEdges: new Set(), mode: 'justified' })
            }
            break
          }
        }
        return { status: 'yielded' }
      }
      if (pendingIndex < pending.length) {
        const batch = pending.slice(pendingIndex, pendingIndex + batchSize)
        if (!work.reserveMeasurements(batch.map(r => probingSpaces ? r.key : `line:${r.key}`))) return terminal = budgetFailure()
        const start = work.stats().activeMs
        let values: readonly LineMeasurement[]
        try { values = measurement.measureBatch(snapshot, batch, probingSpaces ? [] : slots) }
        catch { return terminal = { status: 'fallback', reason: 'measurement-mismatch' } }
        if (checkpoint.isCancelled()) return { status: 'cancelled' }
        if (work.exhausted()) return terminal = budgetFailure()
        const byKey = new Map(values.map(m => [m.key, m]))
        if (batch.some(r => !Number.isFinite(byKey.get(r.key)?.naturalWidth) || byKey.get(r.key)!.naturalWidth < 0))
          return terminal = { status: 'fallback', reason: 'measurement-mismatch' }
        batch.forEach(r => { const m = byKey.get(r.key)!; if (probingSpaces) spaceWidths.set(r.from, m.naturalWidth); else measurements.set(r.key, m) })
        const used = work.stats().activeMs - start
        batchSize = used >= 2 ? Math.max(1, Math.floor(batchSize / 2)) : used <= 1 ? Math.min(16, batchSize + 1) : batchSize
        pendingIndex += batch.length
        return { status: 'yielded' }
      }
      if (probingSpaces) {
        probingSpaces = false; pending = []; pendingIndex = 0
        preparation = prepareGapSlots(snapshot, spaceWidths)
        return { status: 'yielded' }
      }
      if (pending.length) {
        if (acquisition === 'estimated') {
          // Replace the second speculative rescan with one persistent measured
          // frontier. Its dependency-driven batches share the original ledger.
          acquisition = 'reachable'; planner!.dispose()
          planner = createMeasuredPlanningTask({ snapshot, slots, measurements, excludedEdges: new Set(), mode: 'justified', measurementStrategy: 'reachable' })
        } else planner!.supplyMeasurements([...measurements.values()])
        pending = []; pendingIndex = 0
      }
      const result = planner!.step(checkpoint)
      if (result.status !== 'yielded') options.onPlanning?.({ status: result.status, ...('requests' in result ? { requests: result.requests } : {}) })
      if (result.status === 'needs-measurement') {
        pending = result.requests; pendingIndex = 0
        return { status: 'yielded' }
      }
      if (result.status === 'planned') return terminal = { ...result, slots, measurements }
      if (result.status === 'fallback') return terminal = result.reason === 'budget-exceeded' ? budgetFailure() : result
      return result
    },
    replan(values) {
      if (disposed || replanned || !planner) { terminal = { status: 'fallback', reason: 'measurement-mismatch' }; return }
      replanned = true; terminal = undefined
      values.forEach(m => measurements.set(m.key, m)); planner.supplyMeasurements(values, true)
    },
    dispose() { disposed = true; planner?.dispose(); preparation.return(undefined); pending = [] },
  }
}
