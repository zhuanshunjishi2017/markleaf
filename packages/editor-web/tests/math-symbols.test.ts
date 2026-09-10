import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, expandSourceEditor } from '../src/editor'
const editors: ReturnType<typeof createEditor>[] = []
afterEach(() => {
  editors.splice(0).forEach(editor => editor.destroy())
  document.body.innerHTML = ''
  window.getSelection()?.removeAllRanges()
})
function open() {
  const editor = createEditor(document.body.appendChild(document.createElement('div')), '$$xy$$')
  editors.push(editor)
  expandSourceEditor(editor, 0, 'mathBlock')
  const code = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
  code.focus()
  return { editor, code }
}
const literals = [
  ['ℏ', '\\hbar'], ['∫', '\\int_{-\\infty}^{+\\infty}'],
  ['∫∫', '\\iint'], ['∫∫∫', '\\iiint'], ['∮', '\\oint'], ['∯', '\\oiint'], ['∰', '\\oiiint'],
  ['⋯', '\\cdots'], ['⋮', '\\vdots'], ['⋱', '\\ddots'], ['⋅', '\\cdot'],
]
const templates = [
  ['⟨x⟩', '\\langle ', ' \\rangle'],
  ['\\mathrm{e}^{\\mathrm{i}x}', '\\mathrm{e}^{\\mathrm{i}', '}'],
  ['overbrace', '\\overbrace{', '}^{}'], ['underbrace', '\\underbrace{', '}_{}'],
]
function buttonFor(latex: string) {
  const button = Array.from(document.querySelectorAll<HTMLButtonElement>('.markleaf-formula-symbol-button'))
    .find(item => item.title === latex)
  expect(button, `symbol ${latex}`).toBeDefined()
  return button!
}
function caret(code: HTMLElement) {
  const selection = window.getSelection()!
  const range = document.createRange()
  range.selectNodeContents(code)
  range.setEnd(selection.focusNode!, selection.focusOffset)
  return range.toString().length
}
describe('corrected formula additions', () => {
  it.each(literals)('inserts %s literally as %s', (preview, latex) => {
    const { code, editor } = open()
    window.getSelection()!.setBaseAndExtent(code.firstChild!, 1, code.firstChild!, 1)
    const button = buttonFor(latex)
    expect(button.getAttribute('aria-label')).toBe(`${preview} ${latex}`)
    expect(button.querySelector('.katex-error')).toBeNull()
    button.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0, cancelable: true }))
    expect(editor.state.doc.firstChild!.textContent).toBe(`x${latex}y`)
    expect(caret(code)).toBe(1 + latex.length)
  })
  for (const [preview, before, after] of templates) for (const selected of [false, true]) {
    it(`wraps ${preview} selection=${selected} with the correct caret`, () => {
      const { code, editor } = open()
      window.getSelection()!.setBaseAndExtent(code.firstChild!, 1, code.firstChild!, selected ? 0 : 1)
      const button = buttonFor(before! + after!)
      expect(button.getAttribute('aria-label')).toBe(`${preview} ${before}${after}`)
      expect(button.querySelector('.katex-error')).toBeNull()
      button.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0, cancelable: true }))
      const expected = selected ? `${before}x${after}y` : `x${before}${after}y`
      expect(editor.state.doc.firstChild!.textContent).toBe(expected)
      expect(caret(code)).toBe(selected ? before!.length + 1 + after!.length : 1 + before!.length)
    })
  }
})
