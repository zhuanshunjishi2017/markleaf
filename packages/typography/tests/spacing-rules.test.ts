import { describe, expect, it } from 'vitest'
import { buildGapSlots, candidateGapSlots } from '../src/spacing'
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
    expect(edges.filter(x => x.kind === 'cjk').every(x => x.natural === 0 && x.max === 1.28)).toBe(true)
    expect(candidateGapSlots({ ...snap, cjkAutoSpacing: true }, new Map()).filter(x => x.kind === 'mixed').every(x => x.natural === 2 && x.min === 1.28)).toBe(true)
  })
  it('buildsExactBudgets', () => {
    expect(slots('中A')[0]).toMatchObject({ kind: 'mixed', natural: 2, min: 1.28, max: 9.6, preferredMax: 3.2, weight: .5, edge: 'end' })
    expect(slots('中文')[0]).toMatchObject({ natural: 0, min: 0, max: 1.28, weight: 1 })
    expect(slots('A B')[0]).toMatchObject({ natural: 4, min: 3.2, max: 5.6, edge: 'space' })
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
