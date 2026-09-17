import type { LayoutPlan } from './model'
import { breakParagraph } from './breaker'
import { buildParagraph, type ParagraphMeasurements } from './paragraph'

export function layoutParagraph(input: {
  text: string
  availableWidth: number
  maxBadness?: number
  measure: ParagraphMeasurements
}): LayoutPlan {
  if (!Number.isFinite(input.availableWidth) || input.availableWidth <= 0) {
    return {
      lines: [],
      fallback: true,
      fallbackReason: 'no-legal-break',
    }
  }

  const paragraph = buildParagraph({
    text: input.text,
    availableWidth: input.availableWidth,
    measure: input.measure,
  })
  const broken = breakParagraph({
    items: paragraph.items,
    breaks: paragraph.breaks,
    availableWidth: input.availableWidth,
    maxBadness: input.maxBadness,
  })

  return {
    lines: broken.lines,
    fallback: broken.fallback,
    fallbackReason: broken.fallbackReason,
  }
}
