import { afterEach, expect, it } from 'vitest'
import { createEditor, getMarkdown } from '../src/index'
import { applyTypographyPlan, getTypographyProjectionRevision, registerTypographySnapshots } from '../src/typography/projection'
import { extractProjectionSnapshots } from '../src/typography/projection-snapshot'
import type { MeasuredPlan } from '@markleaf/typography'
import { getSupportedTypographyEdges, typographyInlinePieces } from '../src/typography/projection-dom'
import type { GapSlot } from '@markleaf/typography'
import { buildGapSlots } from '@markleaf/typography'
import { closeHistory, undoDepth } from '@tiptap/pm/history'
const cleanup: Array<() => void> = []
afterEach(() => { cleanup.splice(0).forEach(f => f()); document.body.innerHTML = '' })
it('projectsPastedTextAroundLiteralLineFeedsWithoutEditingTheDocument', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, 'initial', false)
  cleanup.push(() => editor.destroy())
  editor.commands.insertContentAt({ from: 1, to: 8 }, { type: 'text', text: '中文English\nEnglish中文' })
  const before = editor.getJSON(), markdown = getMarkdown(editor), selection = editor.state.selection.toJSON(), depth = undoDepth(editor.state)
  const revision = getTypographyProjectionRevision(editor), entries = extractProjectionSnapshots(editor.view, revision, 0)
  const plans: MeasuredPlan[] = entries.map(e => {
    if (!e.eligible) throw Error('pasted soft break made the whole paragraph ineligible')
    const s = e.snapshot
    return { snapshotId: s.id, environmentKey: s.environmentKey, revision, verified: true,
      lines: [{ from: 0, to: s.text.length, contentTo: s.text.length, key: 'one', targetWidth: 100, naturalWidth: 90,
        slots: [], gaps: [], cost: 0, breakReason: 'paragraph-end' }] }
  })
  registerTypographySnapshots(editor, true, entries, plans)
  expect(editor.view.dom.querySelector('p')!.classList.contains('markleaf-typography-v2')).toBe(true)
  expect([...editor.view.dom.querySelectorAll('[data-markleaf-typography-line-id]')].map(el => el.textContent))
    .toEqual(['中文English', 'English中文'])
  expect(editor.view.dom.textContent).toBe('中文English\nEnglish中文')
  expect(editor.view.dom.querySelectorAll('br')).toHaveLength(0)
  expect(editor.getJSON()).toEqual(before); expect(getMarkdown(editor)).toBe(markdown)
  expect(editor.state.selection.toJSON()).toEqual(selection); expect(undoDepth(editor.state)).toBe(depth)
})
it('ownsFallbackBoundarySpacingBeforeTheBoundaryWithoutSplittingLatinWords', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中文**English**中文[English](https://example.com)中文  \nEnglish中文', false)
  cleanup.push(() => editor.destroy())
  const before = editor.getJSON(), markdown = getMarkdown(editor), selection = editor.state.selection.toJSON(), depth = undoDepth(editor.state)
  const revision = getTypographyProjectionRevision(editor), entries = extractProjectionSnapshots(editor.view, revision, 0)
  registerTypographySnapshots(editor, true, entries)
  const p = editor.view.dom.querySelector('p')!
  expect(p.classList.contains('markleaf-typography-quality-fallback')).toBe(true)
  expect(p.querySelectorAll('.markleaf-cjk-autospace-space')).toHaveLength(0)
  expect([...p.querySelectorAll('.markleaf-cjk-autospace-trailing-space')].map(el => el.textContent)).toEqual(['文', 'English', '文', 'English', 'English'])
  expect(p.querySelector('strong')?.textContent).toBe('English')
  expect(p.querySelector('a')?.textContent).toBe('English')
  expect(p.querySelectorAll('br')).toHaveLength(1)
  expect(editor.getJSON()).toEqual(before); expect(getMarkdown(editor)).toBe(markdown)
  expect(editor.state.selection.toJSON()).toEqual(selection); expect(undoDepth(editor.state)).toBe(depth)
  registerTypographySnapshots(editor, false, [])
  expect(p.querySelectorAll('.markleaf-cjk-autospace-trailing-space')).toHaveLength(0)
  expect(p.querySelectorAll('.markleaf-cjk-autospace-space').length).toBeGreaterThan(0)
})
it('appliesFallbackSpacingOnceWhenALatinWordCrossesMarkBoundaries', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中En**gli**sh中文', false)
  cleanup.push(() => editor.destroy())
  const before = editor.getJSON(), entries = extractProjectionSnapshots(editor.view, getTypographyProjectionRevision(editor), 0)
  registerTypographySnapshots(editor, true, entries)
  expect([...editor.view.dom.querySelectorAll('.markleaf-cjk-autospace-trailing-space')].map(el => el.textContent)).toEqual(['中', 'sh'])
  expect(editor.view.dom.querySelector('strong')?.textContent).toBe('gli')
  expect(editor.getJSON()).toEqual(before)
})
it('discardsFallbackOwnershipWhenAnEditInvalidatesSnapshotOffsets', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中文English'.repeat(20), false)
  cleanup.push(() => editor.destroy())
  registerTypographySnapshots(editor, true, extractProjectionSnapshots(editor.view, getTypographyProjectionRevision(editor), 0))
  expect(editor.view.dom.querySelectorAll('.markleaf-cjk-autospace-trailing-space').length).toBeGreaterThan(0)
  expect(() => editor.commands.setContent('短')).not.toThrow()
  expect(editor.state.doc.textContent).toBe('短')
  expect(editor.view.dom.querySelectorAll('.markleaf-cjk-autospace-trailing-space')).toHaveLength(0)
})
it('appliesOnlyVerifiedCurrentPlansWithoutChangingSource', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中文 **English**', false)
  cleanup.push(() => editor.destroy())
  const revision = getTypographyProjectionRevision(editor)
  const snapshots = extractProjectionSnapshots(editor.view, revision, 0)
  const entry = snapshots[0]!
  if (!entry.eligible) throw Error('missing')
  const s = entry.snapshot
  const before = getMarkdown(editor)
  registerTypographySnapshots(editor, true, snapshots)
  const plan: MeasuredPlan = { snapshotId: s.id, revision, environmentKey: s.environmentKey, verified: true,
    lines: [{ from: 0, to: s.text.length, contentTo: s.text.length, key: 'one', targetWidth: 100, naturalWidth: 90,
      gaps: [], slots: [], cost: 0, breakReason: 'paragraph-end' }] }
  expect(applyTypographyPlan(editor, [{ ...plan, environmentKey: 'old' }], revision)).toBe(false)
  expect(applyTypographyPlan(editor, [plan], revision)).toBe(true)
  expect(editor.view.dom.querySelectorAll('.markleaf-typography-v2')).toHaveLength(1)
  const displayed = editor.view.dom.innerHTML
  expect(registerTypographySnapshots(editor, true, snapshots, [{ ...plan, environmentKey: 'stale' }])).toBe(false)
  expect(editor.view.dom.innerHTML).toBe(displayed)
  expect(registerTypographySnapshots(editor, true, snapshots, [plan])).toBe(true)
  expect(editor.view.dom.innerHTML).toBe(displayed)
  expect(getMarkdown(editor)).toBe(before)
  editor.commands.insertContent('x')
  expect(applyTypographyPlan(editor, [plan], revision)).toBe(false)
  expect(registerTypographySnapshots(editor, true, snapshots, [plan])).toBe(false)
})

it('rendersAllocatedSlotsExactlyOnceWithoutChangingSourceOrEnvironment', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中 **A B** 文', false)
  cleanup.push(() => editor.destroy())
  const p = editor.view.dom.querySelector('p')!; p.style.fontSize = '16px'
  p.getBoundingClientRect = () => ({ width: 200 } as DOMRect)
  const revision = getTypographyProjectionRevision(editor), entries = extractProjectionSnapshots(editor.view, revision, 0)
  const e = entries[0]!
  if (!e.eligible) throw Error('missing')
  const s = e.snapshot, before = getMarkdown(editor)
  const slot: GapSlot = { id: 'word-space:3', at: 3, kind: 'word-space', owner: { from: 3, to: 4 }, edge: 'space', natural: 4, min: 3.2, max: 5.6, weight: 1, leftStyleKey: 'a', rightStyleKey: 'a' }
  expect(getSupportedTypographyEdges(s).has(slot.id)).toBe(true)
  const line = { from: 0, to: s.text.length, contentTo: s.text.length, key: 'line', targetWidth: 200, naturalWidth: 180,
    slots: [slot], gaps: [{ slotId: slot.id, width: 5, adjustment: 1 }], cost: 1, breakReason: 'paragraph-end' as const }
  expect(typographyInlinePieces(s, line, line.slots, line.gaps).filter(x => x.ids.includes(slot.id))).toHaveLength(1)
  registerTypographySnapshots(editor, true, entries)
  applyTypographyPlan(editor, [{ snapshotId: s.id, environmentKey: s.environmentKey, revision, verified: true, lines: [line] }], revision)
  expect(p.querySelectorAll('[data-markleaf-typography-slots]')).toHaveLength(1)
  expect((p.querySelector('[data-markleaf-typography-slots]') as HTMLElement).style.wordSpacing).toBe('1px')
  expect(getMarkdown(editor)).toBe(before)
  const after = extractProjectionSnapshots(editor.view, revision, 0)[0]!
  expect(after.eligible && after.snapshot.environmentKey).toBe(s.environmentKey)
})

it('preservesNestedMarksSelectionAndUndoWhenApplyingMixedSlots', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '', false); cleanup.push(() => editor.destroy())
  editor.commands.setContent({ type: 'doc', content: [{ type: 'paragraph', content: [
    { type: 'text', text: '中' },
    { type: 'text', text: '文A中', marks: [{ type: 'bold' }, { type: 'underline' }, { type: 'link', attrs: { href: 'https://example.com' } }] },
    { type: 'text', text: '文' },
  ] }] })
  editor.view.dom.querySelector('p')!.style.fontSize = '16px'
  editor.view.dispatch(closeHistory(editor.state.tr))
  editor.commands.setTextSelection({ from: 2, to: 4 })
  const before = editor.getJSON(), markdown = getMarkdown(editor), selection = editor.state.selection.toJSON(), depth = undoDepth(editor.state)
  const revision = getTypographyProjectionRevision(editor), entries = extractProjectionSnapshots(editor.view, revision, 0), e = entries[0]!
  if (!e.eligible) throw Error('missing snapshot')
  const s = e.snapshot, slots = buildGapSlots(s, getSupportedTypographyEdges(s), new Map())
  const plan: MeasuredPlan = { snapshotId: s.id, revision, environmentKey: s.environmentKey, verified: true, lines: [
    { from: 0, to: s.text.length, contentTo: s.text.length, key: 'all', naturalWidth: 90, targetWidth: 100, cost: 0, breakReason: 'paragraph-end', slots,
      gaps: slots.map(slot => ({ slotId: slot.id, width: slot.natural, adjustment: 0 })) },
  ] }
  registerTypographySnapshots(editor, true, entries); applyTypographyPlan(editor, [plan], revision)
  for (const slot of slots) expect([...editor.view.dom.querySelectorAll('[data-markleaf-typography-slots]')].filter(el => el.getAttribute('data-markleaf-typography-slots')!.split(',').includes(slot.id))).toHaveLength(1)
  expect(editor.view.dom.querySelector('a[href="https://example.com"]')?.textContent).toBe('文A中')
  expect(editor.view.dom.querySelector('a strong u, a u strong, strong u a, u strong a')).not.toBeNull()
  expect(editor.getJSON()).toEqual(before); expect(getMarkdown(editor)).toBe(markdown)
  expect(editor.state.selection.toJSON()).toEqual(selection); expect(undoDepth(editor.state)).toBe(depth)
  editor.commands.insertContent('替换')
  expect(editor.state.doc.textContent).toContain('替换')
  editor.commands.undo(); expect(editor.getJSON()).toEqual(before)
})

it('commitsHardBreakSegmentsOnlyAsAWholeParagraph', () => {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, '中文  \n中文', false); cleanup.push(() => editor.destroy())
  const revision = getTypographyProjectionRevision(editor), entries = extractProjectionSnapshots(editor.view, revision, 0)
  const segments = entries.flatMap(e => e.eligible && e.snapshot.nodeFrom === 0 ? [e.snapshot] : [])
  expect(segments).toHaveLength(2)
  const plans: MeasuredPlan[] = segments.map(s => ({ snapshotId: s.id, revision, environmentKey: s.environmentKey, verified: true, lines: [
    { from: 0, to: s.text.length, contentTo: s.text.length, key: s.id, naturalWidth: 32, targetWidth: 100, cost: 0, breakReason: 'paragraph-end', slots: [], gaps: [] },
  ] }))
  const before = getMarkdown(editor)
  registerTypographySnapshots(editor, true, entries); applyTypographyPlan(editor, plans.slice(0, 1), revision)
  expect(editor.view.dom.querySelector('p.markleaf-typography-v2')).toBeNull()
  expect(editor.view.dom.querySelector('p.markleaf-typography-quality-fallback')).not.toBeNull()
  applyTypographyPlan(editor, plans, revision)
  expect(editor.view.dom.querySelector('p.markleaf-typography-v2')).not.toBeNull()
  expect(getMarkdown(editor)).toBe(before)
})
