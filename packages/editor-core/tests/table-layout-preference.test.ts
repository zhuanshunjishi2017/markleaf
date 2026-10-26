import { afterEach, expect, it } from 'vitest'
import { createEditor, getMarkdown, getEditorCommandState, executeEditorCommand } from '../src/index'
import { clearAllTableLayouts, setTableLayoutEnabled } from '../src/table-layout-extension'
import { SourceEditor } from '../src/source-editor'

const metadata = '<!-- markleaf:table-layout {"version":1,"shape":[2,2],"width":300,"columns":[100,200],"rows":{}} -->'
const table = '| A | B |\n| --- | --- |\n| one | two |'
const document = `${metadata}\n${table}\n\n${metadata}\n${table}`
const editors: ReturnType<typeof createEditor>[] = []
function setup(readOnly = false) {
  const host = window.document.createElement('div'); window.document.body.append(host)
  const editor = createEditor(host, document, readOnly); editors.push(editor)
  editor.commands.setTextSelection(3)
  return { editor, host }
}
afterEach(() => { editors.splice(0).forEach(e => e.destroy()); window.document.body.innerHTML = '' })
it('disables projection and resize commands without clearing document metadata', () => {
  const { editor, host } = setup()
  const before = getMarkdown(editor)
  setTableLayoutEnabled(editor, false)
  expect(host.querySelectorAll('table.markleaf-table-manual')).toHaveLength(0)
  expect(getEditorCommandState(editor).canResizeTable).toBe(false)
  expect(executeEditorCommand(editor, 'fitTableContents')).toBe(false)
  expect(getMarkdown(editor)).toBe(before)
  setTableLayoutEnabled(editor, true)
  expect(host.querySelectorAll('table.markleaf-table-manual')).toHaveLength(2)
  expect(getEditorCommandState(editor).canResizeTable).toBe(true)
})
it('clears all table metadata including stale raw values as one undoable change', () => {
  const { editor } = setup()
  const second = editor.state.doc.child(0).nodeSize
  editor.view.dispatch(editor.state.tr.setNodeMarkup(second, undefined, { ...editor.state.doc.child(1).attrs, tableLayout: null, tableLayoutRaw: '{stale}' }))
  const before = getMarkdown(editor)
  setTableLayoutEnabled(editor, false)
  expect(clearAllTableLayouts(editor)).toBe(true)
  expect(getMarkdown(editor)).not.toContain('markleaf:table-layout')
  editor.commands.undo()
  expect(getMarkdown(editor)).toBe(before)
  expect(editor.view.dom.querySelector('table.markleaf-table-manual')).toBeNull()
})
it('does not edit a read-only document or create history when no metadata exists', () => {
  const { editor } = setup(true)
  const before = getMarkdown(editor)
  expect(clearAllTableLayouts(editor)).toBe(false)
  expect(getMarkdown(editor)).toBe(before)
  const ordinary = createEditor(window.document.createElement('div'), table); editors.push(ordinary)
  expect(clearAllTableLayouts(ordinary)).toBe(false)
  expect(ordinary.can().undo()).toBe(false)
})
it('source cleanup preserves fenced examples, unrelated comments and formatting, with one undo', () => {
  const host = window.document.createElement('div'); window.document.body.append(host)
  const source = `#  title\n\n\`\`\`md\n${metadata}\n${table}\n\`\`\`\n\n<!-- unrelated -->\n${metadata}\n${table}\n\n<!-- markleaf:table-layout {stale} -->\n${table}`
  const editor = new SourceEditor(host, source, () => {})
  try {
    expect(editor.clearTableLayouts()).toBe(true)
    expect(editor.getText()).toBe(source.replace(`<!-- unrelated -->\n${metadata}`, '<!-- unrelated -->\n').replace('<!-- markleaf:table-layout {stale} -->', ''))
    expect(editor.undo()).toBe(true)
    expect(editor.getText()).toBe(source)
    editor.setReadOnly(true)
    expect(editor.clearTableLayouts()).toBe(false)
    expect(editor.getText()).toBe(source)
  } finally { editor.destroy() }
})
it('exports automatic table layout when the global feature is disabled', async () => {
  const { editor } = setup()
  const { generateExportHtml } = await import('../src/export-html')
  const html = await generateExportHtml({ rawBodyHtml: editor.getHTML(), resolved: { rootClass: 'markleaf-document', css: '' }, format: 'html', baseCss: '', editorLoc: {}, allowTableColumnResize: false })
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  expect(parsed.querySelector('table.markleaf-table-manual')).toBeNull()
  expect(parsed.querySelector('tr')!.style.gridTemplateColumns).toBe('')
})
