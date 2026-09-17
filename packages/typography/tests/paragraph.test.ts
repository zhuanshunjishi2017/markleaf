import { describe, expect, it } from 'vitest'
import { buildParagraph } from '../src/paragraph'

const measure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('paragraph construction', () => {
  it('creates boxes, word glue, and legal CJK breaks', () => {
    const paragraph = buildParagraph({
      text: '中文 English 中文',
      availableWidth: 1000,
      measure,
    })

    expect(paragraph.items.some((item) => item.kind === 'glue')).toBe(true)
    expect(paragraph.breaks.length).toBeGreaterThan(3)
  })

  it('creates zero-width glue between directly adjacent CJK and Latin', () => {
    const paragraph = buildParagraph({
      text: '中文English',
      availableWidth: 1000,
      measure,
    })

    expect(paragraph.items.some((item) => (
      item.kind === 'glue' && item.text === ''
    ))).toBe(true)
  })

  it('creates a legal break between directly adjacent Latin and CJK', () => {
    const paragraph = buildParagraph({
      text: 'English中文',
      availableWidth: 1000,
      measure,
    })

    expect(paragraph.breaks.some((candidate) => candidate.sourceTo === 7)).toBe(true)
  })

  it('does not break before closing punctuation', () => {
    const paragraph = buildParagraph({
      text: '中文，',
      availableWidth: 1000,
      measure,
    })

    expect(paragraph.breaks.some((candidate) => candidate.sourceTo === 2)).toBe(false)
  })

  it('reports an oversized atom as no-legal-break input', () => {
    const paragraph = buildParagraph({
      text: 'supercalifragilistic',
      availableWidth: 20,
      measure: (text) => ({ width: text.length * 10, stretch: 0, shrink: 0 }),
    })

    expect(paragraph.breaks).toEqual([])
  })
})
