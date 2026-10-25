import type { Node as PMNode } from '@tiptap/pm/model'
import { Decoration, DecorationSet } from '@tiptap/pm/view'
import type { MeasuredPlan } from '@markleaf/typography'
import type { SnapshotResult } from './projection-snapshot'
import { typographyInlinePieces } from './projection-dom'

export function projectedPlanDecorations(doc: PMNode, snapshots: readonly SnapshotResult[], plans: readonly MeasuredPlan[]) {
  const decorations: Decoration[] = []
  const plannedRanges: Array<{ from: number; to: number }> = []
  const groups = new Map<number, SnapshotResult[]>()
  for (const entry of snapshots) {
    const parent = entry.eligible ? entry.snapshot.nodeFrom : entry.nodeFrom
    groups.set(parent, [...groups.get(parent) ?? [], entry])
  }
  let planned = 0, fallback = 0, lineBreaks = 0
  for (const [parent, entries] of groups) {
    const valid = entries.every(e => e.eligible && plans.some(p => p.snapshotId === e.snapshot.id && p.verified && p.revision === e.snapshot.revision && p.environmentKey === e.snapshot.environmentKey))
    if (!valid) {
      fallback++
      // Never force justify onto unsupported content or override author alignment.
      if (entries.every(e => e.eligible && e.snapshot.alignment === 'default')) {
        const e = entries[0]!
        if (e.eligible) decorations.push(Decoration.node(parent, e.snapshot.nodeTo, { class: 'markleaf-typography-quality-fallback' }))
      }
      continue
    }
    planned++
    const first = entries[0]!
    if (!first.eligible) continue
    decorations.push(Decoration.node(parent, first.snapshot.nodeTo, { class: 'markleaf-typography-v2' }))
    for (const entry of entries) {
      if (!entry.eligible) continue
      const s = entry.snapshot, plan = plans.find(p => p.snapshotId === s.id)!
      plannedRanges.push({ from: s.segmentFrom, to: s.segmentTo })
      for (const line of plan.lines) {
        const base = s.segmentFrom
        for (const piece of typographyInlinePieces(s, line, line.slots, line.gaps)) {
          if (piece.from === piece.to) continue
          decorations.push(Decoration.inline(base + piece.from, base + piece.to, {
            'data-markleaf-typography-line-id': `${s.id}:${line.key}`,
            ...(piece.ids.length ? { 'data-markleaf-typography-slots': piece.ids.join(',') } : {}),
            ...(piece.spacing['word-spacing'] ? { 'data-markleaf-typography-natural-space': piece.css['word-spacing'] ?? '' } : {}),
            style: Object.entries(piece.spacing).map(([k, v]) => `${k}:${v}`).join(';'),
          }))
        }
        if (line.contentTo < line.to) decorations.push(Decoration.inline(base + line.contentTo, base + line.to, { class: 'markleaf-typography-consumed' }))
        if (line.to < s.text.length) {
          lineBreaks++
          decorations.push(Decoration.widget(base + line.to, view => {
            const br = view.dom.ownerDocument.createElement('br')
            br.className = 'markleaf-typography-break'
            br.setAttribute('aria-hidden', 'true'); br.contentEditable = 'false'
            return br
          }, { side: -1, ignoreSelection: true, marks: [], key: `typography-v2:${s.id}:${line.to}` }))
        }
      }
    }
  }
  return { decorations: DecorationSet.create(doc, decorations), plannedRanges,
    diagnostics: { status: planned ? fallback ? 'partial' as const : 'planned' as const : 'fallback' as const,
      eligibleParagraphs: [...groups.values()].filter(es => es.every(e => e.eligible)).length,
      plannedParagraphs: planned, fallbackParagraphs: fallback, lineBreaks } }
}
