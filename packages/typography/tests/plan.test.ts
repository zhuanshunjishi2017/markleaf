import { describe, expect, it } from 'vitest'
import { layoutParagraph } from '../src/plan'

const measure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('layout plan', () => {
  it('produces a successful deterministic plan', () => {
    const first = layoutParagraph({
      text: '中文 English 中文',
      availableWidth: 1000,
      measure,
    })
    const second = layoutParagraph({
      text: '中文 English 中文',
      availableWidth: 1000,
      measure,
    })

    expect(first.fallback).toBe(false)
    expect(first).toEqual(second)
    expect(first.lines).toHaveLength(1)
    expect(first.lines[0]).toMatchObject({ from: 0, to: 13 })
  })

  it('produces a deterministic fallback plan', () => {
    const plan = layoutParagraph({
      text: 'supercalifragilistic',
      availableWidth: 20,
      measure,
    })

    expect(plan).toEqual({
      lines: [],
      fallback: true,
      fallbackReason: 'no-legal-break',
    })
  })
})
