import { describe, expect, it } from 'vitest'
import { planMeasuredParagraph } from '../src/measured-plan'
import { candidateGapSlots } from '../src/spacing'
import { collectBreakOpportunities } from '../src/opportunities'
import type { ParagraphSnapshot } from '../src/layout-model'
const snapshot: ParagraphSnapshot = { id: 'p', revision: 1, environmentKey: 'env', text: '中文', width: 16,
  graphemeOffsets: [0, 1, 2], language: 'zh', direction: 'ltr', alignment: 'default',
  runs: [{ from: 0, to: 2, styleKey: 's', css: { 'font-size': '16px' } }] }
describe('measured paragraph planning', () => {
  it('alignsEnglishUsingReservedWordSpaceCapacityWithoutStretchingLetters', () => {
    const text = 'Alpha Bravo Delta Omega'
    const snap = { ...snapshot, text, width: 87,
      runs: [{ from: 0, to: text.length, styleKey: 's', css: { 'font-size': '16px' } }] }
    const spaces = new Map([...text.matchAll(/ /g)].map(m => [m.index, 4]))
    const ends = [0, ...collectBreakOpportunities(text).map(b => b.at)]
    const measurements = new Map<string, { key: string; naturalWidth: number }>()
    for (const from of ends) for (const to of ends) {
      if (to <= from) continue
      let contentTo = to
      while (text[contentTo - 1] === ' ') contentTo--
      const part = text.slice(from, contentTo), key = `${from}:${to}:${contentTo}`
      measurements.set(key, { key, naturalWidth: (part.match(/[A-Za-z]+/g)?.length ?? 0) * 40 + (part.match(/ /g)?.length ?? 0) * 4 })
    }
    const input = { snapshot: snap, slots: candidateGapSlots(snap, spaces), measurements, excludedEdges: new Set<string>(), mode: 'justified' as const }
    expect(planMeasuredParagraph({ ...input, slots: input.slots.map(s => ({ ...s, max: 5.6, preferredMax: undefined })) }))
      .toMatchObject({ status: 'fallback', reason: 'capacity-exceeded' })
    const result = planMeasuredParagraph(input)
    expect(result.status).toBe('planned')
    if (result.status !== 'planned') throw Error('word-space reserve must avoid fallback')
    expect(result.plan.lines.map(l => text.slice(l.from, l.contentTo))).toEqual(['Alpha Bravo', 'Delta Omega'])
    expect(result.plan.lines[0]!.gaps.map(g => g.width)).toEqual([7])
    expect(result.plan.lines[0]!.slots.every(s => s.kind === 'word-space')).toBe(true)
    expect(result.plan.lines[1]!.gaps.every(g => g.adjustment === 0)).toBe(true)
  })
  it('keepsEnglishJustifiedWhenAWordNeedsMoreThanDoubleSpaceWidth', () => {
    const text = 'Alpha Bravo Delta Omega'
    const snap = { ...snapshot, text, width: 95,
      runs: [{ from: 0, to: text.length, styleKey: 's', css: { 'font-size': '16px' } }] }
    const slots = candidateGapSlots(snap, new Map([[5, 4], [11, 4], [17, 4]]))
    // Each word is 40px. Two fit with a 15px space; three do not fit.
    const measurements = new Map([
      ['0:12:11', { key: '0:12:11', naturalWidth: 84 }],
      ['12:23:23', { key: '12:23:23', naturalWidth: 84 }],
    ])
    const result = planMeasuredParagraph({ snapshot: snap, slots, measurements,
      excludedEdges: new Set(), mode: 'justified' })
    expect(result.status).toBe('planned')
    if (result.status !== 'planned') throw Error('ordinary word spaces must absorb line slack')
    expect(result.plan.lines.map(l => text.slice(l.from, l.contentTo))).toEqual(['Alpha Bravo', 'Delta Omega'])
    expect(result.plan.lines[0]!.gaps).toMatchObject([{ width: 15, adjustment: 11 }])
    expect(result.plan.lines[1]!.gaps).toMatchObject([{ width: 4, adjustment: 0 }])
  })
  it('keepsOnlyAnExcessivelyLooseLineNaturalWhileItsNeighborStillJustifies', () => {
    const text = 'Alpha Bravo Delta Omega Echo'
    const snap = { ...snapshot, text, width: 110,
      runs: [{ from: 0, to: text.length, styleKey: 's', css: { 'font-size': '16px' } }] }
    const ends = [0, ...collectBreakOpportunities(text).map(b => b.at)]
    const measurements = new Map<string, { key: string; naturalWidth: number }>()
    for (const from of ends) for (const to of ends) {
      if (to <= from) continue
      let contentTo = to
      while (text[contentTo - 1] === ' ') contentTo--
      const key = `${from}:${to}:${contentTo}`
      measurements.set(key, { key, naturalWidth: 500 })
    }
    for (const [key, naturalWidth] of [['0:12:11', 84], ['12:24:23', 104], ['24:28:28', 32]] as const)
      measurements.set(key, { key, naturalWidth })
    const spaces = new Map([...text.matchAll(/ /g)].map(m => [m.index, 4]))
    const result = planMeasuredParagraph({ snapshot: snap, slots: candidateGapSlots(snap, spaces), measurements,
      excludedEdges: new Set(), mode: 'justified' })
    if (result.status !== 'planned') throw Error('a loose line must not discard the whole paragraph')
    expect(result.plan.lines.map(l => text.slice(l.from, l.contentTo))).toEqual(['Alpha Bravo', 'Delta Omega', 'Echo'])
    expect(result.plan.lines.map(l => l.breakReason)).toEqual(['spacing-limit', 'optimal', 'paragraph-end'])
    expect(result.plan.lines[0]!.gaps).toMatchObject([{ width: 4, adjustment: 0 }])
    expect(result.plan.lines[1]!.gaps).toMatchObject([{ width: 10, adjustment: 6 }])
  })
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

  it('borrowsBoundedCjkReserveBeforeAnUnbreakableLatinRun', () => {
    const text = '中'.repeat(28) + ' CJK/Latin 文'
    const snap = { ...snapshot, text, width: 498,
      runs: [{ from: 0, to: text.length, styleKey: 's', css: { 'font-size': '16px' } }] }
    const slots = candidateGapSlots(snap, new Map([[28, 4], [38, 4]]))
    const ends = [0, ...collectBreakOpportunities(text).map(b => b.at)]
    const measurements = new Map<string, { key: string; naturalWidth: number }>()
    for (const from of ends) for (const to of ends) {
      if (to <= from) continue
      let contentTo = to
      while (text[contentTo - 1] === ' ') contentTo--
      const part = text.slice(from, contentTo), key = `${from}:${to}:${contentTo}`
      measurements.set(key, { key, naturalWidth: [...part].reduce((w, c) => w + (c === '中' || c === '文' ? 16 : c === ' ' ? 4 : 8), 0) })
    }
    const result = planMeasuredParagraph({ snapshot: snap, slots, measurements, excludedEdges: new Set(), mode: 'justified' })
    expect(result.status).toBe('planned')
    if (result.status !== 'planned') throw Error('CJK reserve must avoid the paragraph fallback')
    expect(result.plan.lines.map(l => text.slice(l.from, l.contentTo))).toEqual(['中'.repeat(28), 'CJK/Latin 文'])
    expect(result.plan.lines[0]!.gaps.every(g => g.width > 1.28 && g.width <= 2.56)).toBe(true)
    expect(result.plan.lines[0]!.gaps.reduce((n, g) => n + g.adjustment, 0)).toBeCloseTo(50, 8)
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
