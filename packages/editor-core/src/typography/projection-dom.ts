import type { GapAllocation, GapSlot, LineRequest, ParagraphSnapshot } from '@markleaf/typography'
import { candidateGapSlots, slotsForLine } from '@markleaf/typography'

export function getSupportedTypographyEdges(snapshot: ParagraphSnapshot): ReadonlySet<string> {
  const spaces = new Map<number, number>()
  for (let i = 0; i < snapshot.text.length; i++) if (snapshot.text[i] === ' ') spaces.set(i, 1)
  return new Set(candidateGapSlots(snapshot, spaces).map(s => s.id))
}

export type TypographyInlinePiece = {
  from: number; to: number; css: Readonly<Record<string, string>>; spacing: Record<string, string>; ids: string[]
}

/** A single ownership/fragmentation recipe for offscreen measurement and PM decorations. */
export function typographyInlinePieces(snapshot: ParagraphSnapshot, line: LineRequest,
  slots: readonly GapSlot[], allocations: readonly GapAllocation[]): TypographyInlinePiece[] {
  const widths = new Map(allocations.map(a => [a.slotId, a.width]))
  const local = slotsForLine(slots, line)
  const boundaries = new Set([line.from, line.contentTo])
  snapshot.runs.forEach(r => { if (r.from > line.from && r.from < line.contentTo) boundaries.add(r.from); if (r.to > line.from && r.to < line.contentTo) boundaries.add(r.to) })
  local.forEach(s => { boundaries.add(s.owner.from); boundaries.add(s.owner.to) })
  const edges = [...boundaries].sort((a, b) => a - b)
  return edges.slice(0, -1).map((from, i) => {
    const to = edges[i + 1]!
    const css = snapshot.runs.find(r => r.from <= from && r.to > from)?.css ?? {}
    const spacing: Record<string, string> = {}, ids: string[] = []
    for (const slot of local) {
      if (slot.owner.from !== from || slot.owner.to !== to) continue
      const width = widths.get(slot.id) ?? slot.natural
      ids.push(slot.id)
      const property = slot.edge === 'space' ? 'word-spacing' : `padding-inline-${slot.edge}`
      const base = slot.edge === 'space' ? parseFloat(css['word-spacing'] ?? '') || 0 : 0
      spacing[property] = `${base + (slot.edge === 'space' ? width - slot.natural : width)}px`
    }
    return { from, to, css, spacing, ids }
  })
}

export function buildTypographyInlineDom(doc: Document, snapshot: ParagraphSnapshot, line: LineRequest,
  slots: readonly GapSlot[], allocations: readonly GapAllocation[]): DocumentFragment {
  const fragment = doc.createDocumentFragment()
  const templates = new Map<Readonly<Record<string, string>>, HTMLSpanElement>()
  for (const piece of typographyInlinePieces(snapshot, line, slots, allocations)) {
    let template = templates.get(piece.css)
    if (!template) {
      template = doc.createElement('span')
      for (const [key, value] of Object.entries(piece.css)) template.style.setProperty(key, value)
      template.style.setProperty('text-autospace', 'no-autospace')
      templates.set(piece.css, template)
    }
    const span = template.cloneNode(false) as HTMLSpanElement
    for (const [key, value] of Object.entries(piece.spacing)) span.style.setProperty(key, value)
    span.textContent = snapshot.text.slice(piece.from, piece.to)
    fragment.append(span)
  }
  return fragment
}
