import { afterEach, expect, it, vi } from 'vitest'
import { createEditor } from '../src/editor'
import { getActiveOutlinePosition, getDocumentOutline } from '../src/outline'
import type { Editor } from '@tiptap/core'

let editor: Editor | undefined
afterEach(() => { editor?.destroy(); editor = undefined; document.body.replaceChildren(); vi.restoreAllMocks() })

function setup(markdown: string) {
  const mount = document.createElement('div')
  document.body.append(mount)
  editor = createEditor(mount, markdown)
  editor.view.setProps({ handleScrollToSelection: () => true })
  return editor
}

it('reuses the heading index for selection changes and invalidates it after edits', () => {
  const editor = setup('Intro\n\n# Same\n\ntext\n\n### Same\n\ntext')
  const headings = getDocumentOutline(editor)
  const original = editor.state.doc
  const traversal = vi.spyOn(editor.state.doc, 'descendants')
  expect(getActiveOutlinePosition(editor, 'cursor')).toBeNull()
  editor.commands.setTextSelection(headings[1]!.position + 1)
  expect(editor.state.doc).toBe(original)
  // Other selection plugins also traverse the document. Measure the outline
  // query itself after that transaction has completed.
  traversal.mockClear()
  expect(getActiveOutlinePosition(editor, 'cursor')).toBe(headings[1]!.position)
  expect(traversal).not.toHaveBeenCalled()
  headings[0]!.text = 'changed by consumer'
  expect(getDocumentOutline(editor)[0]!.text).toBe('Same')
  editor.commands.setContent('# New\n\n## Child', { contentType: 'markdown' })
  expect(getDocumentOutline(editor).map(heading => heading.text)).toEqual(['New', 'Child'])
})

it('locates headings in a long document with bounded live geometry reads', () => {
  const editor = setup(Array.from({ length: 512 }, (_, index) => `## Heading ${index}\n\ntext`).join('\n\n'))
  const headings = getDocumentOutline(editor)
  const elements = [...editor.view.dom.querySelectorAll('h2')]
  let offset = 300 * 1000
  const geometry = vi.fn((index: number) => ({ top: index * 1000 - offset } as DOMRect))
  elements.forEach((element, index) => vi.spyOn(element, 'getBoundingClientRect').mockImplementation(() => geometry(index)))
  expect(getActiveOutlinePosition(editor, 'scroll', 48)).toBe(headings[300]!.position)
  expect(geometry.mock.calls.length).toBeLessThanOrEqual(10)
  geometry.mockClear()
  offset = 45 * 1000
  expect(getActiveOutlinePosition(editor, 'scroll', 48)).toBe(headings[45]!.position)
  expect(geometry.mock.calls.length).toBeLessThanOrEqual(10)
  offset = -500
  expect(getActiveOutlinePosition(editor, 'scroll', 48)).toBe(headings[0]!.position)
  offset = 600 * 1000
  expect(getActiveOutlinePosition(editor, 'scroll', 48)).toBe(headings.at(-1)!.position)
})

it('returns no active heading for a document without headings', () => {
  const editor = setup('Only a paragraph')
  expect(getDocumentOutline(editor)).toEqual([])
  expect(getActiveOutlinePosition(editor, 'scroll')).toBeNull()
  expect(getActiveOutlinePosition(editor, 'cursor')).toBeNull()
})

it('ignores an unmounted heading without losing later visible headings', () => {
  const editor = setup('# First\n\n# Middle\n\n# Last')
  const headings = getDocumentOutline(editor)
  const original = editor.view.nodeDOM.bind(editor.view)
  vi.spyOn(editor.view, 'nodeDOM').mockImplementation(position => position === headings[1]!.position ? null : original(position))
  expect(getActiveOutlinePosition(editor, 'scroll')).toBe(headings[2]!.position)
})
