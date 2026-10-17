import { afterEach, describe, expect, it } from 'vitest'
import { TextSelection } from '@tiptap/pm/state'
import { createEditor } from '../src/index'
import { applyCapturedFormat, captureFormat, FormatPainterController } from '../src/format-painter'

const cleanup: Array<() => void> = []
function setup(markdown: string) {
  document.body.innerHTML = '<main id="editor"></main>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
  editor.view.setProps({ handleScrollToSelection: () => true })
  cleanup.push(() => editor.destroy())
  return editor
}
afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  document.body.innerHTML = ''
})

/// 定位包含 needle 文本的文本节点内 needle 起点的 PM 位置。
function textPosition(editor: ReturnType<typeof createEditor>, needle: string): number {
  let found: number | null = null
  editor.state.doc.descendants((node, pos) => {
    if (found !== null || !node.isText || !node.text?.includes(needle)) return
    found = pos + node.text.indexOf(needle)
  })
  if (found === null) throw new Error(`text not found: ${needle}`)
  return found
}

function selectText(editor: ReturnType<typeof createEditor>, needle: string): void {
  const from = textPosition(editor, needle)
  const to = from + needle.length
  editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, from, to)))
}

describe('format painter across table cells', () => {
  it('captures and paints highlight without copying link destinations or task state', () => {
    const editor = setup('- [x] ==[source](https://example.com)==\n\nplain target')
    selectText(editor, 'source')
    const snapshot = captureFormat(editor)
    expect(snapshot?.marks.highlight).toBe(true)
    const painter = new FormatPainterController()
    expect(painter.arm(editor)).toBe(true)
    selectText(editor, 'plain target')
    expect(painter.applyOnSelection(editor)).toBe(true)
    expect(painter.isArmed).toBe(false)
    const target = editor.state.doc.nodeAt(textPosition(editor, 'plain target'))!
    expect(target.marks.some(mark => mark.type.name === 'highlight')).toBe(true)
    expect(target.marks.some(mark => mark.type.name === 'link')).toBe(false)
    const last = editor.state.doc.lastChild!
    if (last.type.name === 'taskList') expect(last.firstChild?.attrs.checked).toBe(false)
  })

  it('rejects a mixed highlight source', () => {
    const editor = setup('==marked== plain')
    editor.commands.selectAll()
    expect(captureFormat(editor)).toBeNull()
  })
  it('captures a uniform inline-code selection inside a cell', () => {
    const editor = setup('| 名称 | 命令 |\n| --- | --- |\n| C2 | 使用 `tftp -i 127.0.0.1` 下载 |')
    selectText(editor, 'tftp -i 127.0.0.1')
    const snapshot = captureFormat(editor)
    expect(snapshot).not.toBeNull()
    expect(snapshot!.block).toBe('paragraph')
    expect(snapshot!.marks.code).toBe(true)
    expect(snapshot!.marks.bold).toBe(false)
  })

  it('keeps arming disabled while the selection spans two cells', () => {
    const editor = setup('| A | B |\n| --- | --- |\n| `one` | two |')
    const from = textPosition(editor, 'one')
    const to = textPosition(editor, 'two') + 3
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, from, to)))
    expect(captureFormat(editor)).toBeNull()
  })

  it('paints the captured code mark onto plain text in another cell by drag selection', () => {
    const editor = setup('| A | B |\n| --- | --- |\n| `tftp -i 127.0.0.1` | 目标文本 |')
    selectText(editor, 'tftp -i 127.0.0.1')
    const painter = new FormatPainterController()
    expect(painter.arm(editor)).toBe(true)

    selectText(editor, '目标文本')
    expect(painter.applyOnSelection(editor)).toBe(true)
    expect(painter.isArmed).toBe(false)

    const targetFrom = textPosition(editor, '目标文本')
    const marked: boolean[] = []
    editor.state.doc.nodesBetween(targetFrom, targetFrom + 4, (node) => {
      if (node.isText) marked.push(node.marks.some((mark) => mark.type.name === 'code'))
    })
    expect(marked).toEqual([true])
    // 单元格结构保持不变
    expect(getCellCount(editor)).toBe(4)
  })

  it('paints onto the whole cell paragraph from a caret in another cell', () => {
    const editor = setup('| A | B |\n| --- | --- |\n| `code` | 目标文本 |')
    selectText(editor, 'code')
    const painter = new FormatPainterController()
    expect(painter.arm(editor)).toBe(true)

    const caret = textPosition(editor, '目标文') + 2
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, caret, caret)))
    expect(painter.applyOnSelection(editor)).toBe(true)

    const targetFrom = textPosition(editor, '目标文本')
    const marks: Set<string>[] = []
    editor.state.doc.nodesBetween(targetFrom, targetFrom + 4, (node) => {
      if (node.isText) marks.push(new Set(node.marks.map((mark) => mark.type.name)))
    })
    expect(marks).toHaveLength(1)
    for (const set of marks) expect(set.has('code')).toBe(true)
  })

  it('applies only inline marks when the source block is a heading and the target is a cell', () => {
    const editor = setup('# 标题\n\n| A | B |\n| --- | --- |\n| 1 | 目标文本 |')
    selectText(editor, '标题')
    const snapshot = captureFormat(editor)
    expect(snapshot).toEqual({
      block: 'heading1',
      marks: { bold: false, italic: false, underline: false, strike: false, code: false, highlight: false },
    })
    const painter = new FormatPainterController()
    expect(painter.arm(editor)).toBe(true)

    selectText(editor, '目标文本')
    expect(applyCapturedFormat(editor, snapshot!)).toBe(true)
    const targetFrom = textPosition(editor, '目标文本')
    const parentBefore = editor.state.doc.resolve(targetFrom).parent
    expect(parentBefore.type.name).toBe('paragraph')
    expect(parentBefore.marks).toHaveLength(0)
  })
})

function getCellCount(editor: ReturnType<typeof createEditor>): number {
  let count = 0
  editor.state.doc.descendants((node) => {
    if (node.type.name === 'tableCell' || node.type.name === 'tableHeader') count += 1
  })
  return count
}
