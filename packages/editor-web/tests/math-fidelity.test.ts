import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getMarkdown, setMarkdownEditingSettings, setAutoConvertUnsafeEmphasis } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []
function make(markdown: string) {
  const mount = document.body.appendChild(document.createElement('div'))
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  return editor
}
afterEach(() => {
  editors.splice(0).forEach(editor => editor.destroy())
  document.body.innerHTML = ''
  setMarkdownEditingSettings({ escapeLiteralSymbols: false, escapeMarkdownLiteralSymbols: true })
  setAutoConvertUnsafeEmphasis(true)
})
function formulas(editor: ReturnType<typeof createEditor>) {
  const result: string[] = []
  editor.state.doc.descendants(node => {
    if (node.type.name === 'mathInline' || node.type.name === 'mathBlock') result.push(node.textContent)
  })
  return result
}

describe('opaque formula source', () => {
  for (const [open, close, source] of [
    ['$', '$', String.raw`  a\_b + \alpha &amp; &lt; *x*  `],
    ['\\(', '\\)', String.raw`  a\_b + \alpha &amp; &lt; *x*  `],
    ['$$', '$$', '\n  a\\_b + \\alpha &amp; &lt; *x*\n  b \\\\ c\n '],
    ['\\[', '\\]', '\n  a\\_b + \\alpha &amp; &lt; *x*\n  b \\\\ c\n '],
  ]) {
    it(`preserves whitespace and literal source in ${open} delimiters`, () => {
      const editor = make(`${open}${source}${close}`)
      expect(formulas(editor)).toEqual([source])
      expect(getMarkdown(editor)).toContain(source)
      expect(formulas(make(getMarkdown(editor)))).toEqual([source])
    })
  }
  for (const emphasis of [false, true]) for (const symbols of [false, true]) for (const markers of [false, true]) {
    it(`protects actual math nodes with emphasis=${emphasis}, entities=${symbols}, markers=${markers}`, () => {
      setAutoConvertUnsafeEmphasis(emphasis)
      setMarkdownEditingSettings({ escapeLiteralSymbols: symbols, escapeMarkdownLiteralSymbols: markers })
      const source = String.raw`a\_b \*x\* &amp; &lt; 中文**粗体**文`
      const editor = make(`$${source}$\n\n$$${source}$$`)
      expect(getMarkdown(editor)).toContain(`$${source}$`)
      expect(getMarkdown(editor)).toContain(`$$${source}$$`)
    })
  }
  it.each([
    '```text\n$x &amp; y$\n```', '`$x &amp; y$`',
    String.raw`\$a &amp; b\$`, '$5 and $10', '$5, $10 and $20', '$unmatched &amp;',
    '\u0000markleaf-protected-0\u0000', '\u0000markleaf-marker-protected-0\u0000',
  ])('does not interpret ordinary text as formulas: %s', markdown => {
    const editor = make(markdown)
    expect(formulas(editor)).toEqual([])
    const output = getMarkdown(editor)
    expect(formulas(make(output))).toEqual([])
    if (markdown.includes('markleaf-')) expect(output).toContain(markdown)
    if (markdown.startsWith('`')) expect(output).toContain('$x &amp; y$')
  })
  it('decodes ordinary text between escaped dollars', () => {
    setMarkdownEditingSettings({ escapeLiteralSymbols: false })
    expect(getMarkdown(make(String.raw`\$a & b\$`))).toContain('& b')
  })
  it('preserves padded ellipses as data and avoids collisions with token-like text', () => {
    const source = ' ... '
    const editor = make(`MARKLEAFOPAQUEMATH0END $${source}$`)
    expect(formulas(editor)).toEqual([source])
    expect(getMarkdown(editor)).toContain(`MARKLEAFOPAQUEMATH0END $${source}$`)
  })
  it('keeps escaped double dollars inside block payloads', () => {
    const source = String.raw`a \$$ b`
    expect(formulas(make(`$$${source}$$`))).toEqual([source])
  })
  it('keeps escaped dollar payloads inside a formula', () => {
    const source = String.raw`x + \$5 + y`
    expect(formulas(make(`$${source}$`))).toEqual([source])
  })
})
