import { afterEach, expect, it, vi } from 'vitest'
import { createEditor, getDocumentOutline } from '@markleaf/editor-core'
import { createOutlineView } from '../src/vscode-outline'

const cleanup: Array<() => void> = []
afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  vi.restoreAllMocks(); vi.useRealTimers(); document.body.replaceChildren()
  document.documentElement.scrollTop = 0
})

function setup(markdown = '# Root\n\ntext\n\n#### Deep\n\ntext\n\n## Same\n\ntext\n\n# Same\n\ntext') {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown)
  editor.view.setProps({ handleScrollToSelection: () => true })
  const outline = createOutlineView(editor, () => 48)
  document.body.prepend(outline.element)
  cleanup.push(() => editor.destroy(), outline.dispose)
  outline.setVisible(true)
  const rows = () => [...outline.element.querySelectorAll<HTMLElement>('[role="treeitem"]')]
  const visible = () => rows().filter(row => !row.hidden)
  const search = outline.element.querySelector<HTMLInputElement>('input')!
  const action = (name: string) => outline.element.querySelector<HTMLButtonElement>(`[data-outline-action="${name}"]`)!.click()
  return { editor, outline, rows, visible, search, action }
}

it('uses Windows parent-stack hierarchy, keeps duplicate positions and exposes full titles', () => {
  const { rows, visible, action, editor } = setup()
  const original = editor.state.doc
  expect(rows().map(row => row.getAttribute('aria-level'))).toEqual(['1', '2', '2', '1'])
  expect(rows().map(row => row.title)).toEqual(['Root', 'Deep', 'Same', 'Same'])
  expect(new Set(rows().map(row => row.dataset.position)).size).toBe(4)
  action('collapse')
  expect(visible().map(row => row.textContent)).toEqual(['Root', 'Same'])
  action('expand')
  expect(visible()).toHaveLength(4)
  rows()[0]!.querySelector<HTMLElement>('.outline-disclosure')!.click()
  expect(visible()).toHaveLength(2)
  expect(editor.state.doc).toBe(original)
})

it('searches all headings including collapsed descendants and reports empty matches', () => {
  const { visible, search, action, outline } = setup()
  action('collapse')
  search.value = 'dEeP'; search.dispatchEvent(new Event('input'))
  expect(visible().map(row => row.textContent)).toEqual(['Deep'])
  expect(visible()[0]!.getAttribute('aria-level')).toBe('1')
  search.value = 'not present'; search.dispatchEvent(new Event('input'))
  expect(visible()).toHaveLength(0)
  expect(outline.element.querySelector('.outline-empty')?.textContent).toBe('没有匹配的标题')
  search.value = ''; search.dispatchEvent(new Event('input'))
  expect(visible()).toHaveLength(2)
})

it('locates a hidden active heading by expanding its ancestors without changing the document', () => {
  const { rows, action, editor, outline } = setup()
  const original = editor.state.doc
  const position = getDocumentOutline(editor)[1]!.position
  editor.commands.setTextSelection(position + 1)
  outline.updateCurrent('cursor')
  action('collapse')
  expect(rows()[1]!.hidden).toBe(true)
  action('locate')
  expect(rows()[1]!.hidden).toBe(false)
  expect(document.activeElement).toBe(rows()[1])
  expect(rows()[1]!.getAttribute('aria-current')).toBe('true')
  expect(editor.state.doc).toBe(original)
})

it('returns from search on activation and protects the clicked heading from a stale cursor', () => {
  vi.useFakeTimers()
  vi.spyOn(window, 'requestAnimationFrame').mockReturnValue(0)
  const { editor, outline, search, visible, rows } = setup()
  const original = editor.state.doc
  search.value = 'deep'; search.dispatchEvent(new Event('input'))
  visible()[0]!.click()
  expect(search.value).toBe('')
  expect(visible()).toHaveLength(4)
  outline.updateCurrent('cursor')
  expect(rows()[1]!.getAttribute('aria-current')).toBe('true')
  vi.advanceTimersByTime(800)
  expect(rows()[1]!.getAttribute('aria-current')).toBe('true')
  expect(editor.state.doc).toBe(original)
})

it('moves focus with keys and expands or collapses branches without activating headings', () => {
  const { rows, outline } = setup()
  const key = (row: HTMLElement, key: string) => row.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
  rows()[0]!.focus()
  key(rows()[0]!, 'ArrowLeft')
  expect(rows()[1]!.hidden).toBe(true)
  key(rows()[0]!, 'ArrowRight')
  expect(rows()[1]!.hidden).toBe(false)
  key(rows()[0]!, 'ArrowDown')
  expect(document.activeElement).toBe(rows()[1])
  key(rows()[1]!, 'End')
  expect(document.activeElement).toBe(rows()[3])
  expect(outline.element.querySelector('[aria-current="true"]')).toBe(rows()[0])
})

it('scrolls only the heading list when the current visible row changes', () => {
  const { rows, editor, outline } = setup()
  const list = outline.element.querySelector<HTMLElement>('#outline-list')!
  Object.defineProperty(list, 'clientHeight', { value: 50 })
  Object.defineProperty(rows()[3], 'offsetTop', { value: 200 })
  Object.defineProperty(rows()[3], 'offsetHeight', { value: 28 })
  const writes = vi.spyOn(Element.prototype, 'setAttribute')
  editor.commands.setTextSelection(getDocumentOutline(editor)[3]!.position + 1)
  outline.updateCurrent('cursor')
  expect(list.scrollTop).toBe(178)
  expect(document.documentElement.scrollTop).toBe(0)
  writes.mockClear()
  outline.updateCurrent('cursor')
  expect(writes.mock.calls.filter(([name]) => name === 'aria-current')).toHaveLength(0)
})

it('refreshes search results after editing and detaches on disposal', () => {
  const { rows, editor, outline, search, visible } = setup()
  search.value = 'new'; search.dispatchEvent(new Event('input'))
  editor.commands.setContent('# New title', { contentType: 'markdown' })
  expect(rows()).toHaveLength(1)
  expect(visible()[0]!.title).toBe('New title')
  outline.dispose()
  editor.commands.setContent('# Later', { contentType: 'markdown' })
  expect(rows()[0]!.title).toBe('New title')
})

it('shows the empty-document state with no selectable heading', () => {
  const { rows, outline } = setup('Just text')
  expect(rows()).toHaveLength(0)
  expect(outline.element.querySelector<HTMLElement>('.outline-empty')!.hidden).toBe(false)
  expect(outline.element.querySelector<HTMLButtonElement>('[data-outline-action="locate"]')!.disabled).toBe(true)
})
