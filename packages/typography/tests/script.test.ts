import { describe, expect, it } from 'vitest'
import { classifyCharacter, splitGraphemes } from '../src/script'

describe('script classification', () => {
  it('classifies CJK, Latin, digits, spaces, and punctuation', () => {
    expect(classifyCharacter('中')).toBe('cjk')
    expect(classifyCharacter('A')).toBe('latin')
    expect(classifyCharacter('7')).toBe('digit')
    expect(classifyCharacter(' ')).toBe('space')
    expect(classifyCharacter('，')).toBe('punctuation')
    expect(classifyCharacter('+')).toBe('symbol')
  })

  it('keeps surrogate pairs and combining marks intact', () => {
    expect(splitGraphemes('👍abc')).toEqual(['👍', 'a', 'b', 'c'])
    expect(splitGraphemes('é')).toEqual(['é'])
  })
})
