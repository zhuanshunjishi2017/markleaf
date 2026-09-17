import { describe, expect, it } from 'vitest'
import { breakParagraph } from '../src/breaker'
import { buildParagraph } from '../src/paragraph'
import { layoutParagraph } from '../src/plan'

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

const measure = (value: string) => ({
  width: value.length * 10,
  stretch: value.trim() === '' ? 4 : 0,
  shrink: value.trim() === '' ? 2 : 0,
})

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

    const adjustedLines = result.lines.slice(0, -1)
    expect(adjustedLines.length).toBeGreaterThan(0)
    expect(adjustedLines.every((line) => line.adjustment.type !== 'overflow')).toBe(true)
    expect(adjustedLines.every((line) => line.naturalWidth + line.adjustment.amount === line.adjustedWidth)).toBe(true)
    expect(adjustedLines.every((line) => Number.isInteger(line.spaceCount))).toBe(true)
    expect(adjustedLines.every((line) => line.contentTo <= line.to)).toBe(true)
    expect(adjustedLines.every((line) => {
      const visibleText = '中文 English 中文 English'.slice(line.from, line.contentTo)
      const visibleSpaces = Array.from(visibleText).filter((character) => character.trim() === '').length
      return visibleSpaces === line.spaceCount
    })).toBe(true)
    expect(result.lines.at(-1)!.adjustment.type).toBe('natural')
    expect(result.lines.at(-1)!.adjustment.amount).toBe(0)
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

it('does not split emoji', () => {
  const paragraph = buildParagraph({
    text: '中文👍English',
    availableWidth: 100,
    measure: (value) => ({ width: value.length * 12, stretch: 0, shrink: 0 }),
  })
  const result = breakParagraph({
    items: paragraph.items,
    breaks: paragraph.breaks,
    availableWidth: 100,
  })

  const boxText = paragraph.items
    .filter((item) => item.kind === 'box')
    .map((item) => item.kind === 'box' ? item.text : '')
    .join('')

  expect(boxText).toContain('👍')
  expect(result.fallback).toBe(false)
})

it('returns identical plans for repeated input', () => {
  const first = layoutParagraph({
    text: '中文 English 中文 English',
    availableWidth: 140,
    measure,
  })
  const second = layoutParagraph({
    text: '中文 English 中文 English',
    availableWidth: 140,
    measure,
  })

  expect(first).toEqual(second)
})
