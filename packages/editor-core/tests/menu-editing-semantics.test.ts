import { afterEach, describe, expect, it } from 'vitest'
import type { Editor } from '@tiptap/core'
import { TextSelection } from '@tiptap/pm/state'
import { CellSelection } from '@tiptap/pm/tables'
import { undoDepth } from '@tiptap/pm/history'
import { createEditor, executeEditorCommand, getMarkdown, setEditorFormatPreview, getEditorCommandPresentation } from '../src/index'

let editor: Editor
function setup(markdown = 'existing text') {
  document.body.innerHTML = '<main></main>'
  editor = createEditor(document.querySelector('main')!, markdown, false)
  editor.view.setProps({ handleScrollToSelection: () => true })
  return editor
}
afterEach(() => { editor?.destroy(); document.body.innerHTML = '' })

const formats = [
  ['toggleBold', 'bold'], ['toggleItalic', 'italic'], ['toggleUnderline', 'underline'],
  ['toggleStrike', 'strike'], ['toggleHighlight', 'highlight'], ['toggleCode', 'code'],
] as const

describe('menu editing semantics', () => {
  it.each(formats)('empty %s only changes stored marks', (command, mark) => {
    setup()
    editor.commands.setTextSelection(5)
    const before = editor.state.doc
    let updates = 0
    editor.on('update', () => updates++)
    expect(executeEditorCommand(editor, command)).toBe(true)
    expect(editor.state.doc.eq(before)).toBe(true)
    expect(editor.state.storedMarks?.some(m => m.type.name === mark)).toBe(true)
    expect(updates).toBe(0)
    editor.view.dispatch(editor.state.tr.insertText('X'))
    expect(editor.state.doc.nodeAt(5)?.marks.some(m => m.type.name === mark)).toBe(true)
  })

  it('clear format preserves heading, link, inline math and images', () => {
    setup('# **bold [linked](https://example.com)** $x$\n\n![pic](image.png)')
    editor.commands.selectAll()
    const before = editor.state.doc
    const beforeHistory = undoDepth(editor.state)
    expect(executeEditorCommand(editor, 'clearFormat')).toBe(true)
    expect(editor.state.doc.firstChild?.type.name).toBe('heading')
    const types: string[] = []
    const marks: string[] = []
    editor.state.doc.descendants(node => { types.push(node.type.name); marks.push(...node.marks.map(m => m.type.name)) })
    expect(types).toContain('mathInline')
    expect(types).toContain('image')
    expect(marks).toContain('link')
    expect(marks).not.toContain('bold')
    expect(undoDepth(editor.state)).toBe(beforeHistory + 1)
    editor.commands.undo()
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('empty clear only clears pending input, not the paragraph', () => {
    setup('**existing** text')
    editor.commands.setTextSelection(4)
    const before = editor.state.doc
    expect(executeEditorCommand(editor, 'clearFormat')).toBe(true)
    expect(editor.state.doc.eq(before)).toBe(true)
    expect(editor.state.storedMarks?.some(m => m.type.name === 'bold')).toBe(false)
    editor.view.dispatch(editor.state.tr.insertText('X'))
    expect(editor.state.doc.nodeAt(4)?.marks.some(m => m.type.name === 'bold')).toBe(false)
  })

  it('mixed selection applies then removes the same mark', () => {
    setup('**bold** plain')
    editor.commands.selectAll()
    executeEditorCommand(editor, 'toggleBold')
    expect(editor.state.doc.firstChild?.content.firstChild?.marks.some(m => m.type.name === 'bold')).toBe(true)
    executeEditorCommand(editor, 'toggleBold')
    editor.state.doc.descendants(node => { if (node.isText) expect(node.marks.some(m => m.type.name === 'bold')).toBe(false) })
  })

  it.each(formats)('Enter keeps %s except inline code', (command, mark) => {
    setup('first')
    editor.commands.setTextSelection(6)
    executeEditorCommand(editor, command)
    const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
    editor.view.dom.dispatchEvent(enter)
    expect(enter.defaultPrevented).toBe(true)
    editor.view.dispatch(editor.state.tr.insertText('next'))
    expect(editor.state.selection.$from.parent.firstChild?.marks.some(m => m.type.name === mark)).toBe(mark !== 'code')
  })

  it('moving the caret recomputes pending marks', () => {
    setup('**bold** plain')
    editor.commands.setTextSelection(2)
    executeEditorCommand(editor, 'toggleItalic')
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, 9)))
    expect(editor.state.storedMarks).toBeNull()
    expect(editor.state.selection.$from.marks().some(m => m.type.name === 'italic')).toBe(false)
  })

  it('preview never changes the document or history, including composition', () => {
    setup('some text')
    editor.commands.setTextSelection({ from: 1, to: 5 })
    const before = getMarkdown(editor)
    const depth = undoDepth(editor.state)
    editor.view.dom.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))
    setEditorFormatPreview(editor, 'toggleItalic')
    setEditorFormatPreview(editor, null)
    editor.view.dom.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }))
    expect(getMarkdown(editor)).toBe(before)
    expect(undoDepth(editor.state)).toBe(depth)
  })

  it('repeating a heading is idempotent and promotion only applies to headings', () => {
    setup('plain')
    const before = editor.state.doc
    expect(executeEditorCommand(editor, 'promoteHeading')).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
    executeEditorCommand(editor, 'setHeading2')
    executeEditorCommand(editor, 'setHeading2')
    expect(editor.state.doc.firstChild?.type.name).toBe('heading')
    expect(editor.state.doc.firstChild?.attrs.level).toBe(2)
  })

  it('deleting a list item includes its nested children and undo restores it', () => {
    setup('- parent\n  - child\n- sibling')
    editor.commands.setTextSelection(3)
    const before = editor.state.doc
    expect(executeEditorCommand(editor, 'deleteParagraph')).toBe(true)
    expect(getMarkdown(editor)).not.toContain('child')
    expect(getMarkdown(editor)).toContain('sibling')
    editor.commands.undo()
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('deleting one quote paragraph preserves its sibling', () => {
    setup('> first\n>\n> second')
    editor.commands.setTextSelection(3)
    executeEditorCommand(editor, 'deleteParagraph')
    expect(getMarkdown(editor)).not.toContain('first')
    expect(getMarkdown(editor)).toContain('> second')
  })

  it('retypes an alert without nesting and explicit cancel keeps its children', () => {
    setup('> [!NOTE]\n> first\n>\n> second')
    editor.commands.setTextSelection(3)
    executeEditorCommand(editor, 'insertAlertTip')
    let alerts = 0
    editor.state.doc.descendants(node => { if (node.type.name === 'alert') alerts++ })
    expect(alerts).toBe(1)
    expect(executeEditorCommand(editor, 'cancelAlert')).toBe(true)
    expect(editor.state.doc.firstChild?.type.name).toBe('paragraph')
    expect(getMarkdown(editor)).toContain('first')
    expect(getMarkdown(editor)).toContain('second')
  })

  it.each([false, true])('rejects an outside-to-alert selection in both directions (%s)', reverse => {
    setup('outside\n\n> [!NOTE]\n> inside')
    let inside = 0
    editor.state.doc.descendants((node, pos) => { if (node.isText && node.text === 'inside') inside = pos + 2 })
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, reverse ? inside : 2, reverse ? 2 : inside)))
    const before = editor.state.doc
    expect(getEditorCommandPresentation(editor).actions.insertAlertTip?.enabled).toBe(false)
    expect(executeEditorCommand(editor, 'insertAlertTip')).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('unifies mixed bold text on both sides of a formula without marking the formula', () => {
    setup('**bold** $x$ plain')
    editor.commands.selectAll()
    executeEditorCommand(editor, 'toggleBold')
    editor.state.doc.descendants((node, _pos, parent) => {
      if (node.isText && parent?.type.name !== 'mathInline' && node.text?.trim())
        expect(node.marks.some(mark => mark.type.name === 'bold')).toBe(true)
    })
    executeEditorCommand(editor, 'toggleBold')
    editor.state.doc.descendants(node => { if (node.isText) expect(node.marks.some(mark => mark.type.name === 'bold')).toBe(false) })
  })

  it('cross-block paragraph operations are disabled but inline formatting remains enabled', () => {
    setup('first\n\nsecond')
    editor.commands.selectAll()
    const before = editor.state.doc
    expect(getEditorCommandPresentation(editor).actions.deleteParagraph?.enabled).toBe(false)
    expect(getEditorCommandPresentation(editor).actions.toggleBold?.enabled).toBe(true)
    expect(executeEditorCommand(editor, 'deleteParagraph')).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('list insertion creates a clean same-level item', () => {
    setup('- **first**\n- second')
    editor.commands.setTextSelection(4)
    executeEditorCommand(editor, 'insertLineAfter')
    const list = editor.state.doc.firstChild!
    expect(list.type.name).toBe('bulletList')
    expect(list.childCount).toBe(3)
    expect(list.child(1).firstChild?.textContent).toBe('')
    expect(editor.state.storedMarks ?? []).toEqual([])
  })

  it.each(['deleteRow', 'deleteColumn'])('cannot %s the last table dimension', command => {
    setup('| H |\n| --- |')
    let pos = 0
    editor.state.doc.descendants((node, at) => { if (!pos && node.isText) pos = at })
    editor.commands.setTextSelection(pos)
    const before = editor.state.doc
    expect(getEditorCommandPresentation(editor).actions[command]?.enabled).toBe(false)
    expect(executeEditorCommand(editor, command)).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('deleting the table header promotes the next row', () => {
    setup('| H | X |\n| --- | --- |\n| a | b |')
    let pos = 0
    editor.state.doc.descendants((node, at) => { if (!pos && node.isText) pos = at })
    editor.commands.setTextSelection(pos)
    executeEditorCommand(editor, 'deleteRow')
    const table = editor.state.doc.firstChild!
    expect(table.firstChild?.firstChild?.type.name).toBe('tableHeader')
    expect(table.firstChild?.textContent).toBe('ab')
  })

  it('cross table and body selection cannot run table operations', () => {
    setup('| H | X |\n| --- | --- |\n| a | b |\n\nbody')
    let from = 0
    let to = 0
    editor.state.doc.descendants((node, at) => {
      if (node.isText && node.text === 'a') from = at
      if (node.isText && node.text === 'body') to = at + 2
    })
    editor.commands.setTextSelection({ from, to })
    const before = editor.state.doc
    expect(getEditorCommandPresentation(editor).selectionScope).toBe('mixed')
    expect(executeEditorCommand(editor, 'addRowAfter')).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
    expect(editor.state.selection instanceof CellSelection).toBe(false)
  })

  it('ordinary text crossing columns aligns only the current cell', () => {
    setup('| H | X |\n| --- | --- |\n| a | b |')
    let from = 0
    let to = 0
    editor.state.doc.descendants((node, at) => {
      if (node.isText && node.text === 'a') from = at
      if (node.isText && node.text === 'b') to = at + 1
    })
    editor.commands.setTextSelection({ from, to })
    executeEditorCommand(editor, 'alignTableCenter')
    const alignments: (string | null)[] = []
    editor.state.doc.descendants(node => {
      if (['tableHeader', 'tableCell'].includes(node.type.name)) alignments.push(node.attrs.align)
    })
    expect(alignments).toEqual([null, 'center', null, 'center'])
    expect(editor.state.selection instanceof CellSelection).toBe(false)
  })

  it('explicit cell selection aligns every selected column without changing the selection type', () => {
    setup('| H | X |\n| --- | --- |\n| a | b |')
    const cellPositions: number[] = []
    editor.state.doc.descendants((node, position) => {
      if (node.type.name !== 'tableCell' && node.type.name !== 'tableHeader') return true
      cellPositions.push(position)
      return false
    })
    editor.view.dispatch(editor.state.tr.setSelection(
      CellSelection.create(editor.state.doc, cellPositions[0]!, cellPositions[1]!),
    ))
    executeEditorCommand(editor, 'alignTableCenter')
    editor.state.doc.descendants(node => {
      if (['tableHeader', 'tableCell'].includes(node.type.name)) expect(node.attrs.align).toBe('center')
    })
    expect(editor.state.selection instanceof CellSelection).toBe(true)
  })

  it.each(['１', '1.', 'a'])('rejects non-ASCII/hierarchy math number %s', number => {
    setup('$$\nx\n$$')
    editor.commands.setNodeSelection(0)
    const before = editor.state.doc
    expect(executeEditorCommand(editor, 'setMathNumber', number)).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('allows read-only source viewing without document or undo changes', () => {
    setup('$$\nx\n$$')
    editor.commands.setNodeSelection(0)
    editor.setEditable(false)
    const before = editor.state.doc
    const depth = undoDepth(editor.state)
    expect(executeEditorCommand(editor, 'viewMathSource')).toBe(true)
    expect((document.querySelector('.markleaf-expanded-source-editor') as HTMLElement)?.contentEditable).toBe('false')
    expect(executeEditorCommand(editor, 'deleteMath')).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
    expect(undoDepth(editor.state)).toBe(depth)
  })

  it('edits and removes a single link while retaining display text and other marks', () => {
    setup('**[linked](https://example.com)**')
    editor.commands.setTextSelection(3)
    expect(getEditorCommandPresentation(editor).linkHref).toBe('https://example.com')
    expect(executeEditorCommand(editor, 'setLink', 'https://new.example.com')).toBe(true)
    expect(editor.state.doc.textContent).toBe('linked')
    expect(executeEditorCommand(editor, 'removeLink')).toBe(true)
    expect(getMarkdown(editor)).toContain('**linked**')
    expect(getMarkdown(editor)).not.toContain('https:')
  })

  it('can edit an existing link into an internal heading anchor', () => {
    setup('**[linked](https://example.com)**')
    editor.commands.setTextSelection(3)
    expect(executeEditorCommand(editor, 'setLink', '#heading')).toBe(true)
    expect(editor.state.doc.textContent).toBe('linked')
    expect(getEditorCommandPresentation(editor).linkHref).toBe('#heading')
  })

  it('does not choose the first of mixed links', () => {
    setup('[one](https://one.example) [two](https://two.example)')
    editor.commands.selectAll()
    expect(getEditorCommandPresentation(editor).linkHref).toBeNull()
  })

  it('footnote rename rejects duplicates and never touches code examples', () => {
    setup('first[^one] second[^two]\n\n[^one]: first note\n\n[^two]: second note\n\n```text\n[^one]: code example\n```')
    const before = editor.state.doc
    expect(executeEditorCommand(editor, 'resetFootnoteLabel', JSON.stringify({ oldLabel: 'one', newLabel: 'two' }))).toBe(false)
    expect(editor.state.doc.eq(before)).toBe(true)
    expect(executeEditorCommand(editor, 'resetFootnoteLabel', JSON.stringify({ oldLabel: 'one', newLabel: 'renamed' }))).toBe(true)
    expect(getMarkdown(editor)).toContain('first[^renamed]')
    expect(editor.state.doc.lastChild?.eq(before.lastChild!)).toBe(true)
  })

  it('deleting one reference preserves the definition and other references', () => {
    setup('first[^one] second[^one]\n\n[^one]: note')
    let position: number | null = null
    editor.state.doc.descendants((node, pos) => { if (position === null && node.type.name === 'footnoteReference') position = pos })
    editor.commands.setNodeSelection(position!)
    expect(getEditorCommandPresentation(editor).footnoteReferenceLabel).toBe('one')
    expect(executeEditorCommand(editor, 'deleteFootnoteReference')).toBe(true)
    expect(getMarkdown(editor).match(/\[\^one\]/g)?.length).toBe(2)
    expect(executeEditorCommand(editor, 'goToFootnoteDefinition', 'missing')).toBe(false)
  })

  it('clears references without deleting the definition, and deletes both in one undo', () => {
    setup('first[^one] second[^one]\n\n[^one]: note')
    const before = editor.state.doc
    editor.commands.setTextSelection(before.content.size - 2)
    expect(executeEditorCommand(editor, 'clearFootnoteReferences')).toBe(true)
    expect(getMarkdown(editor).match(/\[\^one\]/g)?.length).toBe(1)
    expect(editor.commands.undo()).toBe(true)
    expect(editor.state.doc.eq(before)).toBe(true)
    editor.commands.setTextSelection(before.content.size - 2)
    expect(executeEditorCommand(editor, 'deleteFootnote')).toBe(true)
    expect(getMarkdown(editor)).not.toContain('[^one]')
    expect(editor.commands.undo()).toBe(true)
    expect(editor.state.doc.eq(before)).toBe(true)
  })

  it('converts numbered block math to inline and undo restores its number', () => {
    setup('$$\nx\n$$')
    editor.commands.setNodeSelection(0)
    expect(executeEditorCommand(editor, 'setMathNumber', '1.1')).toBe(true)
    const numbered = editor.state.doc
    expect(executeEditorCommand(editor, 'convertMath')).toBe(true)
    expect(editor.state.doc.firstChild?.firstChild?.type.name).toBe('mathInline')
    expect(editor.state.doc.firstChild?.firstChild?.attrs.number).toBeUndefined()
    expect(editor.commands.undo()).toBe(true)
    expect(editor.state.doc.eq(numbered)).toBe(true)
  })

  it('read-only Mermaid re-render never converts code blocks or changes undo history', () => {
    setup('```mermaid\ngraph TD; A-->B\n```')
    editor.setEditable(false)
    const before = editor.state.doc
    const depth = undoDepth(editor.state)
    expect(executeEditorCommand(editor, 'rerenderAllMermaid')).toBe(true)
    expect(editor.state.doc.eq(before)).toBe(true)
    expect(undoDepth(editor.state)).toBe(depth)
  })
})
