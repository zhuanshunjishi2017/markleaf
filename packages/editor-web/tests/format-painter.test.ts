import { afterEach, expect, it } from 'vitest'
import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'
import { createEditor } from '../src/editor'
import {
  applyCapturedFormat,
  captureFormat,
  executeFormatPainterApply,
  FormatPainterController,
  normalizeContextMenuCaretPosition,
  type FormatPainterSnapshot,
} from '../src/format-painter'

const editors: Editor[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) {
    editor.destroy()
  }
  document.body.innerHTML = ''
})

function makeEditor(markdown: string): Editor {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, markdown)
  editors.push(editor)
  return editor
}

function selectText(editor: Editor, text: string): void {
  let from = -1
  let to = -1
  editor.state.doc.descendants((node, pos) => {
    if (from >= 0) return false
    if (node.isText) {
      const index = node.text!.indexOf(text)
      if (index >= 0) {
        from = pos + index
        to = from + text.length
        return false
      }
    }
    return true
  })
  expect(from).toBeGreaterThanOrEqual(0)
  editor.commands.setTextSelection({ from, to })
}

function textRange(editor: Editor, text: string): { from: number; to: number } {
  let from = -1
  let to = -1
  editor.state.doc.descendants((node, pos) => {
    if (from >= 0) return false
    if (node.isText) {
      const index = node.text!.indexOf(text)
      if (index >= 0) {
        from = pos + index
        to = from + text.length
        return false
      }
    }
    return true
  })
  expect(from).toBeGreaterThanOrEqual(0)
  return { from, to }
}

function selectWholeBlockBackwards(editor: Editor, text: string): void {
  let anchor = -1
  let head = -1
  editor.state.doc.descendants((node, pos) => {
    if (head >= 0) return false
    if (node.isTextblock && node.textContent === text) {
      head = pos + 1
      anchor = pos + node.nodeSize
      return false
    }
    return true
  })
  expect(head).toBeGreaterThanOrEqual(0)
  editor.view.dispatch(editor.state.tr.setSelection(TextSelection.between(
    editor.state.doc.resolve(anchor),
    editor.state.doc.resolve(head),
  )))
}

function placeCaret(editor: Editor, text: string, offset = 1): void {
  let pos = -1
  editor.state.doc.descendants((node, p) => {
    if (pos >= 0) return false
    if (node.isText) {
      const index = node.text!.indexOf(text)
      if (index >= 0) {
        pos = p + index + offset
        return false
      }
    }
    return true
  })
  expect(pos).toBeGreaterThanOrEqual(0)
  editor.commands.setTextSelection(pos)
}

function editorWithCrossParagraphSelection(): Editor {
  const editor = makeEditor('first paragraph\n\nsecond paragraph')
  let from = -1
  let to = -1
  editor.state.doc.descendants((node, pos) => {
    if (node.isText) {
      if (from < 0) from = pos
      to = pos + node.nodeSize
    }
    return true
  })
  editor.commands.setTextSelection({ from, to })
  return editor
}

function editorWithListSelection(): Editor {
  const editor = makeEditor('- list item\n- other item')
  selectText(editor, 'list item')
  return editor
}

function editorWithTableSelection(): Editor {
  const editor = makeEditor('| a | b |\n| --- | --- |\n| 1 | 2 |')
  selectText(editor, '1')
  return editor
}

function editorWithSelectedImage(): Editor {
  const editor = makeEditor('before ![alt](image.png) after')
  let position = -1
  editor.state.doc.descendants((node, pos) => {
    if (position >= 0) return false
    if (node.type.name === 'image') {
      position = pos
      return false
    }
    return true
  })
  expect(position).toBeGreaterThanOrEqual(0)
  editor.commands.setNodeSelection(position)
  return editor
}

function editorWithPartiallyBoldSelection(): Editor {
  const editor = makeEditor('**bold**tail')
  let from = -1
  let to = -1
  editor.state.doc.descendants((node, pos) => {
    if (node.isText) {
      if (from < 0) from = pos
      to = pos + node.nodeSize
    }
    return true
  })
  editor.commands.setTextSelection({ from, to })
  return editor
}

it('captures a heading and uniform supported marks', () => {
  const editor = makeEditor('## **source**\n\ntarget')
  selectText(editor, 'source')
  const snapshot = captureFormat(editor)
  expect(snapshot).toEqual({
    block: 'heading2',
    marks: { bold: true, italic: false, underline: false, strike: false, code: false },
  })
})

it('rejects cross-block, table, node, and mixed-mark sources', () => {
  expect(captureFormat(editorWithCrossParagraphSelection())).toBeNull()
  expect(captureFormat(editorWithTableSelection())).toBeNull()
  expect(captureFormat(editorWithSelectedImage())).toBeNull()
  expect(captureFormat(editorWithPartiallyBoldSelection())).toBeNull()
})

it('captures a bullet list block and its marks', () => {
  const snapshot = captureFormat(editorWithListSelection())
  expect(snapshot).toEqual({
    block: 'bulletList',
    marks: { bold: false, italic: false, underline: false, strike: false, code: false },
  })
})

it('captures the block and active marks at a caret', () => {
  const editor = makeEditor('## **source** tail')
  placeCaret(editor, 'source', 1)
  const snapshot = captureFormat(editor)
  expect(snapshot).toEqual({
    block: 'heading2',
    marks: { bold: true, italic: false, underline: false, strike: false, code: false },
  })
})

it('captures the format immediately before a caret at the end of a marked run', () => {
  const editor = makeEditor('**source** plain')
  placeCaret(editor, 'source', 'source'.length)
  expect(captureFormat(editor)?.marks.bold).toBe(true)
})

it('captures a caret positioned at the end of the final paragraph', () => {
  const editor = makeEditor('plain paragraph')
  editor.commands.setTextSelection(editor.state.doc.content.size - 1)
  expect(editor.state.selection.empty).toBe(true)
  expect(captureFormat(editor)).not.toBeNull()
})

it('normalizes a document-boundary context-menu position into the final paragraph', () => {
  const editor = makeEditor('plain paragraph')
  const position = normalizeContextMenuCaretPosition(editor, editor.state.doc.content.size)
  editor.commands.setTextSelection(position)
  expect(captureFormat(editor)).not.toBeNull()
})

it('captures format when the editor reports the document boundary as a caret', () => {
  const editor = makeEditor('plain paragraph')
  editor.commands.setTextSelection(editor.state.doc.content.size)
  expect(editor.state.selection.empty).toBe(true)
  expect(captureFormat(editor)).not.toBeNull()
})

it('captures a selection whose end is reported as the document boundary', () => {
  const editor = makeEditor('plain paragraph')
  editor.commands.setTextSelection({ from: 1, to: editor.state.doc.content.size })
  expect(editor.state.selection.empty).toBe(false)
  expect(captureFormat(editor)).not.toBeNull()
})

it('applies once without changing text or link href and one undo restores the target', () => {
  const editor = makeEditor('## **source**\n\n[target](https://example.com)')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(painter.isArmed).toBe(false)
  expect(editor.getMarkdown()).toContain('## [**target**](https://example.com)')
  editor.commands.undo()
  expect(editor.getMarkdown()).toContain('[target](https://example.com)')
})

it('applies to a whole line selected from right to left', () => {
  const editor = makeEditor('**source**\n\ntarget line\n\nafter')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)

  selectWholeBlockBackwards(editor, 'target line')

  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('**target line**')
})

it('applies to every paintable block in a multi-paragraph selection', () => {
  const editor = makeEditor('**source**\n\ntarget one\n\ntarget two')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  const first = textRange(editor, 'target one')
  const second = textRange(editor, 'target two')
  editor.commands.setTextSelection({ from: first.from, to: second.to })

  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('**target one**')
  expect(editor.getMarkdown()).toContain('**target two**')
})

it('keeps armed on an invalid target and applies nothing', () => {
  const editor = makeEditor('**source**\n\n| a |\n| --- |\n| 1 |')
  const painter = new FormatPainterController()
  selectText(editor, 'source')
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, '1')
  expect(painter.applyOnSelection(editor)).toBe(false)
  expect(painter.isArmed).toBe(true)
  painter.cancel()
  expect(painter.isArmed).toBe(false)
})

it('paints a bullet list onto a paragraph target', () => {
  const editor = makeEditor('- source\n\ntarget')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('- target')
})

it('paints a paragraph format onto a list item (exits the list)', () => {
  const editor = makeEditor('source\n\n- target')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('target')
  expect(editor.getMarkdown()).not.toContain('- target')
})

it('paints marks between list items', () => {
  const editor = makeEditor('- **source**\n\n- target')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('- **target**')
})

it('returns false for the unchanged source range', () => {
  const editor = makeEditor('**source**\n\ntarget')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  expect(painter.applyOnSelection(editor)).toBe(false)
  expect(painter.isArmed).toBe(true)
  painter.cancel()
  expect(painter.isArmed).toBe(false)
})

it('round-trips paragraph and heading levels 1 through 6', () => {
  const editor = makeEditor('source\n\ntarget')
  selectText(editor, 'source')
  const paragraphPainter = new FormatPainterController()
  expect(paragraphPainter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(paragraphPainter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('target')

  for (let level = 1; level <= 6; level += 1) {
    const headingEditor = makeEditor(`${'#'.repeat(level)} source\n\ntarget`)
    selectText(headingEditor, 'source')
    const painter = new FormatPainterController()
    expect(painter.arm(headingEditor)).toBe(true)
    selectText(headingEditor, 'target')
    expect(painter.applyOnSelection(headingEditor)).toBe(true)
    expect(headingEditor.getMarkdown()).toContain(`${'#'.repeat(level)} target`)
  }
})

it('replaces each supported mark on the target', () => {
  const marks = ['bold', 'italic', 'underline', 'strike', 'code'] as const
  for (const mark of marks) {
    const editor = makeEditor('source\n\ntarget')
    selectText(editor, 'source')
    editor.chain().focus().setMark(mark).run()
    const painter = new FormatPainterController()
    expect(painter.arm(editor)).toBe(true)
    selectText(editor, 'target')
    expect(painter.applyOnSelection(editor)).toBe(true)
    expect(editor.isActive(mark)).toBe(true)
  }
})

it('keeps target text byte-for-byte unchanged', () => {
  const editor = makeEditor('**source**\n\ntarget text')
  const before = editor.state.doc.textContent
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target text')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.state.doc.textContent).toBe(before)
})

it('single mode disarms after one paint', () => {
  const editor = makeEditor('**source**\n\ntarget one\n\ntarget two')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target one')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(painter.isArmed).toBe(false)

  selectText(editor, 'target two')
  expect(painter.applyOnSelection(editor)).toBe(false)
  expect(editor.getMarkdown()).toContain('**target one**')
  expect(editor.getMarkdown()).toContain('target two')
})

it('disarms without applying when a code source targets linked text', () => {
  const editor = makeEditor('`source`\n\n[target](https://example.com)')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(false)
  expect(painter.isArmed).toBe(false)
  expect(editor.getMarkdown()).toContain('[target](https://example.com)')
})

it('preserves the href when non-code formatting targets a link', () => {
  const editor = makeEditor('**source**\n\n[target](https://example.com)')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('[**target**](https://example.com)')
})

it('applies captured format to the paragraph at a caret', () => {
  const editor = makeEditor('**source**\n\ntarget paragraph')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  placeCaret(editor, 'target paragraph', 3)
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(painter.isArmed).toBe(false)
  expect(editor.getMarkdown()).toContain('**target paragraph**')
})

it('keeps captured marks when painting an empty caret paragraph before typing', () => {
  const editor = makeEditor('**source**\n\ntarget')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  editor.commands.deleteSelection()
  expect(editor.state.selection.empty).toBe(true)
  expect(painter.applyOnSelection(editor)).toBe(true)
  editor.commands.insertContent('typed')
  expect(editor.getMarkdown()).toContain('**typed**')
})

it('applies an armed format through the shortcut command', () => {
  const editor = makeEditor('**source** target')
  selectText(editor, 'source')
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')

  expect(executeFormatPainterApply(editor, painter, false)).toBe(true)
  expect(editor.getMarkdown()).toBe('**source** **target**')
  expect(painter.isArmed).toBe(false)
})

it('rejects the shortcut command when source mode or unarmed', () => {
  const editor = makeEditor('source target')
  const painter = new FormatPainterController()
  selectText(editor, 'target')

  expect(executeFormatPainterApply(editor, painter, true)).toBe(false)
  expect(executeFormatPainterApply(editor, painter, false)).toBe(false)
})

it('paints paragraph format from a caret source to a caret target', () => {
  const editor = makeEditor('## heading text\n\nplain target')
  placeCaret(editor, 'heading text', 2)
  const painter = new FormatPainterController()
  expect(painter.arm(editor)).toBe(true)
  placeCaret(editor, 'plain target', 2)
  expect(painter.applyOnSelection(editor)).toBe(true)
  expect(editor.getMarkdown()).toContain('## plain target')
})

it('applies a synthetic all-mark snapshot through the public API', () => {
  const editor = makeEditor('target')
  selectText(editor, 'target')
  const snapshot: FormatPainterSnapshot = {
    block: 'paragraph',
    marks: { bold: true, italic: true, underline: true, strike: true, code: true },
  }
  expect(applyCapturedFormat(editor, snapshot)).toBe(true)
})
