import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function insertTable(text?: string) {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, '')
  editors.push(editor)
  executeEditorCommand(editor, 'insertTable', text)
  return editor.state.doc.firstChild
}

describe('insert table command dimensions', () => {
  it('inserts the selected rows and columns with a header row', () => {
    const table = insertTable('7,8')

    expect(table?.type.name).toBe('table')
    expect(table?.childCount).toBe(7)
    expect(table?.firstChild?.type.name).toBe('tableRow')
    expect(table?.firstChild?.childCount).toBe(8)
    expect(table?.firstChild?.firstChild?.type.name).toBe('tableHeader')
  })

  it('falls back to three by three for missing or invalid dimensions', () => {
    for (const text of [undefined, 'bad', '0,2', '3,4,5']) {
      const table = insertTable(text)

      expect(table?.childCount).toBe(3)
      expect(table?.firstChild?.childCount).toBe(3)
    }
  })
})
