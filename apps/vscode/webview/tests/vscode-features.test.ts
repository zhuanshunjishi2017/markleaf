import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, setBlockHandleVisible, setMarkdownEditingSettings } from '@markleaf/editor-core'
import { createFindBar } from '../src/vscode-find'
import { createReadingView } from '../src/vscode-reading'
import { defaultSettings } from '../src/vscode-settings'

const cleanup: Array<() => void> = []
function setup(markdown: string) {
  document.body.innerHTML = '<div id="app"><div id="editor-chrome"><div id="toolbar"><button data-command="formatPainter"></button></div></div><main id="editor"></main><footer><span id="count"></span></footer></div>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
  editor.view.setProps({ handleScrollToSelection: () => true })
  cleanup.push(() => editor.destroy())
  return { editor, mount }
}
afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  setMarkdownEditingSettings({})
  document.body.innerHTML = ''
  vi.restoreAllMocks()
})

describe('completed rendered editing features', () => {
  it('runs find/replace UI without writing on find and prevents replacement while reading', () => {
    const { editor } = setup('Hello hello shelloworld')
    const bar = createFindBar(editor, document.querySelector('#toolbar')!)
    cleanup.push(bar.dispose)
    const changed = vi.fn()
    editor.on('update', changed)
    bar.open(true)
    const query = document.querySelector<HTMLInputElement>('#find-input')!
    query.value = 'hello'
    query.dispatchEvent(new InputEvent('input', { bubbles: true }))
    expect(document.querySelector('#find-result')?.textContent).toBe('1 / 3')
    const whole = document.querySelector<HTMLInputElement>('#find-whole')!
    whole.checked = true; whole.dispatchEvent(new Event('input'))
    expect(document.querySelector('#find-result')?.textContent).toBe('1 / 2')
    expect(changed).not.toHaveBeenCalled()
    const replacement = document.querySelector<HTMLInputElement>('#replace-input')!
    replacement.value = 'leaf'
    editor.setEditable(false, false); bar.update()
    expect(document.querySelector<HTMLButtonElement>('#replace-all')?.disabled).toBe(true)
    document.querySelector<HTMLButtonElement>('#replace-all')!.click()
    expect(editor.state.doc.textContent).toBe('Hello hello shelloworld')
    editor.setEditable(true, false); bar.update()
    document.querySelector<HTMLButtonElement>('#replace-all')!.click()
    expect(editor.state.doc.textContent).toBe('leaf leaf shelloworld')
    expect(changed).toHaveBeenCalledTimes(1)
  })

  it('applies typography, outline, spacing and focus without changing the document', () => {
    const { editor, mount } = setup('# Same\n\ntext\n\n###### Same')
    const reading = createReadingView(editor, mount, document.querySelector('#count')!)
    cleanup.push(reading.dispose)
    const original = editor.state.doc
    const changed = vi.fn()
    editor.on('update', changed)
    reading.apply({ ...defaultSettings, showOutline: true, typography: 'serif', colorTheme: 'apple-dark', focusMode: true, fontSize: 20, zoom: 125, bulletMarker: 'plus' })
    expect(editor.state.doc).toBe(original)
    expect(changed).not.toHaveBeenCalled()
    expect(mount.classList.contains('markleaf-style-serif')).toBe(true)
    expect(document.documentElement.style.getPropertyValue('--ml-font-size')).toBe('25px')
    const headings = Array.from(document.querySelectorAll<HTMLButtonElement>('#outline button'))
    expect(headings.map(heading => heading.textContent)).toEqual(['Same', 'Same'])
    expect(headings[0]?.dataset.position).not.toBe(headings[1]?.dataset.position)
    expect(mount.classList.contains('markleaf-editor-focus-mode')).toBe(true)
    editor.setEditable(false, false); reading.update()
    expect(mount.classList.contains('markleaf-editor-focus-mode')).toBe(false)
    expect(changed).not.toHaveBeenCalled()
    setBlockHandleVisible(editor, true)
  })

  it('retains the original print layout in derived typography styles', () => {
    const { editor, mount } = setup('# Title\n\ntext')
    const reading = createReadingView(editor, mount, document.querySelector('#count')!)
    cleanup.push(reading.dispose)
    const original = editor.state.doc
    for (const typography of ['print-double', 'latex', 'retro-print'] as const) {
      reading.apply({ ...defaultSettings, typography })
      expect(getComputedStyle(mount).paddingTop).toBe('44px')
      expect(getComputedStyle(editor.view.dom.querySelector('h1')!).textAlign).toBe('center')
    }
    reading.apply(defaultSettings)
    expect(mount.classList.contains('markleaf-style-print')).toBe(false)
    expect(editor.state.doc).toBe(original)
  })


})
