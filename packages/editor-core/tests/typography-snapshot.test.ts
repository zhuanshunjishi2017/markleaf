import { afterEach, describe, expect, it } from 'vitest'
import { createEditor } from '../src/index'
import { extractProjectionSnapshots } from '../src/typography/projection-snapshot'

const cleanups: Array<() => void> = []
afterEach(() => { cleanups.splice(0).forEach(f => f()); document.body.innerHTML = '' })
function setup(text: string) {
  const mount = document.createElement('div'); document.body.append(mount)
  const editor = createEditor(mount, text, false)
  cleanups.push(() => editor.destroy())
  editor.view.dom.querySelectorAll('p').forEach((p, i) => {
    p.style.fontSize = '16px'; p.style.fontFamily = 'serif'; p.style.textAlign = ''
    p.getBoundingClientRect = () => ({ width: i ? 600 : 320 } as DOMRect)
  })
  return editor
}
describe('typography snapshot', () => {
  it('includesTheAutomaticCjkSpacingPreferenceInTheMeasuredEnvironment', () => {
    const editor = setup('中文English中文'), root = document.documentElement
    const previous = root.classList.contains('markleaf-cjk-autospace')
    cleanups.push(() => root.classList.toggle('markleaf-cjk-autospace', previous))
    root.classList.remove('markleaf-cjk-autospace')
    const off = extractProjectionSnapshots(editor.view, 0, 0)[0]!
    root.classList.add('markleaf-cjk-autospace')
    const on = extractProjectionSnapshots(editor.view, 0, 0)[0]!
    if (!off.eligible || !on.eligible) throw Error('missing snapshot')
    expect(off.snapshot).toMatchObject({ cjkAutoSpacing: false })
    expect(on.snapshot).toMatchObject({ cjkAutoSpacing: true })
    expect(on.snapshot.environmentKey).not.toBe(off.snapshot.environmentKey)
  })
  it('extractsActualParagraphWidthAndRuns', () => {
    const editor = setup('中文 **English**\n\n第二段')
    const snapshots = extractProjectionSnapshots(editor.view, 7, 2)
    expect(snapshots.every(x => x.eligible)).toBe(true)
    if (!snapshots[0]!.eligible || !snapshots[1]!.eligible) throw Error('missing snapshot')
    expect(snapshots.map(x => x.eligible && x.snapshot.width)).toEqual([320, 600])
    const s = snapshots[0]!.snapshot
    expect(s.alignment).toBe('default'); expect(s.revision).toBe(7)
    expect(s.runs).toHaveLength(2)
    expect(s.runs[0]!.styleKey).not.toBe(s.runs[1]!.styleKey)
    expect(Object.isFrozen(s)).toBe(true)
    expect(extractProjectionSnapshots(editor.view, 7, 3)[0]).not.toEqual(snapshots[0])
  })
  it('preservesAuthorAlignmentAndRejectsUnsupportedText', () => {
    const editor = setup('中文\n\nA\tB\n\n- 列表')
    editor.view.dom.querySelector('p')!.style.textAlign = 'left'
    const all = extractProjectionSnapshots(editor.view, 1, 0)
    expect(all[0]).toMatchObject({ eligible: false, reason: 'author-alignment' })
    expect(all[1]).toMatchObject({ eligible: false, reason: 'unsupported-text' })
    expect(all[2]).toMatchObject({ eligible: false, reason: 'unsupported-block' })
  })
  it('keepsHardBreakSegmentsWithinOneParent', () => {
    const editor = setup('A👍  \n中文')
    const all = extractProjectionSnapshots(editor.view, 1, 0)
    expect(all).toHaveLength(2)
    if (!all[0]!.eligible || !all[1]!.eligible) throw Error('ineligible')
    expect(all[0]!.snapshot.nodeFrom).toBe(all[1]!.snapshot.nodeFrom)
    expect(all[0]!.snapshot.graphemeOffsets).toEqual([0, 1, 3])
    expect(all[1]!.snapshot.segmentFrom).toBe(5)
  })
  it('keepsLiteralLineFeedsInMarkdownTextAsSegmentBoundaries', () => {
    const editor = setup('中文English\nEnglish中文')
    expect(editor.state.doc.firstChild!.firstChild!.text).toBe('中文English\nEnglish中文')
    const before = editor.getJSON()
    const all = extractProjectionSnapshots(editor.view, 1, 0)
    expect(all.map(e => e.eligible && [e.snapshot.text, e.snapshot.segmentFrom, e.snapshot.segmentTo]))
      .toEqual([['中文English', 1, 10], ['English中文', 11, 20]])
    expect(editor.getJSON()).toEqual(before)
  })
  it('retainsStylesAndExactOffsetsAcrossTextAndHardBreakBoundaries', () => {
    const editor = setup('initial')
    editor.commands.setContent({ type: 'doc', content: [{ type: 'paragraph', content: [
      { type: 'text', text: '\n中\n\n文', marks: [{ type: 'bold' }] },
      { type: 'hardBreak' }, { type: 'text', text: 'English\n' },
    ] }] })
    const before = editor.getJSON()
    const all = extractProjectionSnapshots(editor.view, 1, 0)
    expect(all.map(e => e.eligible && [e.snapshot.text, e.snapshot.segmentFrom, e.snapshot.segmentTo]))
      .toEqual([['', 1, 1], ['中', 2, 3], ['', 4, 4], ['文', 5, 6], ['English', 7, 14], ['', 15, 15]])
    const textSegments = all.flatMap(e => e.eligible && e.snapshot.text ? [e.snapshot] : [])
    expect(textSegments.map(s => s.runs.map(r => [r.from, r.to]))).toEqual([[[0, 1]], [[0, 1]], [[0, 7]]])
    expect(textSegments[0]!.runs[0]!.styleKey).toBe(textSegments[1]!.runs[0]!.styleKey)
    expect(textSegments[0]!.runs[0]!.styleKey).not.toBe(textSegments[2]!.runs[0]!.styleKey)
    expect(editor.getJSON()).toEqual(before)
  })
})
