import { describe, expect, it } from 'vitest'
import { createLayoutWork } from '../../typography/src/layout-work'
import { createTypographyJob } from '../src/typography-job'
import type { ProjectionSnapshot } from '@markleaf/editor-core'
const snapshot: ProjectionSnapshot = { id: 'p', revision: 0, environmentKey: 'env', text: '中 文 中 文 中 文 中 文', width: 128,
  graphemeOffsets: Array.from({ length: 16 }, (_, i) => i), language: 'zh', direction: 'ltr', alignment: 'default',
  runs: [{ from: 0, to: 15, styleKey: 's', css: { 'font-size': '16px' } }], nodeFrom: 0, nodeTo: 17, segmentFrom: 1, segmentTo: 16, }
describe('typography paragraph job', () => {
  it('doesNotTreatSpaceProbeCompletionAsTheInitialLineAcquisition', () => {
    const work = createLayoutWork(() => 0)
    const requests: Array<readonly { from: number }[]> = []
    const job = createTypographyJob({ snapshot: { ...snapshot, width: 200 }, work,
      onPlanning: p => { if (p.requests) requests.push(p.requests) },
      measurement: { dispose() {}, measureBatch(_s, lines) {
        return lines.map(l => ({ key: l.key, naturalWidth: l.key.startsWith('space:') ? 4 : (l.contentTo - l.from) * 8 }))
      } } })
    const cp = { work, shouldYield: () => false, isCancelled: () => false }
    for (let i = 0; !requests.length && i < 1000; i++) job.step(cp)
    // Initial estimated paths include their following-line/neighbor seeds;
    // mistaking spaces for a completed phase jumps straight to start0 probes.
    expect(requests[0]?.some(r => r.from > 0)).toBe(true)
    job.dispose()
  })
  it('connectsMeasuredFrontiersBeyondTheTwoInitialEstimatedPaths', () => {
    // Ten actual glyphs fill100px; the16px estimator cannot predict that.
    // A measured0→10 and10→20 still needs the previously unmeasured20→21.
    const text = '中'.repeat(21), work = createLayoutWork(() => 0)
    const measured = new Set<string>()
    const job = createTypographyJob({ snapshot: { ...snapshot, text, width: 100,
      graphemeOffsets: Array.from({ length: 22 }, (_, i) => i), runs: [{ ...snapshot.runs[0]!, to: 21 }] },
      supportedEdges: new Set(), work, measurement: { dispose() {}, measureBatch(_s, lines) {
        for (const line of lines) {
          expect(measured.has(line.key)).toBe(false)
          measured.add(line.key)
        }
        return lines.map(l => ({ key: l.key, naturalWidth: (l.contentTo - l.from) * 10 }))
      } } })
    const cp = { work, shouldYield: () => false, isCancelled: () => false }
    let result = job.step(cp)
    for (let i = 0; result.status === 'yielded' && i < 2000; i++) result = work.run(() => job.step(cp))
    expect(result).toMatchObject({ status: 'planned', plan: { lines: [
      { from: 0, to: 10, naturalWidth: 100 }, { from: 10, to: 20, naturalWidth: 100 }, { from: 20, to: 21, naturalWidth: 10 },
    ] } })
    expect(work.stats().uniqueMeasurements).toBeLessThanOrEqual(256)
    job.dispose()
  })
  it('yieldsBetweenAdaptiveMeasurementBatchesAndCountsUniqueRequests', () => {
    let now = 0
    const sizes: number[] = [], work = createLayoutWork(() => now)
    const job = createTypographyJob({ snapshot, supportedEdges: new Set(), work,
      measurement: { dispose() {}, measureBatch(_s, lines) { sizes.push(lines.length); now += 3; return lines.map(l => ({ key: l.key, naturalWidth: l.key.startsWith('space:') ? 4 : (l.contentTo - l.from) * 8 })) } } })
    const cp = { work, shouldYield: () => false, isCancelled: () => false }
    for (let i = 0; i < 300; i++) { const r = work.run(() => job.step(cp)); if (r.status !== 'yielded') break }
    expect(sizes.slice(0, 3)).toEqual([4, 2, 1])
    expect(work.stats().uniqueMeasurements).toBeLessThanOrEqual(256)
    expect(sizes.every(n => n <= 16)).toBe(true)
  })
  it('sharesTheMeasurementQuotaWithReachableAcquisitionInsteadOfClaimingNoSolution', () => {
    const text = '中'.repeat(120), work = createLayoutWork(() => 0)
    const phases: number[] = [], measured = new Set<string>()
    const job = createTypographyJob({ snapshot: { ...snapshot, text, width: 100,
      graphemeOffsets: Array.from({ length: 121 }, (_, i) => i), runs: [{ ...snapshot.runs[0]!, to: 120 }] }, work,
      onPlanning: p => { if (p.requests) phases.push(p.requests.length) },
      measurement: { dispose() {}, measureBatch(_s, lines) {
        for (const line of lines) { expect(measured.has(line.key)).toBe(false); measured.add(line.key) }
        return lines.map(l => ({ key: l.key, naturalWidth: 1000 }))
      } } })
    const cp = { work, shouldYield: () => false, isCancelled: () => false }
    let result = job.step(cp)
    for (let i = 0; result.status === 'yielded' && i < 5000; i++) result = work.run(() => job.step(cp))
    expect(phases.length).toBeGreaterThan(2)
    expect(result).toMatchObject({ status: 'fallback', reason: 'budget-exceeded', budgetReason: 'measurements' })
    expect(work.stats().uniqueMeasurements).toBeLessThanOrEqual(256)
    const count = measured.size
    expect(job.step(cp)).toEqual(result)
    expect(measured.size).toBe(count)
    job.dispose()
  })
  it.each(['slow', 'nan', 'missing'] as const)('terminatesMalformedOrOverBudgetMeasurement %s', kind => {
    let now = 0, reads = 0
    const work = createLayoutWork(() => now)
    const job = createTypographyJob({ snapshot, supportedEdges: new Set(), work,
      measurement: { dispose() {}, measureBatch(_s, lines) { reads++; if (kind === 'slow') now += 129; return kind === 'missing' ? [] : lines.map(l => ({ key: l.key, naturalWidth: kind === 'nan' ? NaN : 4 })) } } })
    const cp = { work, shouldYield: () => false, isCancelled: () => false }
    let result = work.run(() => job.step(cp))
    for (let i = 0; result.status === 'yielded' && i < 100; i++) result = work.run(() => job.step(cp))
    expect(result).toMatchObject({ status: 'fallback', reason: kind === 'slow' ? 'budget-exceeded' : 'measurement-mismatch' })
    const count = reads
    now += 1000
    expect(work.run(() => job.step(cp))).toEqual(result)
    expect(reads).toBe(count)
  })
})
