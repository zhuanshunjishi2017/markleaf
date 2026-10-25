import { describe, expect, it } from 'vitest'
import { collectBreakOpportunities, graphemeOffsets, supportsTypographyText } from '../src/opportunities'

describe('UTF-16 break opportunities', () => {
  it('keepsStraightDoubleQuotesAttachedToTheirEnglishContent', () => {
    for (const text of ['"global optimal line breaking", next', 'say ("alpha beta") then "gamma delta".', 'a 6" mark']) {
      const ends = collectBreakOpportunities(text).map(b => b.at)
      for (const match of text.matchAll(/"/g)) {
        const at = match.index
        const opens = at === 0 || /[\s([{]/.test(text[at - 1]!)
        expect(ends).not.toContain(opens ? at + 1 : at)
      }
    }
    expect(collectBreakOpportunities('"alpha beta" next').map(b => b.at)).toContain(7)
  })
  it('allowsExistingHyphensInOrdinaryCompoundsWhileProtectingResourceTokens', () => {
    const ends = collectBreakOpportunities('well-known text')
    expect(ends.map(b => b.at)).toEqual([5, 11, 15])
    expect(ends[0]!.preferenceCost).toBeGreaterThan(ends[1]!.preferenceCost)
    for (const text of ['session_guard.py', 'draft-final.md', 'https://example-site.com/a-b', 'path/to/some-file', '2026-10-25', '12-34']) {
      expect(collectBreakOpportunities(text).map(b => b.at)).toEqual([text.length])
    }
    expect(collectBreakOpportunities('abc\u2011def').map(b => b.at)).toEqual([7])
  })
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
