import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'

export type PaintableBlock = 'paragraph' | `heading${1 | 2 | 3 | 4 | 5 | 6}`
export type FormatPainterSnapshot = {
  block: PaintableBlock
  marks: { bold: boolean; italic: boolean; underline: boolean; strike: boolean; code: boolean }
}
/// single = 单击（应用一次后自动关闭）；lock = 双击/锁定（反复应用直到取消）。
export type FormatPainterMode = 'single' | 'lock'

const supportedMarks = ['bold', 'italic', 'underline', 'strike', 'code'] as const
const forbiddenAncestorNames = new Set([
  'bulletList',
  'orderedList',
  'taskList',
  'listItem',
  'taskItem',
  'table',
  'tableRow',
  'tableCell',
  'tableHeader',
])

export function isPaintableTextSelection(editor: Editor): boolean {
  const selection = editor.state.selection
  if (!(selection instanceof TextSelection) || selection.empty) return false
  const { from, to } = selection
  const $from = editor.state.doc.resolve(from)
  const $to = editor.state.doc.resolve(to)
  if ($from.parent !== $to.parent) return false
  const parentName = $from.parent.type.name
  if (parentName !== 'paragraph' && parentName !== 'heading') return false
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if (forbiddenAncestorNames.has($from.node(depth).type.name)) return false
  }
  return true
}

export function captureFormat(editor: Editor): FormatPainterSnapshot | null {
  if (!isPaintableTextSelection(editor)) return null
  const { from, to } = editor.state.selection
  const $from = editor.state.doc.resolve(from)
  const parentName = $from.parent.type.name

  let block: PaintableBlock
  if (parentName === 'paragraph') {
    block = 'paragraph'
  } else {
    const level = editor.getAttributes('heading').level as unknown
    if (typeof level !== 'number' || level < 1 || level > 6) return null
    block = `heading${level}` as PaintableBlock
  }

  const marks: FormatPainterSnapshot['marks'] = {
    bold: false,
    italic: false,
    underline: false,
    strike: false,
    code: false,
  }
  let sawText = false
  let uniform = true
  editor.state.doc.nodesBetween(from, to, (node) => {
    if (!node.isText) return true
    const present = new Set(node.marks.map((mark) => mark.type.name))
    for (const mark of supportedMarks) {
      const value = present.has(mark)
      if (!sawText) {
        marks[mark] = value
      } else if (marks[mark] !== value) {
        uniform = false
      }
    }
    sawText = true
    return true
  })
  if (!sawText || !uniform) return null
  return { block, marks }
}

export function selectionContainsMark(editor: Editor, markName: string): boolean {
  const { from, to } = editor.state.selection
  let found = false
  editor.state.doc.nodesBetween(from, to, (node) => {
    if (found) return false
    if (node.isText && node.marks.some((mark) => mark.type.name === markName)) {
      found = true
      return false
    }
    return true
  })
  return found
}

export function applyCapturedFormat(editor: Editor, snapshot: FormatPainterSnapshot): boolean {
  if (!isPaintableTextSelection(editor)) return false
  if (snapshot.marks.code && selectionContainsMark(editor, 'link')) return false
  let chain = editor.chain().focus()
  chain = snapshot.block === 'paragraph'
    ? chain.setParagraph()
    : chain.setHeading({ level: Number(snapshot.block.slice('heading'.length)) as 1 | 2 | 3 | 4 | 5 | 6 })
  for (const mark of supportedMarks) {
    chain = snapshot.marks[mark] ? chain.setMark(mark) : chain.unsetMark(mark)
  }
  // setParagraph() on an already-paragraph target returns false even though the
  // transaction applies; eligibility pre-check above is the success gate.
  chain.run()
  return true
}

export class FormatPainterController {
  private snapshot: FormatPainterSnapshot | null = null
  private sourceRange: { from: number; to: number } | null = null
  private mode: FormatPainterMode = 'single'
  private armed = false

  get isArmed(): boolean { return this.armed }
  get currentMode(): FormatPainterMode { return this.mode }

  /// 吸附格式刷（对应 Word 单击/双击格式刷按钮）。
  arm(editor: Editor, mode: FormatPainterMode = 'single'): boolean {
    const snapshot = captureFormat(editor)
    if (!snapshot) return false
    const { from, to } = editor.state.selection
    this.snapshot = snapshot
    this.sourceRange = { from, to }
    this.mode = mode
    this.armed = true
    return true
  }

  cancel(): void {
    this.armed = false
    this.snapshot = null
    this.sourceRange = null
  }

  /// 鼠标抬起时应用：若当前是一个新的可涂抹文本选区，则套用已捕获格式。
  /// single 模式应用后自动关闭；lock 模式保持激活，可继续涂抹下一处。
  applyOnSelection(editor: Editor): boolean {
    if (!this.armed || !this.snapshot) return false
    const { from, to } = editor.state.selection
    const sr = this.sourceRange
    if (sr && from === sr.from && to === sr.to) return false
    if (!isPaintableTextSelection(editor)) return false

    const applied = applyCapturedFormat(editor, this.snapshot)
    if (this.mode === 'lock') {
      // 保持激活；清空来源锚点，使下一次选区改变可再次应用。
      this.sourceRange = null
      return applied
    }
    this.cancel()
    return applied
  }
}
