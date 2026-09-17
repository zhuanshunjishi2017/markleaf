import { describe, expect, it } from 'vitest'
import {
  canBreakAfter,
  canBreakBefore,
  canHang,
  compressionMode,
} from '../src/punctuation'

describe('CJK punctuation', () => {
  it('forbids closing punctuation at line start', () => {
    expect(canBreakBefore('，')).toBe(false)
    expect(canBreakBefore('。')).toBe(false)
    expect(canBreakBefore('）')).toBe(false)
    expect(canBreakBefore('中')).toBe(true)
  })

  it('forbids opening punctuation at line end', () => {
    expect(canBreakAfter('（')).toBe(false)
    expect(canBreakAfter('“')).toBe(false)
    expect(canBreakAfter('中')).toBe(true)
  })

  it('classifies compression and hanging', () => {
    expect(compressionMode('，')).toBe('half')
    expect(compressionMode('。')).toBe('terminal')
    expect(canHang('，')).toBe(true)
    expect(canHang('。')).toBe(true)
    expect(canHang('中')).toBe(false)
  })
})
