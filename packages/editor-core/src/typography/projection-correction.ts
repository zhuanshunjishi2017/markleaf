import type { MeasuredLine } from '@markleaf/typography'
import { allocateGaps } from '@markleaf/typography'
import type { EditorView } from '@tiptap/pm/view'
import type { ProjectionSnapshot } from './projection-snapshot'

export function correctTypographyLine(line: MeasuredLine, measuredWidth: number):
  { status: 'accepted' } | { status: 'corrected'; gaps: MeasuredLine['gaps'] } | { status: 'replan' } {
  if (!Number.isFinite(measuredWidth) || measuredWidth <= 0) return { status: 'replan' }
  if (line.breakReason === 'paragraph-end') return measuredWidth <= line.targetWidth + .5 ? { status: 'accepted' } : { status: 'replan' }
  const delta = line.targetWidth - measuredWidth
  if (Math.abs(delta) <= .5) return { status: 'accepted' }
  if (Math.abs(delta) > 1) return { status: 'replan' }
  const allocation = allocateGaps(line.slots, line.gaps.reduce((n, g) => n + g.adjustment, 0) + delta)
  return allocation.feasible ? { status: 'corrected', gaps: allocation.gaps } : { status: 'replan' }
}

export function measureProjectedLine(view: EditorView, line: MeasuredLine, snapshot: ProjectionSnapshot): number {
  const range = view.dom.ownerDocument.createRange()
  const start = view.domAtPos(snapshot.segmentFrom + line.from, 1)
  const end = view.domAtPos(snapshot.segmentFrom + line.contentTo, -1)
  range.setStart(start.node, start.offset); range.setEnd(end.node, end.offset)
  const rects = [...range.getClientRects()].filter(r => r.width > 0)
  if (!rects.length) return Number.NaN
  // Union all inline fragments; text Range rectangles use advances, not glyph ink bounds.
  const top = Math.min(...rects.map(r => r.top)), bottom = Math.max(...rects.map(r => r.bottom))
  if (bottom - top > Math.max(...rects.map(r => r.height)) * 1.9) return Number.NaN
  return Math.max(...rects.map(r => r.right)) - Math.min(...rects.map(r => r.left))
}

export type TypographySpacingMode = 'word' | 'character'

export type TypographySpacingCorrection = {
  mode: TypographySpacingMode
  spacing: number
  delta: number
}

export function calculateTypographySpacingCorrection(input: {
  mode: TypographySpacingMode
  currentSpacing: number
  currentWidth: number
  targetWidth: number
  gapCount: number
  threshold?: number
}): TypographySpacingCorrection | null {
  const { mode, currentSpacing, currentWidth, targetWidth, gapCount } = input
  const threshold = input.threshold ?? 0.25
  if (![currentSpacing, currentWidth, targetWidth].every(Number.isFinite)) return null
  if (targetWidth <= 0 || currentWidth <= 0 || gapCount <= 0) return null

  const delta = targetWidth - currentWidth
  if (Math.abs(delta) <= threshold) return null
  const spacing = currentSpacing + delta / gapCount
  if (!Number.isFinite(spacing)) return null
  return { mode, spacing, delta }
}
