import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getMarkdown, getEditorCommandState, exportEditorSelection } from '../src/index'

if (!globalThis.ClipboardEvent) globalThis.ClipboardEvent = class extends Event {} as unknown as typeof ClipboardEvent

const editors: ReturnType<typeof createEditor>[] = []
const source = '| A | B |\n| --- | --- |\n| first | second |\n| third | fourth |'
const layout = { version: 1, shape: [2, 2, 2], width: 300, columns: [100, 200], rows: { '2': [150, 150] } }
function make(text = source, readonly = false) {
  const host = document.createElement('div'); document.body.append(host)
  const editor = createEditor(host, text, readonly); editors.push(editor)
  const positions: number[] = []
  editor.state.doc.descendants((n, pos) => { if (['tableCell', 'tableHeader'].includes(n.type.name)) positions.push(pos) })
  if (positions[2] != null) editor.commands.setTextSelection(positions[2] + 2)
  return { editor, host, positions }
}
afterEach(() => { editors.splice(0).forEach(e => e.destroy()); document.body.innerHTML = '' })

describe('persistent table widths', () => {
  it('loads row overrides and round trips a layout comment with its table', () => {
    const { editor, host } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
    const table = editor.state.doc.firstChild!
    expect(table.type.name).toBe('table')
    expect(table.attrs.tableLayout).toEqual(layout)
    expect(host.querySelector('table')?.getAttribute('data-markleaf-table-layout')).toBeTruthy()
    const reopened = make(getMarkdown(editor)).editor
    expect(reopened.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
  })
  it('does not interpret a metadata comment in a code fence', () => {
    const { editor } = make('```md\n<!-- markleaf:table-layout {} -->\n' + source + '\n```')
    expect(editor.state.doc.firstChild!.type.name).toBe('codeBlock')
    expect(getMarkdown(editor)).toContain('markleaf:table-layout {}')
  })
  it('preserves malformed or structurally stale metadata without applying it', () => {
    for (const raw of ['{"version":99}', '{bad}', JSON.stringify({ ...layout, shape: [2, 2] })]) {
      const { editor } = make(`<!-- markleaf:table-layout ${raw} -->\n${source}`)
      expect(editor.state.doc.firstChild!.type.name).toBe('table')
      expect(editor.state.doc.firstChild!.attrs.tableLayout).toBeNull()
      expect(getMarkdown(editor)).toContain(`<!-- markleaf:table-layout ${raw} -->`)
    }
  })
  it('distributes, resets and undoes widths without changing the text', () => {
    const { editor } = make()
    const text = editor.state.doc.textContent
    expect(executeEditorCommand(editor, 'distributeTableColumns')).toBe(true)
    expect(editor.state.doc.firstChild!.attrs.tableLayout.columns[0]).toBe(editor.state.doc.firstChild!.attrs.tableLayout.columns[1])
    expect(executeEditorCommand(editor, 'resetTableWidths')).toBe(true)
    expect(getMarkdown(editor)).not.toContain('table-layout')
    editor.commands.undo()
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toBeTruthy()
    expect(editor.state.doc.textContent).toBe(text)
  })
  it('supports content fitting without a special cell-selection command', () => {
    const { editor } = make()
    expect(executeEditorCommand(editor, 'fitTableContents')).toBe(true)
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toBeTruthy()
  })
  it('disables width commands when readonly and for spanned HTML tables', () => {
    const { editor } = make(source, true)
    expect(executeEditorCommand(editor, 'distributeTableColumns')).toBe(false)
    const other = make().editor
    other.commands.setContent('<table><tr><th colspan="2">A</th></tr><tr><td>B</td><td>C</td></tr></table>')
    other.commands.setTextSelection(3)
    expect(getEditorCommandState(other).canResizeTable).toBe(false)
    expect(executeEditorCommand(other, 'fitTableContents')).toBe(false)
  })
  it('updates row override indices for a keyboard/API row insertion', () => {
    const { editor } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
    editor.commands.addRowBefore()
    const changed = editor.state.doc.firstChild!.attrs.tableLayout
    expect(changed.shape).toEqual([2, 2, 2, 2])
    expect(changed.rows['3']).toEqual([150, 150])
    expect(make(getMarkdown(editor)).editor.state.doc.firstChild!.attrs.tableLayout).toEqual(changed)
  })
  it('retains layout in whole-table HTML but not an unrelated text selection', () => {
    const { editor, positions } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
    editor.commands.setCellSelection({ anchorCell: positions[0]!, headCell: positions[5]! })
    expect(exportEditorSelection(editor).html).toContain('data-markleaf-table-layout')
    editor.commands.setTextSelection({ from: positions[2]! + 2, to: positions[2]! + 4 })
    expect(exportEditorSelection(editor).html).not.toContain('data-markleaf-table-layout')
  })
})

it('preserves local widths and logical columns after API insert/delete column and undo', () => {
  const { editor } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
  editor.commands.addColumnAfter()
  let current = editor.state.doc.firstChild!.attrs.tableLayout
  expect(current.shape).toEqual([3, 3, 3])
  expect(current.rows['2']).toHaveLength(3)
  expect(current.columns.every((n: number) => n >= 40)).toBe(true)
  editor.commands.undo()
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
  editor.commands.redo()
  editor.commands.deleteColumn()
  current = editor.state.doc.firstChild!.attrs.tableLayout
  expect(current.shape).toEqual([2, 2, 2])
  expect(make(getMarkdown(editor)).editor.state.doc.firstChild!.attrs.tableLayout).toEqual(current)
})
it('expands a minimum-width table when inserting a column and retains its content', () => {
  const narrow = { version: 1, shape: [2, 2, 2], width: 80, columns: [40, 40], rows: {} }
  const { editor } = make(`<!-- markleaf:table-layout ${JSON.stringify(narrow)} -->\n${source}`)
  editor.commands.addColumnAfter()
  expect(editor.state.doc.firstChild!.attrs.tableLayout.width).toBe(120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout.columns).toEqual([40, 40, 40])
  expect(editor.state.doc.textContent).toContain('first')
})
it('keeps layout across HTML copy and paste, including caption and inline marks', () => {
  const { editor } = make(`> tablecaption: Width test\n\n<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
  const reopened = make(getMarkdown(editor)).editor
  expect(reopened.state.doc.firstChild!.attrs.caption).toBe('Width test')
  expect(reopened.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
  const pasted = make().editor
  expect(executeEditorCommand(pasted, 'pasteHtml', editor.getHTML())).toBe(true)
  const tableNodes: any[] = []
  pasted.state.doc.descendants(n => { if (n.type.name === 'table') tableNodes.push(n) })
  expect(tableNodes.some(n => JSON.stringify(n.attrs.tableLayout) === JSON.stringify(layout))).toBe(true)
})
it('whole-cell clipboard produces one table, and a partial cell selection omits unrelated widths', () => {
  const { editor, positions } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
  editor.commands.setCellSelection({ anchorCell: positions[0]!, headCell: positions[5]! })
  const whole = exportEditorSelection(editor)
  const dom = new DOMParser().parseFromString(whole.html, 'text/html')
  expect(dom.querySelectorAll('table')).toHaveLength(1)
  expect(whole.markdown).toContain('markleaf:table-layout')
  const pasted = make('').editor
  executeEditorCommand(pasted, 'pasteHtml', whole.html)
  expect(pasted.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
  editor.commands.setCellSelection({ anchorCell: positions[2]!, headCell: positions[3]! })
  expect(exportEditorSelection(editor).html).not.toContain('data-markleaf-table-layout')
})

it('preserves oversized metadata as raw source without applying it', () => {
  const raw = JSON.stringify({ version: 99, extra: 'x'.repeat(66000) })
  const { editor } = make(`<!-- markleaf:table-layout ${raw} -->\n${source}`)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toBeNull()
  expect(getMarkdown(editor)).toContain(`<!-- markleaf:table-layout ${raw} -->`)
})
it('shares the row projection in HTML/PDF exports and allows page fitting', async () => {
  const { generateExportHtml } = await import('../src/export-html')
  const { editor } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
  const output = await generateExportHtml({ rawBodyHtml: editor.getHTML(), resolved: { rootClass: 'markleaf-document', css: '' }, format: 'pdf', baseCss: '', editorLoc: {} })
  const dom = new DOMParser().parseFromString(output, 'text/html')
  const table = dom.querySelector('table')!
  expect(table.style.minWidth).toBe('0')
  expect(table.rows[0]!.style.gridTemplateColumns).toContain('100fr')
  expect(table.rows[2]!.style.gridTemplateColumns).toContain('150fr')
  expect(table.rows).toHaveLength(3)
  expect(dom.querySelectorAll('td,th')).toHaveLength(6)
})
it('fits fixed-size images at baseline width independently of native font zoom', () => {
  const descriptor = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth')!
  Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { configurable: true, get() {
    const el = this as HTMLElement
    if (el.tagName === 'IMG') return 200
    if (['TD', 'TH'].includes(el.tagName)) {
      const font = Number.parseFloat(getComputedStyle(el.closest('.markleaf-document')!).fontSize) || 16
      return (el.querySelector('img') ? 200 : font * 5) + 20
    }
    return 0
  } })
  try {
    const widths: number[][] = []
    for (const scale of [.8, 1, 2]) {
      const { editor } = make('| Image | Text |\n| --- | --- |\n| ![probe](image.png "markleaf:width=200;height=50;rotation=0") | hello |')
      editor.view.dom.style.fontSize = `${16 * scale}px`
      editor.view.dom.style.setProperty('--ml-table-scale', String(scale))
      executeEditorCommand(editor, 'fitTableContents')
      widths.push(editor.state.doc.firstChild!.attrs.tableLayout.columns)
    }
    expect(widths[0]).toEqual(widths[1])
    expect(widths[2]).toEqual(widths[1])
    expect(widths[1]![0]).toBeGreaterThanOrEqual(220)
  } finally { Object.defineProperty(HTMLElement.prototype, 'offsetWidth', descriptor) }
})
it('maintains layout when Tab adds a row at the final cell', () => {
  const { editor, positions } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
  editor.commands.setTextSelection(positions[5]! + 2)
  editor.commands.keyboardShortcut('Tab')
  const current = editor.state.doc.firstChild!.attrs.tableLayout
  expect(current.shape).toEqual([2, 2, 2, 2])
  expect(current.rows['2']).toEqual(layout.rows['2'])
  expect(current.rows['3']).toBeUndefined()
  editor.commands.undo()
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
})
it('maintains widths when a rectangular paste expands the selected table', () => {
  const { editor, positions } = make(`<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n${source}`)
  editor.commands.setTextSelection(positions[5]! + 2)
  editor.view.pasteHTML('<table><tr><td>new A</td><td>new B</td></tr><tr><td>new C</td><td>new D</td></tr></table>')
  const current = editor.state.doc.firstChild!.attrs.tableLayout
  expect(current.shape).toEqual([3, 3, 3, 3])
  expect(current.columns).toHaveLength(3)
  expect(make(getMarkdown(editor)).editor.state.doc.firstChild!.attrs.tableLayout).toEqual(current)
  expect(editor.state.doc.textContent).toContain('new D')
  editor.commands.undo()
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
})
