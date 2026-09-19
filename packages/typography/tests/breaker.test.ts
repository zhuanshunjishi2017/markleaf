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
      stretch: value.trim() === '' ? 40 : value.length * 10,
      shrink: value.trim() === '' ? 2 : 0,
    }),
  })
}

const measure = (value: string) => ({
  width: value.length * 10,
  stretch: value.trim() === '' ? 40 : value.length * 10,
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

  it('does not strand line-start-forbidden punctuation after a space break', () => {
    const text = '中文 中文 。'
    const paragraph = buildParagraph({
      text,
      availableWidth: 50,
      measure: value => ({
        width: value.length * 10,
        stretch: value.trim() === '' ? 40 : value.length * 20,
        shrink: value.trim() === '' ? 2 : 0,
      }),
    })
    const result = breakParagraph({
      items: paragraph.items,
      breaks: paragraph.breaks,
      availableWidth: 50,
    })

    expect(result.fallback).toBe(false)
    expect(result.lines.slice(1).every(line => !text.slice(line.from, line.to).trimStart().startsWith('。'))).toBe(true)
    expect(result.lines.slice(1).every(line => !/^\s/u.test(text.slice(line.from, line.to)))).toBe(true)
  })
})

it('does not split emoji', () => {
  const paragraph = buildParagraph({
    text: '中文👍English',
    availableWidth: 140,
    measure: (value) => ({ width: value.length * 12, stretch: 0, shrink: 0 }),
  })
  const result = breakParagraph({
    items: paragraph.items,
    breaks: paragraph.breaks,
    availableWidth: 140,
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

it('uses CJK box stretch when scoring pure CJK lines', () => {
  const text = '中文中文中文中文中文中文中文中文中文中文中文中文中文中文中文中文中文中文中文中文'
  const result = layoutParagraph({
    text,
    availableWidth: 105,
    measure: value => ({
      width: value.length * 10,
      stretch: value.length * 2,
      shrink: value.length,
    }),
  })

  expect(result.fallback).toBe(false)
  const first = result.lines[0]!
  expect(first.from).toBe(0)
  expect(first.to).toBe(10)
  expect(first.adjustment.type).toBe('stretch')
  expect(first.adjustment.amount).toBeLessThanOrEqual(first.naturalWidth * 0.25)
})

it('does not break inside dictionary-segmented CJK words', () => {
  const text = '中文中文English中文中文中文English中文中文中文中文'
  const result = layoutParagraph({
    text,
    availableWidth: 240,
    measure: value => ({
      width: value.length * 10,
      stretch: value.length * 2,
      shrink: value.length,
    }),
  })
  const boundaries = new Set<number>([0, text.length])
  for (const segment of new Intl.Segmenter(undefined, { granularity: 'word' }).segment(text)) {
    boundaries.add(segment.index + segment.segment.length)
  }

  expect(result.fallback).toBe(false)
  for (const line of result.lines.slice(0, -1)) {
    expect(boundaries.has(line.to)).toBe(true)
  }
})

it('applies max badness to underfull intermediate lines', () => {
  const paragraph = buildParagraph({
    text: 'AA BB CC DD',
    availableWidth: 45,
    measure: value => ({
      width: value.length * 10,
      stretch: value.trim() === '' ? 1 : 0,
      shrink: value.trim() === '' ? 1 : 0,
    }),
  })
  const result = breakParagraph({
    items: paragraph.items,
    breaks: paragraph.breaks,
    availableWidth: 45,
    maxBadness: 0,
  })

  expect(result).toEqual({
    lines: [],
    fallback: true,
    fallbackReason: 'badness-exceeded',
  })
})
