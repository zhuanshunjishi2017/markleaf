import { describe, expect, it } from 'vitest'
import { allocateGaps } from '../src/spacing'
import type { GapSlot } from '../src/layout-model'

const slot = (id: string, natural = 10, min = 8, max = 14): GapSlot => ({
  id, natural, min, max, at: 1, kind: 'word-space', weight: 1,
  owner: { from: 0, to: 1 }, edge: 'space', leftStyleKey: '', rightStyleKey: '',
})
const widths = (delta: number) => {
  const r = allocateGaps([slot('a'), slot('b')], delta)
  if (!r.feasible) throw Error('infeasible')
  return r.gaps.map(x => x.width)
}
describe('bounded spacing allocation', () => {
  it('keepsNormalAllocationBeforeBorrowingMixedOverflow', () => {
    const mixed: GapSlot = { ...slot('mixed', 2, 1.28, 9.6), kind: 'mixed', preferredMax: 3.2 }
    const ordinary = slot('space')
    const normal = allocateGaps([{ ...mixed, max: 3.2, preferredMax: undefined }, ordinary], 3)
    expect(allocateGaps([mixed, ordinary], 3)).toEqual(normal)
    const expanded = allocateGaps([mixed, ordinary], 8)
    if (!expanded.feasible || !normal.feasible) throw Error('mixed overflow should remain feasible')
    expect(expanded.gaps.map(g => g.width)).toEqual([6, 14])
    expect(expanded.residual).toBeCloseTo(0, 8)
    expect(expanded.cost).toBeGreaterThan(normal.cost)
    expect(allocateGaps([mixed, ordinary], 12)).toEqual({ feasible: false, reason: 'capacity-exceeded' })
    expect(allocateGaps([mixed, ordinary], -2)).toEqual(allocateGaps([{ ...mixed, max: 3.2, preferredMax: undefined }, ordinary], -2))
  })
  it('sharesMixedOverflowByItsLoosenessCostInsteadOfPhysicalSpaceCeiling', () => {
    const mixed: GapSlot = { ...slot('mixed', 2, 1.28, 9.6), kind: 'mixed', preferredMax: 3.2, weight: .5 }
    const word = { ...slot('space', 4, 3.2, 100), preferredMax: 5.6 }
    const narrow = allocateGaps([word, mixed], 10)
    const wide = allocateGaps([{ ...word, max: 900 }, mixed], 10)
    expect(narrow).toEqual(wide)
    if (!narrow.feasible) throw Error('mixed expansion is feasible')
    expect(narrow.gaps[0]!.width).toBeCloseTo(8.7578947368, 6)
    expect(narrow.gaps[1]!.width).toBeCloseTo(7.2421052632, 6)
    expect(narrow.residual).toBeCloseTo(0, 8)
  })
  it('rejectsInvalidPreferredBounds', () => {
    for (const preferredMax of [Number.NaN, Infinity, 9, 15]) {
      expect(allocateGaps([{ ...slot('bad'), preferredMax }], 1)).toEqual({ feasible: false, reason: 'invalid-input' })
    }
  })

  it('allocatesWithinLimitsAndRejectsImpossibleRequests', () => {
    expect(widths(6)).toEqual([13, 13]); expect(widths(-4)).toEqual([8, 8])
    expect(allocateGaps([slot('a'), slot('b')], 9)).toEqual({ feasible: false, reason: 'capacity-exceeded' })
    expect(allocateGaps([], Number.NaN)).toEqual({ feasible: false, reason: 'invalid-input' })
    expect(allocateGaps([], 0)).toMatchObject({ feasible: true, residual: 0, gaps: [] })
    expect(allocateGaps([], 1).feasible).toBe(false)
  })
  it('saturatesAndRedistributes', () => {
    const slots = [slot('a', 0, 0, 1), slot('b', 0, 0, 3)]
    const a = allocateGaps(slots, 3), b = allocateGaps(slots, 3.5)
    if (!a.feasible || !b.feasible) throw Error('infeasible')
    expect(a.gaps[0]!.width).toBeCloseTo(.3); expect(a.gaps[1]!.width).toBeCloseTo(2.7)
    expect(a.cost).toBeCloseTo(.9)
    expect(b.gaps.map(x => x.width)).toEqual([.5, 3])
  })
  it('preservesBoundsAndDeterminismForSeededCases', () => {
    let seed = 9
    const random = () => ((seed = Math.imul(seed, 1664525) + 1013904223 | 0) >>> 0) / 4294967296
    for (let i = 0; i < 1000; i++) {
      const slots = Array.from({ length: 5 }, (_, n) => slot(String(n), 10, random() * 10, 10 + random() * 10))
      const total = slots.reduce((n, s) => n + s.max - s.natural, 0)
      const delta = total * random()
      const r = allocateGaps(slots, delta)
      expect(r).toEqual(allocateGaps(slots, delta))
      if (!r.feasible) throw Error('feasible budget rejected')
      expect(r.gaps.reduce((n, g) => n + g.adjustment, 0)).toBeCloseTo(delta, 6)
      r.gaps.forEach((g, n) => { expect(g.width).toBeGreaterThanOrEqual(slots[n]!.min); expect(g.width).toBeLessThanOrEqual(slots[n]!.max) })
    }
  })
})
