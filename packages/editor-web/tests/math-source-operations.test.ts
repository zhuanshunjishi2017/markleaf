import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, expandSourceEditor, exportEditorSelection, getMarkdown, setCodeHighlightVisible } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []
afterEach(() => {
  editors.splice(0).forEach(editor => editor.destroy())
  document.body.innerHTML = ''
  window.getSelection()?.removeAllRanges()
  vi.restoreAllMocks()
})
function open(source = 'abcdef') {
  const mount = document.body.appendChild(document.createElement('div'))
  const editor = createEditor(mount, `$$${source}$$`)
  editors.push(editor)
  expandSourceEditor(editor, 0, 'mathBlock')
  const code = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
  code.focus()
  return { editor, code }
}
function select(code: HTMLElement, anchor: number, focus: number) {
  window.getSelection()!.setBaseAndExtent(code.firstChild!, anchor, code.firstChild!, focus)
}
function offsets(code: HTMLElement) {
  const selection = window.getSelection()!
  const range = document.createRange()
  range.selectNodeContents(code)
  range.setEnd(selection.anchorNode!, selection.anchorOffset)
  const anchor = range.toString().length
  range.setEnd(selection.focusNode!, selection.focusOffset)
  return [anchor, range.toString().length]
}

describe('floating formula source editing', () => {
  for (const [anchor, focus] of [[3, 3], [0, 0], [6, 6], [1, 4], [4, 1]]) {
    for (const key of ['Home', 'End']) for (const shiftKey of [false, true]) {
      it(`${key} shift=${shiftKey} from anchor=${anchor}, focus=${focus}`, () => {
        const { code } = open()
        select(code, anchor!, focus!)
        const event = new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true })
        code.dispatchEvent(event)
        const destination = key === 'Home' ? 0 : 6
        expect(offsets(code)).toEqual([shiftKey ? anchor : destination, destination])
        expect(event.defaultPrevented).toBe(true)
      })
    }
  }
  it('uses whole multiline formula boundaries and leaves Cmd+arrows to macOS', () => {
    const { code } = open('abc\ndef')
    select(code, 5, 5)
    code.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }))
    expect(offsets(code)).toEqual([0, 0])
    for (const key of ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']) {
      const event = new KeyboardEvent('keydown', { key, metaKey: true, bubbles: true, cancelable: true })
      code.dispatchEvent(event)
      expect(event.defaultPrevented).toBe(false)
    }
  })
  it.each([[1, 4], [4, 1]])('preserves highlight selection direction %i -> %i', (anchor, focus) => {
    const { code, editor } = open()
    setCodeHighlightVisible(editor, true)
    select(code, anchor, focus)
    code.dispatchEvent(new Event('input', { bubbles: true }))
    expect(offsets(code)).toEqual([anchor, focus])
  })
  it('exports only the partial source selection without focusing or collapsing it', () => {
    const { code, editor } = open()
    select(code, 4, 1)
    const focus = vi.spyOn(code, 'focus')
    expect(exportEditorSelection(editor)).toEqual({ text: 'bcd', markdown: 'bcd', html: '' })
    expect(offsets(code)).toEqual([4, 1])
    expect(focus).not.toHaveBeenCalled()
  })
  it('does not copy the whole formula when its source caret has no selection', () => {
    const { code, editor } = open()
    select(code, 3, 3)
    expect(exportEditorSelection(editor)).toEqual({ text: '', markdown: '', html: '' })
  })
  it('commits Ctrl+Enter and restores editor focus', () => {
    const { code, editor } = open()
    code.textContent = 'changed'
    code.dispatchEvent(new Event('input', { bubbles: true }))
    editor.view.coordsAtPos = () => ({ left: 0, top: 0, right: 0, bottom: 0 })
    const event = new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true, cancelable: true })
    code.dispatchEvent(event)
    expect(event.defaultPrevented).toBe(true)
    expect(document.querySelector('.markleaf-expanded-source-exit')).not.toBeNull()
    expect(getMarkdown(editor)).toContain('$$changed$$')
  })
})
