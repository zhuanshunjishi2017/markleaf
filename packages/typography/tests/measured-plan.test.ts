import { describe, expect, it } from 'vitest'
import { planMeasuredParagraph } from '../src/measured-plan'
import { candidateGapSlots } from '../src/spacing'
import { collectBreakOpportunities } from '../src/opportunities'
import type { ParagraphSnapshot } from '../src/layout-model'
const snapshot: ParagraphSnapshot = { id: 'p', revision: 1, environmentKey: 'env', text: '中文', width: 16,
  graphemeOffsets: [0, 1, 2], language: 'zh', direction: 'ltr', alignment: 'default',
  runs: [{ from: 0, to: 2, styleKey: 's', css: { 'font-size': '16px' } }] }
describe('measured paragraph planning', () => {
  it('alignsMixedTextUsingExpandedBoundariesWithoutSplittingEnglish', () => {
    const text = '中English文中English文'
    const snap = { ...snapshot, text, width: 92, cjkAutoSpacing: false,
      runs: [{ from: 0, to: text.length, styleKey: 's', css: { 'font-size': '16px' } }] }
    const ends = [0, ...collectBreakOpportunities(text).map(b => b.at)]
    const measurements = new Map<string, { key: string; naturalWidth: number }>()
    for (const from of ends) for (const to of ends) {
      if (to <= from) continue
      const part = text.slice(from, to), key = `${from}:${to}:${to}`
      measurements.set(key, { key, naturalWidth: [...part].filter(c => /\p{Script=Han}/u.test(c)).length * 16 + (part.match(/English/g)?.length ?? 0) * 50 })
    }
    const result = planMeasuredParagraph({ snapshot: snap, slots: candidateGapSlots(snap, new Map()), measurements, excludedEdges: new Set(), mode: 'justified' })
    expect(result.status).toBe('planned')
    if (result.status !== 'planned') throw Error('expanded mixed spacing must avoid whole-paragraph fallback')
    expect(result.plan.lines.map(l => text.slice(l.from, l.to))).toEqual(['中English文', '中English文'])
    const line = result.plan.lines[0]!
    expect(line.naturalWidth + line.gaps.reduce((n, g) => n + g.adjustment, 0)).toBeCloseTo(snap.width, 8)
    expect(result.plan.lines[1]!.gaps.every(g => g.adjustment === 0)).toBe(true)
  })

  it('rejectsEstimatedOnlyPlan', () => {
    const input = { snapshot, slots: [], measurements: new Map(), excludedEdges: new Set<string>(), mode: 'natural' as const }
    const first = planMeasuredParagraph(input)
    expect(first.status).toBe('needs-measurement')
    if (first.status !== 'needs-measurement') throw Error('missing requests')
    const measurements = new Map(first.requests.map(r => [r.key, { key: r.key, naturalWidth: 16 }]))
    const result = planMeasuredParagraph({ ...input, measurements })
    expect(result).toMatchObject({ status: 'planned', plan: { verified: true, revision: 1 } })
    const tooWide = new Map(first.requests.map(r => [r.key, { key: r.key, naturalWidth: 19 }]))
    tooWide.set('0:2:2', { key: '0:2:2', naturalWidth: 40 })
    expect(planMeasuredParagraph({ ...input, measurements: tooWide }).status).toBe('fallback')
  })
  it('measuresNarrowWordsBeforeRejectingTheirEstimates', () => {
    const narrow = { ...snapshot, text: 'iiii', width: 20, graphemeOffsets: [0, 1, 2, 3, 4] }
    const input = { snapshot: narrow, slots: [], measurements: new Map(), excludedEdges: new Set<string>(), mode: 'justified' as const }
    const result = planMeasuredParagraph(input)
    expect(result).toMatchObject({ status: 'needs-measurement', requests: [{ from: 0, to: 4, contentTo: 4 }] })
    expect(planMeasuredParagraph({ ...input, measurements: new Map([['0:4:4', { key: '0:4:4', naturalWidth: 14 }]]) })).toMatchObject({ status: 'planned', plan: { verified: true } })
  })
  it('keepsFinalLinesNaturalAndEmptyParagraphsEmpty', () => {
    const result = planMeasuredParagraph({ snapshot: { ...snapshot, text: '', graphemeOffsets: [0] }, slots: [], measurements: new Map(), excludedEdges: new Set(), mode: 'justified' })
    expect(result).toMatchObject({ status: 'planned', plan: { lines: [], verified: true } })
  })
})
