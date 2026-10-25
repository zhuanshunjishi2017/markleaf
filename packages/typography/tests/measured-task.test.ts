import { describe, expect, it } from 'vitest'
import { createMeasuredPlanningTask } from '../src/measured-task'
import { createLayoutWork } from '../src/layout-work'
import { candidateGapSlots } from '../src/spacing'
import type { ParagraphSnapshot } from '../src/layout-model'
const snapshot: ParagraphSnapshot = { id: 'p', revision: 1, environmentKey: 'env', text: '中文中文', width: 32,
  graphemeOffsets: [0, 1, 2, 3, 4], language: 'zh', direction: 'ltr', alignment: 'default',
  runs: [{ from: 0, to: 4, styleKey: 's', css: { 'font-size': '16px' } }] }
function solve(slice: number) {
  const work = createLayoutWork(() => 0)
  const task = createMeasuredPlanningTask({ snapshot, slots: [], measurements: new Map(), excludedEdges: new Set(), mode: 'natural' })
  let yields = 0
  for (let guard = 0; guard < 1000; guard++) {
    let checks = 0
    const result = task.step({ work, shouldYield: () => ++checks > slice, isCancelled: () => false })
    if (result.status === 'yielded') { yields++; continue }
    if (result.status === 'needs-measurement') {
      task.supplyMeasurements(result.requests.map(r => ({ key: r.key, naturalWidth: (r.contentTo - r.from) * 16 })))
      continue
    }
    return { result, yields, checks: work.stats().edgeChecks }
  }
  throw Error('task never terminated')
}
describe('resumable measured planning', () => {
  it.each([1, 1000])('continuesMeasuredReachabilityWithoutSpeculativePruning slice=%s', slice => {
    const text = '中'.repeat(21), work = createLayoutWork(() => 0)
    const task = createMeasuredPlanningTask({ snapshot: { ...snapshot, text, width: 100,
      graphemeOffsets: Array.from({ length: 22 }, (_, i) => i), runs: [{ ...snapshot.runs[0]!, to: 21 }] },
      slots: [], measurements: new Map(), excludedEdges: new Set(['0:11:11']), mode: 'justified', measurementStrategy: 'reachable' })
    const queried = new Set<string>()
    for (let guard = 0; guard < 10_000; guard++) {
      let calls = 0
      const result = task.step({ work, shouldYield: () => ++calls > slice, isCancelled: () => false })
      if (result.status === 'yielded') continue
      if (result.status === 'needs-measurement') {
        expect(result.requests.length).toBeLessThanOrEqual(4)
        for (const r of result.requests) {
          expect(r.key).not.toBe('0:11:11')
          expect(queried.has(r.key)).toBe(false)
          queried.add(r.key)
        }
        task.supplyMeasurements(result.requests.map(r => ({ key: r.key, naturalWidth: (r.contentTo - r.from) * 10 })))
        continue
      }
      expect(result).toMatchObject({ status: 'planned', plan: { lines: [{ from: 0, to: 10 }, { from: 10, to: 20 }, { from: 20, to: 21 }] } })
      task.dispose()
      return
    }
    throw Error('reachable acquisition did not terminate')
  })
  it('backtracksFromAMeasuredDeadEndAndCancelsWhileAwaitingWidths', () => {
    const task = createMeasuredPlanningTask({ snapshot, slots: [], mode: 'justified', measurementStrategy: 'reachable',
      measurements: new Map([['0:1:1', { key: '0:1:1', naturalWidth: 32 }]]), excludedEdges: new Set() })
    const cp = { work: createLayoutWork(() => 0), shouldYield: () => false, isCancelled: () => false }
    let done = false
    for (let i = 0; i < 200; i++) {
      const result = task.step(cp)
      if (result.status === 'yielded') continue
      if (result.status === 'needs-measurement') {
        expect(task.step({ ...cp, isCancelled: () => true })).toEqual({ status: 'cancelled' })
        task.supplyMeasurements(result.requests.map(r => ({ key: r.key,
          naturalWidth: r.key === '0:2:2' ? 32 : r.key === '2:4:4' ? 16 : 64 })))
        continue
      }
      expect(result).toMatchObject({ status: 'planned', plan: { lines: [{ from: 0, to: 2 }, { from: 2, to: 4 }] } })
      done = true; break
    }
    expect(done).toBe(true)
    task.dispose()
    expect(task.step(cp)).toEqual({ status: 'cancelled' })
  })
  it('keepsNaturalLineSpacingDuringReachableAcquisition', () => {
    const task = createMeasuredPlanningTask({ snapshot, slots: candidateGapSlots(snapshot, new Map()), mode: 'natural',
      measurementStrategy: 'reachable', measurements: new Map(), excludedEdges: new Set() })
    const cp = { work: createLayoutWork(() => 0), shouldYield: () => false, isCancelled: () => false }
    for (let i = 0; i < 500; i++) {
      const result = task.step(cp)
      if (result.status === 'yielded') continue
      if (result.status === 'needs-measurement') {
        task.supplyMeasurements(result.requests.map(r => ({ key: r.key, naturalWidth: (r.contentTo - r.from) * 15 })))
        continue
      }
      expect(result).toMatchObject({ status: 'planned', plan: { lines: [{ from: 0, to: 2, naturalWidth: 30 }, { from: 2, to: 4, naturalWidth: 30 }] } })
      if (result.status === 'planned') expect(result.plan.lines.flatMap(l => l.gaps).every(g => g.adjustment === 0)).toBe(true)
      task.dispose(); return
    }
    throw Error('natural acquisition did not terminate')
  })
  it('discardsSuspendedAcquisitionWhenCorrectionFinalizesAMeasuredGraph', () => {
    const task = createMeasuredPlanningTask({ snapshot, slots: [], mode: 'justified', measurementStrategy: 'reachable',
      measurements: new Map(), excludedEdges: new Set() })
    const cp = { work: createLayoutWork(() => 0), shouldYield: () => false, isCancelled: () => false }
    let result = task.step(cp)
    while (result.status === 'yielded') result = task.step(cp)
    expect(result.status).toBe('needs-measurement')
    task.supplyMeasurements([{ key: '0:1:1', naturalWidth: 32 }, { key: '1:4:4', naturalWidth: 16 }], true)
    do { result = task.step(cp) } while (result.status === 'yielded')
    expect(result).toMatchObject({ status: 'planned', plan: { lines: [{ from: 0, to: 1 }, { from: 1, to: 4 }] } })
    task.dispose()
  })
  it('chargesNeighborCandidateChecksToTheSameEdgeBudget', () => {
    const work = createLayoutWork(() => 0)
    const task = createMeasuredPlanningTask({ snapshot, slots: [], measurements: new Map(), excludedEdges: new Set(), mode: 'natural' })
    const cp = { work, shouldYield: () => false, isCancelled: () => false }
    let result = task.step(cp)
    while (result.status === 'yielded') result = task.step(cp)
    expect(result.status).toBe('needs-measurement')
    // Four boundaries: 1+2+3+4 DP pairs, then 2*3 + 3*2 neighbor pairs.
    // Invalid/eagerly-rejected pairs still consumed an edge check.
    expect(work.stats().edgeChecks).toBe(22)
  })
  it('canFinalizeTheBestFullyMeasuredPathWhenAnUnmeasuredEstimateRanksHigher', () => {
    const task = createMeasuredPlanningTask({ snapshot, slots: [], excludedEdges: new Set(), mode: 'natural',
      measurements: new Map([['0:1:1', 16], ['1:4:4', 32], ['2:4:4', 32], ['0:3:3', 48], ['0:4:4', 64]].map(([key, naturalWidth]) => [key as string, { key: key as string, naturalWidth: naturalWidth as number }])) })
    const cp = { work: createLayoutWork(() => 0), shouldYield: () => false, isCancelled: () => false }
    let result = task.step(cp)
    while (result.status === 'yielded') result = task.step(cp)
    expect(result.status).toBe('needs-measurement')
    task.supplyMeasurements([], true)
    do { result = task.step(cp) } while (result.status === 'yielded')
    expect(result).toMatchObject({ status: 'planned', plan: { lines: [{ from: 0, to: 1 }, { from: 1, to: 4 }] } })
  })
  it('resumesInsidePreparationAndEdgeSearchWithoutRevisitingEdges', () => {
    const tiny = solve(1), large = solve(1000)
    expect(tiny.yields).toBeGreaterThan(10)
    expect(tiny.checks).toBe(large.checks)
    expect(tiny.result).toEqual(large.result)
    expect(tiny.result).toMatchObject({ status: 'planned', plan: { lines: [{ from: 0, to: 2 }, { from: 2, to: 4 }] } })
  })
  it.each([1, 7, 100])('sliceSizeDoesNotChangePlan %s', size => {
    expect(solve(size).result).toEqual(solve(1000).result)
  })
  it('rescoresAfterNewWidthsInsteadOfKeepingAnEstimatedPath', () => {
    const task = createMeasuredPlanningTask({ snapshot: { ...snapshot, text: 'iiii', width: 20 }, slots: [], measurements: new Map(), excludedEdges: new Set(), mode: 'justified' })
    const checkpoint = { work: createLayoutWork(() => 0), shouldYield: () => false, isCancelled: () => false }
    let first = task.step(checkpoint)
    while (first.status === 'yielded') first = task.step(checkpoint)
    expect(first).toMatchObject({ status: 'needs-measurement', requests: [{ key: '0:4:4' }] })
    task.supplyMeasurements([{ key: '0:4:4', naturalWidth: 14 }])
    let result = task.step(checkpoint)
    while (result.status === 'yielded') result = task.step(checkpoint)
    expect(result).toMatchObject({ status: 'planned', plan: { lines: [{ naturalWidth: 14 }] } })
  })
  it('cancelsAndDisposesWithoutReturningAnOldPlan', () => {
    const task = createMeasuredPlanningTask({ snapshot, slots: [], measurements: new Map(), excludedEdges: new Set(), mode: 'natural' })
    const checkpoint = { work: createLayoutWork(() => 0), shouldYield: () => false, isCancelled: () => true }
    expect(task.step(checkpoint)).toEqual({ status: 'cancelled' })
    task.dispose()
    expect(task.step({ ...checkpoint, isCancelled: () => false })).toEqual({ status: 'cancelled' })
  })
})
