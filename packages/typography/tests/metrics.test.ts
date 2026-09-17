import { describe, expect, it } from 'vitest'
import { badness, demerits, lineAdjustment } from '../src/metrics'

describe('line metrics', () => {
  it('uses stretch when a line is loose', () => {
    expect(lineAdjustment({
      availableWidth: 104,
      naturalWidth: 100,
      stretchWidth: 8,
      shrinkWidth: 2,
    })).toEqual({ type: 'stretch', amount: 4 })
  })

  it('uses shrink when a line is tight', () => {
    expect(lineAdjustment({
      availableWidth: 98,
      naturalWidth: 100,
      stretchWidth: 8,
      shrinkWidth: 4,
    })).toEqual({ type: 'shrink', amount: 2 })
  })

  it('scores natural lines as zero and overflow as maximum badness', () => {
    expect(badness({
      availableWidth: 100,
      naturalWidth: 100,
      stretchWidth: 4,
      shrinkWidth: 2,
    })).toBe(0)
    expect(badness({
      availableWidth: 90,
      naturalWidth: 100,
      stretchWidth: 4,
      shrinkWidth: 2,
    })).toBe(10_000)
  })

  it('increases demerits for bad and flagged breaks', () => {
    const natural = demerits({ badness: 0, penalty: 0, flagged: false })
    const poor = demerits({ badness: 2_000, penalty: 0, flagged: false })
    const flagged = demerits({ badness: 0, penalty: 0, flagged: true })

    expect(poor).toBeGreaterThan(natural)
    expect(flagged).toBeGreaterThan(natural)
  })
})
