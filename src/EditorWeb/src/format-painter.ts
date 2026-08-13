import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'
import type { ResolvedPos } from '@tiptap/pm/model'

export type PaintableBlock = 'paragraph' | `heading${1 | 2 | 3 | 4 | 5 | 6}`
export type FormatPainterSnapshot = {
  block: PaintableBlock
  marks: { bold: boolean; italic: boolean; underline: boolean; strike: boolean; code: boolean }
}

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

/// 光标/选区所在块是否为可涂抹的段落或标题（且不在列表/表格等容器内）。
function blockAt($from: ResolvedPos): PaintableBlock | null {
  const parent = $from.parent
  const name = parent.type.name
  let block: PaintableBlock
  if (name === 'paragraph') {
    block = 'paragraph'
  } else if (name === 'heading') {
    const level = parent.attrs.level as unknown
    if (typeof level !== 'number' || level < 1 || level > 6) return null
    block = `heading${level}` as PaintableBlock
  } else {
    return null
  }
  for (let depth = $from.depth; depth > 0; depth -= 1) {
    if (forbiddenAncestorNames.has($from.node(depth).type.name)) return null
  }
  return block
}

function uniformMarksInRange(editor: Editor, from: number, to: number): FormatPainterSnapshot['marks'] | null {
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
  return marks
}

function rangeContainsMark(editor: Editor, from: number, to: number, markName: string): boolean {
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

/// 吸附格式：有选区时取选区内统一标记；无选区（光标）时取光标处激活的标记。
export function captureFormat(editor: Editor): FormatPainterSnapshot | null {
  const selection = editor.state.selection
  if (!(selection instanceof TextSelection)) return null
  const { from, to } = selection
  const $from = editor.state.doc.resolve(from)
  const block = blockAt($from)
  if (block === null) return null

  if (selection.empty) {
    return {
      block,
      marks: {
        bold: editor.isActive('bold'),
        italic: editor.isActive('italic'),
        underline: editor.isActive('underline'),
        strike: editor.isActive('strike'),
        code: editor.isActive('code'),
      },
    }
  }

  // 有选区：必须在同一个块内，且行内标记统一。
  const $to = editor.state.doc.resolve(to)
  if ($from.parent !== $to.parent) return null
  const marks = uniformMarksInRange(editor, from, to)
  if (marks === null) return null
  return { block, marks }
}

/// 套用格式：有选区时涂抹选区；无选区（光标）时涂抹光标所在的整个文本块。
export function applyCapturedFormat(editor: Editor, snapshot: FormatPainterSnapshot): boolean {
  const selection = editor.state.selection
  if (!(selection instanceof TextSelection)) return false
  const { from, to } = selection
  const $from = editor.state.doc.resolve(from)
  if (blockAt($from) === null) return false

  const hasSelection = !selection.empty
  if (hasSelection) {
    const $to = editor.state.doc.resolve(to)
    if ($from.parent !== $to.parent) return false
  }

  const targetFrom = hasSelection ? from : $from.start()
  const targetTo = hasSelection ? to : $from.end()

  // code 源不涂抹包含链接的范围，避免破坏链接。
  if (snapshot.marks.code && rangeContainsMark(editor, targetFrom, targetTo, 'link')) return false

  let chain = editor.chain().focus()
  if (!hasSelection) {
    chain = chain.setTextSelection({ from: targetFrom, to: targetTo })
  }
  chain = snapshot.block === 'paragraph'
    ? chain.setParagraph()
    : chain.setHeading({ level: Number(snapshot.block.slice('heading'.length)) as 1 | 2 | 3 | 4 | 5 | 6 })
  for (const mark of supportedMarks) {
    chain = snapshot.marks[mark] ? chain.setMark(mark) : chain.unsetMark(mark)
  }
  if (!hasSelection) {
    chain = chain.setTextSelection(from)
  }
  chain.run()
  return true
}

/// 目标是否可涂抹：有选区时必须在同一可涂抹块内；无选区（光标）时必须在可涂抹块内。
function isPaintableTarget(editor: Editor): boolean {
  const selection = editor.state.selection
  if (!(selection instanceof TextSelection)) return false
  const { from, to } = selection
  const $from = editor.state.doc.resolve(from)
  if (blockAt($from) === null) return false
  if (selection.empty) return true
  const $to = editor.state.doc.resolve(to)
  return $from.parent === $to.parent
}

export class FormatPainterController {
  private snapshot: FormatPainterSnapshot | null = null
  private sourceRange: { from: number; to: number } | null = null
  private armed = false

  get isArmed(): boolean { return this.armed }

  /// 吸附格式刷（对齐 Word 单击格式刷按钮）。
  arm(editor: Editor): boolean {
    const snapshot = captureFormat(editor)
    if (!snapshot) return false
    const { from, to } = editor.state.selection
    this.snapshot = snapshot
    this.sourceRange = { from, to }
    this.armed = true
    return true
  }

  cancel(): void {
    this.armed = false
    this.snapshot = null
    this.sourceRange = null
  }

  /// 鼠标抬起时应用：若当前是新的可涂抹选区或光标所在块，则套用已捕获格式后自动关闭。
  applyOnSelection(editor: Editor): boolean {
    if (!this.armed || !this.snapshot) return false
    const { from, to } = editor.state.selection
    const sr = this.sourceRange
    if (sr && from === sr.from && to === sr.to) return false
    // 目标块不可涂抹（列表/表格/图片等）时保持激活，等待下一次有效涂抹。
    if (!isPaintableTarget(editor)) return false

    const applied = applyCapturedFormat(editor, this.snapshot)
    this.cancel()
    return applied
  }
}
