import { afterEach, expect, it } from 'vitest'
import type { Editor } from '@tiptap/core'
import { createEditor } from '../src/editor'
import {
  applyCapturedFormat,
  captureFormat,
  FormatPainterController,
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

function editorWithCaret(): Editor {
  return makeEditor('plain paragraph')
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

it('rejects caret, cross-block, list, table, node, and mixed-mark sources', () => {
  expect(captureFormat(editorWithCaret())).toBeNull()
  expect(captureFormat(editorWithCrossParagraphSelection())).toBeNull()
  expect(captureFormat(editorWithListSelection())).toBeNull()
  expect(captureFormat(editorWithTableSelection())).toBeNull()
  expect(captureFormat(editorWithSelectedImage())).toBeNull()
  expect(captureFormat(editorWithPartiallyBoldSelection())).toBeNull()
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

it('keeps armed on an invalid target and applies nothing', () => {
  const editor = makeEditor('**source**\n\n- target')
  const painter = new FormatPainterController()
  selectText(editor, 'source')
  expect(painter.arm(editor)).toBe(true)
  selectText(editor, 'target')
  expect(painter.applyOnSelection(editor)).toBe(false)
  expect(painter.isArmed).toBe(true)
  painter.cancel()
  expect(painter.isArmed).toBe(false)
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

it('applies a synthetic all-mark snapshot through the public API', () => {
  const editor = makeEditor('target')
  selectText(editor, 'target')
  const snapshot: FormatPainterSnapshot = {
    block: 'paragraph',
    marks: { bold: true, italic: true, underline: true, strike: true, code: true },
  }
  expect(applyCapturedFormat(editor, snapshot)).toBe(true)
})
