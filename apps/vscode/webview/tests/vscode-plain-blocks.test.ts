import { afterEach, expect, it } from 'vitest'
import { createEditor, getMarkdown } from '@markleaf/editor-core'
import { applyPlainSourceEdit, sourceForPlainEdit } from '../src/vscode-plain-blocks'

const open: ReturnType<typeof createEditor>[] = []
function editor(source: string) {
  const element = document.createElement('div')
  document.body.append(element)
  const value = createEditor(element, source)
  open.push(value)
  return value
}
afterEach(() => { open.splice(0).forEach(e => e.destroy()); document.body.replaceChildren() })

it('updates the correct duplicate paragraph and preserves the other source bytes on a local edit', () => {
  const before = '# Heading\r\n\r\nhello world\r\n\r\nhello world\r\n'
  const after = before.replace('hello world', 'hello everyone')
  const view = editor(before)
  expect(applyPlainSourceEdit(view, before, after)).toBe(true)
  expect(getMarkdown(view)).toBe(getMarkdown(editor(after)))
  const pos = view.state.doc.content.size - 1
  const tr = view.state.tr.insertText('界', pos)
  expect(sourceForPlainEdit(view, tr, after)).toBe(after.replace('hello world', 'hello world界'))
})

it.each([
  ['hello', '# hello'], ['hello', '1. hello'], ['hello', '**hello**'],
  ['hello', '[a]: https://example.com'], ['hello', 'hello\n\nsecond'],
  ['# Intro\n\n`hello`', '# Intro\n\n`new`'],
])('defers structural or marked edits to the full Markdown parser', (before, after) => {
  const view = editor(before), old = view.state.doc
  expect(applyPlainSourceEdit(view, before, after)).toBe(false)
  expect(view.state.doc).toBe(old)
})
