import { describe, expect, it } from 'vitest'
import { allocateGaps, buildGapSlots, candidateGapSlots } from '../src/spacing'
import { collectBreakOpportunities } from '../src/opportunities'
import type { ParagraphSnapshot } from '../src/layout-model'
const s = (text: string): ParagraphSnapshot => ({ id: 'p', revision: 0, environmentKey: text, text, width: 100,
  language: 'zh', direction: 'ltr', alignment: 'default', graphemeOffsets: [],
  runs: [{ from: 0, to: text.length, styleKey: 's', css: { 'font-size': '16px' } }] })
const slots = (text: string) => { const snap = s(text), widths = new Map([[1, 4]])
  return buildGapSlots(snap, new Set(candidateGapSlots(snap, widths).map(x => x.id)), widths) }
describe('mixed spacing rules', () => {
  it('respectsDisabledAutomaticSpacingWithoutRemovingJustificationCapacity', () => {
    const snap = { ...s('中文English中文'), cjkAutoSpacing: false }
    const edges = candidateGapSlots(snap, new Map())
    expect(edges.filter(x => x.kind === 'mixed').map(x => [x.natural, x.min, x.max])).toEqual([[0, 0, 9.6], [0, 0, 9.6]])
    expect(edges.filter(x => x.kind === 'cjk').every(x => x.natural === 0 && x.preferredMax === 1.28 && x.max === 2.56)).toBe(true)
    expect(candidateGapSlots({ ...snap, cjkAutoSpacing: true }, new Map()).filter(x => x.kind === 'mixed').every(x => x.natural === 2 && x.min === 1.28)).toBe(true)
  })
  it('buildsExactBudgets', () => {
    expect(slots('中A')[0]).toMatchObject({ kind: 'mixed', natural: 2, min: 1.28, max: 9.6, preferredMax: 3.2, weight: .5, edge: 'end' })
    expect(slots('中文')[0]).toMatchObject({ natural: 0, min: 0, preferredMax: 1.28, max: 2.56, weight: 1 })
    expect(slots('A B')[0]).toMatchObject({ natural: 4, min: 3.2, preferredMax: 5.6, max: 100, edge: 'space' })
  })
  it('reservesExpandedWordSpacesForOtherwiseUnfillableLines', () => {
    const current = slots('A B'), ordinary = current.map(s => ({ ...s, max: 5.6, preferredMax: undefined }))
    expect(allocateGaps(current, 1)).toEqual(allocateGaps(ordinary, 1))
    expect(allocateGaps(current, -.5)).toEqual(allocateGaps(ordinary, -.5))
    const expanded = allocateGaps(current, 3)
    expect(expanded).toMatchObject({ feasible: true, gaps: [{ width: 7 }], residual: 0 })
    expect(allocateGaps(current, 96.1)).toEqual({ feasible: false, reason: 'capacity-exceeded' })
  })
  it('penalizesActualWordSpaceExpansionIndependentlyOfAvailableMeasure', () => {
    const snap = s('A B'), natural = new Map([[1, 4]])
    const small = candidateGapSlots({ ...snap, width: 40 }, natural)
    const large = candidateGapSlots({ ...snap, width: 400 }, natural)
    const a = allocateGaps(small, 11), b = allocateGaps(large, 11)
    expect(a).toMatchObject({ feasible: true, gaps: [{ width: 15 }], residual: 0 })
    expect(b).toEqual(a)
    const looser = allocateGaps(small, 19)
    expect(looser.feasible).toBe(true)
    if (a.feasible && looser.feasible) expect(looser.cost).toBeGreaterThan(a.cost)
  })
  it('neverDuplicatesExplicitOrPunctuationGaps', () => {
    expect(slots('中 A').map(x => x.kind)).toEqual(['word-space'])
    expect(slots('English，中文').map(x => x.kind)).toEqual(['cjk'])
    expect(slots('A  B')).toEqual([])
    expect(slots('A\u00a0B A\u2060B 123 word')).toEqual([])
    expect(buildGapSlots(s('中A'), new Set(), new Map())).toEqual([])
  })
  it('protectsPunctuationAndLatinTokens', () => {
    for (const [open, close] of Array.from('（〔［｛《〈「『【〖〘〚“‘').map((x, i) => [x, Array.from('）〕］｝》〉」』】〗〙〛”’')[i]!])) {
      expect(collectBreakOpportunities(`${open}中文${close}`).map(x => x.at)).not.toContain(1)
      expect(collectBreakOpportunities(`${open}中文${close}`).map(x => x.at)).not.toContain(3)
    }
    expect(collectBreakOpportunities('…………').map(x => x.at)).toEqual([2, 4])
    expect(collectBreakOpportunities('————').map(x => x.at)).toEqual([2, 4])
    expect(collectBreakOpportunities("can't session_guard.py 12.34").map(x => x.at)).toEqual([6, 23, 28])
  })
})
