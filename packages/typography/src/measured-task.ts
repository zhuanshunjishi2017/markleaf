import type { GapSlot, LineMeasurement, LineRequest, MeasuredLine, MeasuredPlan, ParagraphSnapshot } from './layout-model'
import type { PlanningResult } from './measured-plan'
import { typographyLineKey } from './measured-plan'
import { typographyWorkLimits, type LayoutCheckpoint } from './layout-work'
import { prepareBreakOpportunities, graphemeOffsets, type BreakOpportunity } from './opportunities'
import { allocateGaps } from './spacing'

export type MeasuredPlanningInput = {
  snapshot: ParagraphSnapshot; slots: readonly GapSlot[]; measurements: ReadonlyMap<string, LineMeasurement>
  excludedEdges: ReadonlySet<string>; mode: 'natural' | 'justified'
  measurementStrategy?: 'estimated' | 'reachable'
}
export type MeasuredPlanningTask = {
  step(checkpoint: LayoutCheckpoint): PlanningResult | { status: 'yielded' } | { status: 'cancelled' }
  supplyMeasurements(values: readonly LineMeasurement[], measuredOnly?: boolean): void
  dispose(): void
}
type Path = { cost: number; line?: MeasuredLine; previous?: Path }

/** Generator checkpoints retain the exact preparation/search/backtracking cursor. */
export function createMeasuredPlanningTask(input: MeasuredPlanningInput): MeasuredPlanningTask {
  const { snapshot: s, slots } = input
  const measurements = new Map(input.measurements)
  const prefix = new Map<number, number>([[0, 0]])
  let opportunities: readonly BreakOpportunity[] = [], prepared = false, disposed = false
  let checkpoint: LayoutCheckpoint
  let terminal: PlanningResult | undefined
  let measuredOnly = false
  let waiting: readonly LineRequest[] | undefined
  const slotIndex: Array<{ slot: GapSlot; order: number }> = []
  function* prepare(): Generator<void, void> {
    let total = 0, run = 0
    const offsets = graphemeOffsets(s.text)
    for (let i = 0; i < offsets.length - 1; i++) {
      yield
      const from = offsets[i]!, to = offsets[i + 1]!, text = s.text.slice(from, to)
      while (run + 1 < s.runs.length && s.runs[run]!.to <= from) run++
      const size = parseFloat(s.runs[run]?.css['font-size'] ?? '') || 16
      total += size * (/\p{Script=Han}|\p{Extended_Pictographic}/u.test(text) ? 1 : text === ' ' ? .25 : .53)
      prefix.set(to, total)
    }
    opportunities = yield* prepareBreakOpportunities(s.text)
    for (let order = 0; order < slots.length; order++) { slotIndex.push({ slot: slots[order]!, order }); yield }
    slotIndex.sort((a, b) => a.slot.owner.from - b.slot.owner.from || a.order - b.order)
    prepared = true
  }
  function lineSlots(from: number, contentTo: number) {
    let low = 0, high = slotIndex.length
    while (low < high) { const mid = (low + high) >>> 1; if (slotIndex[mid]!.slot.owner.from < from) low = mid + 1; else high = mid }
    const result: typeof slotIndex = []
    for (let i = low; i < slotIndex.length && slotIndex[i]!.slot.owner.from < contentTo; i++) {
      const entry = slotIndex[i]!, x = entry.slot
      if (x.owner.to <= contentTo && (x.kind === 'word-space' || x.at > from && x.at < contentTo)) result.push(entry)
    }
    return result.sort((a, b) => a.order - b.order).map(x => x.slot)
  }
  /** The second acquisition phase never follows an unmeasured edge. Estimates
   * only order legal ends; a dead end backtracks rather than pruning neighbors.
   * A measurement yield suspends this cursor, not a fresh whole-graph search. */
  function* acquireReachable(): Generator<void | readonly LineRequest[], PlanningResult> {
    if (!(s.width > 0) || !Number.isFinite(s.width)) return { status: 'fallback', reason: 'measurement-mismatch' }
    if (!prepared) yield* prepare()
    const dead = new Set<number>()
    let missedCapacity = false, budgetExceeded = false
    function request(from: number, to: number): LineRequest | undefined {
      let contentTo = to
      while (contentTo > from && s.text[contentTo - 1] === ' ') contentTo--
      const key = typographyLineKey(from, to, contentTo)
      return contentTo > from && !input.excludedEdges.has(key) ? { from, to, contentTo, key } : undefined
    }
    function* follow(r: LineRequest): Generator<void | readonly LineRequest[], boolean> {
      const naturalWidth = measurements.get(r.key)?.naturalWidth
      if (naturalWidth === undefined || !Number.isFinite(naturalWidth) || naturalWidth < 0) return false
      const final = r.to === s.text.length, delta = s.width - naturalWidth
      if ((final || input.mode === 'natural') && delta < -.5) return false
      const allocation = allocateGaps(lineSlots(r.from, r.contentTo), final || input.mode === 'natural' || Math.abs(delta) <= .5 ? 0 : delta)
      if (!allocation.feasible) { missedCapacity = true; return false }
      return final || (yield* visit(r.to))
    }
    function* visit(from: number): Generator<void | readonly LineRequest[], boolean> {
      if (dead.has(from)) return false
      // Existing measured edges get first refusal. Their combined scale is a
      // ranking hint only; mixed fonts/kerning cannot make it an exclusion rule.
      let observed = 0, estimated = 0
      const known: LineRequest[] = []
      for (const [key, m] of measurements) {
        yield
        const [start, to, contentTo] = key.split(':').map(Number)
        const advance = (prefix.get(contentTo!) ?? 0) - (prefix.get(start!) ?? 0)
        if (advance > 0 && m.naturalWidth > 0) { observed += m.naturalWidth; estimated += advance }
        if (start === from) {
          const r = request(from, to!)
          if (r) known.push(r)
        }
      }
      for (const r of known) {
        yield
        if (!checkpoint.work.visitEdge()) { budgetExceeded = true; return false }
        if (yield* follow(r)) return true
        if (budgetExceeded) return false
      }
      const scale = estimated > 0 ? observed / estimated : 1
      const target = prefix.get(from)! + s.width / scale
      let low = 0, high = opportunities.length
      while (low < high) {
        const mid = (low + high) >>> 1
        if (prefix.get(opportunities[mid]!.at)! < target) low = mid + 1
        else high = mid
      }
      let left = low - 1, right = low
      // Expand from the estimated fit in both directions, without discarding
      // any legal endpoint. No eager sort/scan of the entire unknown graph.
      while (left >= 0 || right < opportunities.length) {
        const batch: LineRequest[] = []
        while (batch.length < typographyWorkLimits.initialBatch && (left >= 0 || right < opportunities.length)) {
          yield
          if (!checkpoint.work.visitEdge()) { budgetExceeded = true; return false }
          const leftEnd = opportunities[left], rightEnd = opportunities[right]
          const takeLeft = leftEnd && (!rightEnd || Math.abs(prefix.get(leftEnd.at)! - target) <= Math.abs(prefix.get(rightEnd.at)! - target))
          const to = takeLeft ? opportunities[left--]!.at : opportunities[right++]!.at
          if (to <= from) { left = -1; continue }
          const r = request(from, to)
          if (r && !measurements.has(r.key)) batch.push(r)
        }
        if (!batch.length) continue
        yield batch
        for (const r of batch) {
          yield
          if (yield* follow(r)) return true
          if (budgetExceeded) return false
        }
      }
      dead.add(from)
      return false
    }
    if (!(yield* visit(0))) return { status: 'fallback', reason: budgetExceeded ? 'budget-exceeded' : missedCapacity ? 'capacity-exceeded' : 'no-legal-break' }
    // Use the existing cost model to choose the best path in the graph actually
    // acquired, not the first DFS parent. This is not a global-optimum claim.
    measuredOnly = true
    return yield* search()
  }
  function* search(): Generator<void, PlanningResult> {
    if (!(s.width > 0) || !Number.isFinite(s.width)) return { status: 'fallback', reason: 'measurement-mismatch' }
    if (!prepared) yield* prepare()
    const paths = new Map<number, Path>([[0, { cost: 0 }]])
    const disputed = new Map<string, LineRequest>()
    let missedCapacity = false
    for (const end of opportunities) {
      let best: Path | undefined
      for (const [from, previous] of paths) {
        yield
        if (!checkpoint.work.visitEdge()) return { status: 'fallback', reason: 'budget-exceeded' }
        if (from >= end.at) continue
        let contentTo = end.at
        while (contentTo > from && s.text[contentTo - 1] === ' ') contentTo--
        if (contentTo === from) continue
        const key = typographyLineKey(from, end.at, contentTo)
        if (input.excludedEdges.has(key)) continue
        const request = { from, to: end.at, contentTo, key }
        const measured = measurements.get(key)
        if (measuredOnly && !measured) continue
        const selected = lineSlots(from, contentTo)
        const naturalWidth = measured?.naturalWidth ?? (prefix.get(contentTo)! - prefix.get(from)! + selected.reduce((n, x) => n + (x.edge === 'space' ? 0 : x.natural), 0))
        if (!Number.isFinite(naturalWidth) || naturalWidth < 0) continue
        const final = end.at === s.text.length, delta = s.width - naturalWidth
        const dispute = () => { if (!measured && disputed.size < 257) disputed.set(key, request) }
        if ((final || input.mode === 'natural') && delta < -.5) { dispute(); continue }
        const allocation = allocateGaps(selected, final || input.mode === 'natural' || Math.abs(delta) <= .5 ? 0 : delta)
        if (!allocation.feasible) { missedCapacity = true; dispute(); continue }
        const cost = final ? 0 : 10000 + (input.mode === 'natural' ? 10000 * (delta / s.width) ** 2 : 100 * allocation.cost) + end.preferenceCost
        if (!best || previous.cost + cost < best.cost) best = { cost: previous.cost + cost, previous,
          line: { ...request, naturalWidth, targetWidth: s.width, slots: selected, gaps: allocation.gaps, cost, breakReason: final ? 'paragraph-end' : 'optimal' } }
      }
      if (best) paths.set(end.at, best)
    }
    const best = paths.get(s.text.length)
    if (!best) {
      if (disputed.size > 256) return { status: 'fallback', reason: 'budget-exceeded' }
      if (disputed.size) return { status: 'needs-measurement', requests: [...disputed.values()] }
      return { status: 'fallback', reason: missedCapacity ? 'capacity-exceeded' : 'no-legal-break' }
    }
    const lines: MeasuredLine[] = []
    for (let path: Path | undefined = best; path?.line; path = path.previous) { lines.push(path.line); yield }
    lines.reverse()
    const requests = new Map(lines.filter(l => !measurements.has(l.key)).map(l => [l.key, { from: l.from, to: l.to, contentTo: l.contentTo, key: l.key }]))
    if (requests.size) {
      const boundaries = [0, ...opportunities.map(o => o.at)], indices = new Map(boundaries.map((at, i) => [at, i]))
      for (const line of lines) {
        const fromIndex = indices.get(line.from)!, toIndex = indices.get(line.to)!
        for (const from of boundaries.slice(Math.max(0, fromIndex - 1), fromIndex + 2)) {
          for (const to of boundaries.slice(Math.max(0, toIndex - 1), toIndex + 2)) {
            yield
            if (!checkpoint.work.visitEdge()) return { status: 'fallback', reason: 'budget-exceeded' }
            if (from >= to) continue
            let contentTo = to
            while (contentTo > from && s.text[contentTo - 1] === ' ') contentTo--
            const key = typographyLineKey(from, to, contentTo)
            if (contentTo > from && !measurements.has(key) && !input.excludedEdges.has(key)) requests.set(key, { from, to, contentTo, key })
          }
        }
      }
      if (requests.size > 256) return { status: 'fallback', reason: 'budget-exceeded' }
      return { status: 'needs-measurement', requests: [...requests.values()] }
    }
    return { status: 'planned', plan: { snapshotId: s.id, revision: s.revision, environmentKey: s.environmentKey, lines, verified: true } }
  }
  const startSearch = () => input.measurementStrategy === 'reachable' && !measuredOnly ? acquireReachable() : search()
  let iterator = startSearch()
  return {
    step(next) {
      checkpoint = next
      for (let operations = 0; operations < 64; operations++) {
        if (disposed || next.isCancelled()) return { status: 'cancelled' }
        if (next.work.exhausted()) return terminal = { status: 'fallback', reason: 'budget-exceeded' }
        if (terminal) return terminal
        if (next.shouldYield()) return { status: 'yielded' }
        if (waiting) return { status: 'needs-measurement', requests: waiting }
        const result = iterator.next()
        if (result.done) return terminal = result.value
        if (result.value) return { status: 'needs-measurement', requests: waiting = result.value }
      }
      return { status: 'yielded' }
    },
    supplyMeasurements(values, finalize = false) {
      values.forEach(m => measurements.set(m.key, m))
      if (waiting && !finalize) {
        if (waiting.every(r => measurements.has(r.key))) waiting = undefined
        return
      }
      waiting = undefined
      measuredOnly = finalize
      terminal = undefined
      iterator.return({ status: 'fallback', reason: 'stale' })
      iterator = startSearch()
    },
    dispose() { disposed = true; iterator.return({ status: 'fallback', reason: 'stale' }); measurements.clear() },
  }
}
