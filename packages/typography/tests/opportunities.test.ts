import { describe, expect, it } from 'vitest'
import { collectBreakOpportunities, graphemeOffsets, supportsTypographyText } from '../src/opportunities'

describe('UTF-16 break opportunities', () => {
  it('usesUtf16GraphemeBoundaries', () => {
    const text = 'A𠀀e\u0301👨‍👩‍👧‍👦B'
    expect(graphemeOffsets(text)).toEqual([0, 1, 3, 5, 16, 17])
    expect(collectBreakOpportunities(text).every(b => [1, 3, 5, 16, 17].includes(b.at))).toBe(true)
  })
  it('preservesProtectedWhitespace', () => {
    expect(collectBreakOpportunities('A\u00a0B')).toEqual([{ at: 3, kind: 'forced', preferenceCost: 0 }])
    expect(collectBreakOpportunities('A\u2060B')).toEqual([{ at: 3, kind: 'forced', preferenceCost: 0 }])
    expect(collectBreakOpportunities('A  B').map(b => b.at)).toEqual([3, 4])
    expect(supportsTypographyText('A\tB')).toBe(false)
    expect(supportsTypographyText('A\u00adB')).toBe(false)
    expect(supportsTypographyText('العربية')).toBe(false)
    expect(supportsTypographyText('中文 A𠀀e\u0301👨‍👩‍👧‍👦')).toBe(true)
  })
})
