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
  const owners = new Map<number, GapSlot[]>()
  local.forEach(s => {
    boundaries.add(s.owner.from); boundaries.add(s.owner.to)
    const at = owners.get(s.owner.from)
    if (at) at.push(s)
    else owners.set(s.owner.from, [s])
  })
  const edges = [...boundaries].sort((a, b) => a - b)
  return edges.slice(0, -1).map((from, i) => {
    const to = edges[i + 1]!
    const css = snapshot.runs.find(r => r.from <= from && r.to > from)?.css ?? {}
    const spacing: Record<string, string> = {}, ids: string[] = []
    for (const slot of owners.get(from) ?? []) {
      if (slot.owner.to !== to) continue
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
  let css: Readonly<Record<string, string>> | undefined, run: HTMLSpanElement | undefined
  for (const piece of typographyInlinePieces(snapshot, line, slots, allocations)) {
    // Computed font properties are inherited. Apply them once per contiguous
    // style run rather than cloning a full CSS declaration for every CJK gap.
    if (piece.css !== css) {
      css = piece.css; run = doc.createElement('span')
      for (const [key, value] of Object.entries(css)) run.style.setProperty(key, value)
      run.style.setProperty('text-autospace', 'no-autospace')
      fragment.append(run)
    }
    if (Object.keys(piece.spacing).length) {
      const span = doc.createElement('span')
      for (const [key, value] of Object.entries(piece.spacing)) span.style.setProperty(key, value)
      span.textContent = snapshot.text.slice(piece.from, piece.to)
      run!.append(span)
    } else run!.append(doc.createTextNode(snapshot.text.slice(piece.from, piece.to)))
  }
  return fragment
}
