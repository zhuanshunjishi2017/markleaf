import { describe, expect, it } from 'vitest'
import { breakParagraph } from '../src/breaker'
import { buildParagraph } from '../src/paragraph'

function build(text: string, width: number) {
  return buildParagraph({
    text,
    availableWidth: width,
    measure: (value) => ({
      width: value.length * 10,
      stretch: value.trim() === '' ? 4 : 0,
      shrink: value.trim() === '' ? 2 : 0,
    }),
  })
}

describe('Knuth–Plass breaker', () => {
  it('fits a paragraph on one optimal line', () => {
    const paragraph = build('中文 English', 1000)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 1000,
    })

    expect(result.fallback).toBe(false)
    expect(result.lines).toHaveLength(1)
    expect(result.lines[0]).toMatchObject({
      from: 0,
      to: 10,
      badness: 0,
      breakReason: 'paragraph-end',
    })
  })

  it('does not create an empty final line after a CJK break candidate', () => {
    const paragraph = build('中文', 1000)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 1000,
    })

    expect(result.fallback).toBe(false)
    expect(result.lines).toHaveLength(1)
    expect(result.lines[0]).toMatchObject({
      from: 0,
      to: 2,
      breakReason: 'paragraph-end',
    })
  })

  it('chooses global breaks for a narrow paragraph', () => {
    const paragraph = build('中文 English 中文 English', 120)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 120,
    })

    expect(result.fallback).toBe(false)
    expect(result.lines.length).toBeGreaterThan(1)
    expect(result.lines.every((line) => line.naturalWidth <= 120)).toBe(true)
    expect(result.lines.every((line) => Number.isFinite(line.badness))).toBe(true)
    expect(result.lines.every((line) => line.badness >= 0)).toBe(true)
  })

  it('falls back when no legal break exists', () => {
    const paragraph = build('supercalifragilistic', 20)
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 20,
    })

    expect(result).toEqual({
      lines: [],
      fallback: true,
      fallbackReason: 'no-legal-break',
    })
  })
})
