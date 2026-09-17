import type {
  BreakCandidate,
  LayoutItem,
  TypographyFallbackReason,
  TypographyLine,
} from './model'
import { badness, demerits, lineAdjustment } from './metrics'

type Path = {
  breakIndex: number
  sourceTo: number
  totalDemerits: number
  previous: Path | null
  penalty: number
  flagged: boolean
}

function lineMetrics(items: LayoutItem[], fromIndex: number, toIndex: number) {
  let naturalWidth = 0
  let stretchWidth = 0
  let shrinkWidth = 0

  for (let index = fromIndex; index <= toIndex; index += 1) {
    const item = items[index]
    if (!item || item.kind === 'penalty') continue
    naturalWidth += item.width
    if (item.kind === 'glue') {
      stretchWidth += item.stretch
      shrinkWidth += item.shrink
    }
  }

  return { naturalWidth, stretchWidth, shrinkWidth }
}

function textEndPosition(items: LayoutItem[]): number {
  return items.at(-1)?.to ?? 0
}

function lastContentIndex(items: LayoutItem[]): number {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (items[index]!.kind !== 'glue') return index
  }
  return -1
}

function makeLine(
  items: LayoutItem[],
  fromIndex: number,
  toIndex: number,
  from: number,
  to: number,
  availableWidth: number,
  penalty: number,
  flagged: boolean,
  reason: TypographyLine['breakReason'],
) {
  const metrics = lineMetrics(items, fromIndex, toIndex)
  const adjustment = lineAdjustment({
    availableWidth,
    ...metrics,
  })
  const lineBadness =
    reason === 'paragraph-end' && metrics.naturalWidth <= availableWidth
      ? 0
      : badness({
          availableWidth,
          ...metrics,
        })

  return {
    line: {
      from,
      to,
      naturalWidth: metrics.naturalWidth,
      adjustedWidth: metrics.naturalWidth
        + (adjustment.type === 'stretch' || adjustment.type === 'shrink'
          ? adjustment.amount
          : 0),
      badness: lineBadness,
      breakReason: reason,
    } satisfies TypographyLine,
    demerits: demerits({ badness: lineBadness, penalty, flagged }),
  }
}

function pathLines(
  path: Path,
  items: LayoutItem[],
  availableWidth: number,
): TypographyLine[] {
  const lines: TypographyLine[] = []
  let cursor: Path = path

  while (cursor.previous) {
    const previous = cursor.previous
    const line = makeLine(
      items,
      previous.breakIndex + 1,
      cursor.breakIndex,
      previous.sourceTo,
      cursor.sourceTo,
      availableWidth,
      cursor.penalty,
      cursor.flagged && previous.flagged,
      'optimal',
    )
    lines.unshift(line.line)
    cursor = previous
  }

  return lines
}

export function breakParagraph(input: {
  items: LayoutItem[]
  breaks: BreakCandidate[]
  availableWidth: number
  maxBadness?: number
}): {
  lines: TypographyLine[]
  fallback: boolean
  fallbackReason?: TypographyFallbackReason
} {
  const maxBadness = input.maxBadness ?? 8_000
  if (input.items.length === 0) {
    return {
      lines: [],
      fallback: false,
    }
  }

  const paths: Path[] = [{
    breakIndex: -1,
    sourceTo: 0,
    totalDemerits: 0,
    previous: null,
    penalty: 0,
    flagged: false,
  }]

  for (const candidate of input.breaks) {
    const item = input.items[candidate.itemIndex]
    if (!item) continue

    let best: { path: Path; cost: number } | null = null
    for (const previous of paths) {
      const fromIndex = previous.breakIndex + 1
      if (fromIndex > candidate.itemIndex) continue

      const line = makeLine(
        input.items,
        fromIndex,
        candidate.itemIndex,
        previous.sourceTo,
        candidate.sourceTo,
        input.availableWidth,
        candidate.penalty,
        candidate.flagged && previous.flagged,
        'optimal',
      )
      if (line.line.badness > maxBadness) continue

      const cost = previous.totalDemerits + line.demerits
      if (!best || cost < best.cost) best = { path: previous, cost }
    }

    if (best) {
      paths.push({
        breakIndex: candidate.itemIndex,
        sourceTo: candidate.sourceTo,
        totalDemerits: best.cost,
        previous: best.path,
        penalty: candidate.penalty,
        flagged: candidate.flagged,
      })
    }
  }

  const contentEnd = lastContentIndex(input.items)
  const textEnd = textEndPosition(input.items)
  let bestFinal: {
    path: Path
    lines: TypographyLine[]
    cost: number
  } | null = null

  for (const path of paths) {
    if (path.breakIndex === contentEnd) {
      const lines = pathLines(path, input.items, input.availableWidth)
      const lastLine = lines.at(-1)
      if (!lastLine) continue
      lastLine.breakReason = 'paragraph-end'
      const finalLine = makeLine(
        input.items,
        path.breakIndex + 1,
        contentEnd,
        path.sourceTo,
        textEnd,
        input.availableWidth,
        -10_000,
        false,
        'paragraph-end',
      )
      if (finalLine.line.badness <= maxBadness && finalLine.line.to > finalLine.line.from) {
        lines.push(finalLine.line)
      }

      if (!bestFinal || path.totalDemerits < bestFinal.cost) {
        bestFinal = { path, lines, cost: path.totalDemerits }
      }
      continue
    }

    const fromIndex = path.breakIndex + 1
    if (fromIndex > contentEnd) continue
    const finalLine = makeLine(
      input.items,
      fromIndex,
      contentEnd,
      path.sourceTo,
      textEnd,
      input.availableWidth,
      -10_000,
      false,
      'paragraph-end',
    )
    if (finalLine.line.badness > maxBadness) continue

    const cost = path.totalDemerits + finalLine.demerits
    if (!bestFinal || cost < bestFinal.cost) {
      bestFinal = {
        path,
        lines: [...pathLines(path, input.items, input.availableWidth), finalLine.line],
        cost,
      }
    }
  }

  if (!bestFinal) {
    return {
      lines: [],
      fallback: true,
      fallbackReason: input.breaks.length === 0
        ? 'no-legal-break'
        : 'badness-exceeded',
    }
  }

  return {
    lines: bestFinal.lines,
    fallback: false,
  }
}
