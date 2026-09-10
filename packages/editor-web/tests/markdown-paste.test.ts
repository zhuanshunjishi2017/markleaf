import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getMarkdown, pasteMarkdownText, pasteMarkdownTextWithResult, replaceEditorDocument, shouldParsePastedTextAsMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(content = '') {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, content)
  editors.push(editor)
  return editor
}

describe('Markdown paste', () => {
  it('parses block and inline Markdown through the Markdown manager', () => {
    const editor = makeEditor('')

    expect(pasteMarkdownText(editor, '# Heading\n\n**bold** and $x^2$\n\n$$y^2$$')).toBe(true)

    expect(editor.getJSON().content?.map(node => node.type)).toEqual([
      'heading',
      'paragraph',
      'mathBlock',
      'paragraph',
    ])
    expect(editor.getHTML()).toContain('<strong>bold</strong>')
    expect(editor.getHTML()).toContain('data-math-inline="1"')
    expect(editor.getHTML()).toContain('data-math-block="1"')
  })

  it('normalizes marks that cannot coexist with inline code', () => {
    const editor = makeEditor('')

    const result = pasteMarkdownTextWithResult(editor, '**`font-family` is important**')

    expect(result).toEqual({ success: true, outcome: 'normalized' })
    expect(editor.getHTML()).toContain('<code>font-family</code>')
    expect(getMarkdown(editor)).toContain('`font-family`')
  })

  it.each([
    ['italic', '*`code`*'],
    ['bold and italic', '***`code`***'],
    ['strikethrough', '~~`code`~~'],
    ['link', '[`code`](https://example.com)'],
  ])('rebuilds the %s mark set through ProseMirror exclusion rules', (_name, markdown) => {
    const editor = makeEditor('')

    const result = pasteMarkdownTextWithResult(editor, markdown)
    const codeText = editor.getJSON().content?.[0]?.content?.find(node => 'text' in node && node.text === 'code')

    expect(result).toEqual({ success: true, outcome: 'normalized' })
    expect(codeText?.marks?.map(mark => mark.type)).toEqual(['code'])
  })

  it('keeps the surrounding Markdown when a long mixed paste contains bold inline code', () => {
    const editor = makeEditor('')
    const markdown = `正文，**粗体内容。**

### 标题

* **列表项**：通过 \`@font-face\` 加载字体。
* **\`font-family\` 声明本身风险极低，但根本问题未解决**：后续说明。

1. **第一项**：正文。
2. **第二项**：正文。

\`\`\`css
body { font-family: serif; }
\`\`\`

**总结内容。**`

    const result = pasteMarkdownTextWithResult(editor, markdown)
    const pasted = getMarkdown(editor)

    expect(result).toEqual({ success: true, outcome: 'normalized' })
    expect(pasted).toContain('### 标题')
    expect(pasted).toContain('`font-family`')
    expect(pasted).toContain('body { font-family: serif; }')
    expect(pasted).toContain('总结内容')
  })

  it('separates an indented display formula from the preceding list item', () => {
    const editor = makeEditor('')
    const markdown = String.raw`- 只有在三维空间中，才能得到唯一的法向量，即：
  \[
  a \times b = * (a \wedge b)
  \]
**结论**：这是三维空间的特殊性质。`

    const result = pasteMarkdownTextWithResult(editor, markdown)
    const containsMathBlock = (node: any): boolean => node?.type === 'mathBlock'
      || (Array.isArray(node?.content) && node.content.some(containsMathBlock))

    expect(result).toEqual({ success: true, outcome: 'normalized' })
    expect(containsMathBlock(editor.getJSON())).toBe(true)
    expect(editor.getText()).toContain('结论')
  })

  it('renders bracket-delimited formulas in an ordered derivation paste', () => {
    const editor = makeEditor('')
    const hardBreak = '  '
    const markdown = String.raw`给定方程 \( f(x) + f'(-x) = 1 \)，求通解。

### 推导过程
1. **变量替换**${hardBreak}
   令 \( g(x) = f(-x) \)，则 \( g'(x) = -f'(-x) \)。${hardBreak}
   代入原方程得：${hardBreak}
   \[
   f(x) - g'(x) = 1. \tag{A}
   \]

2. **对称形式**${hardBreak}
   \[
   g(x) + f'(x) = 1. \tag{B}
   \]

### 结论
\[
\boxed{f(x)=1+C(\cos x-\sin x)}
\]`

    const result = pasteMarkdownTextWithResult(editor, markdown)
    const json = editor.getJSON()
    const mathNodes: any[] = []
    const collectMath = (node: any): void => {
      if (node?.type === 'mathInline' || node?.type === 'mathBlock') mathNodes.push(node)
      if (Array.isArray(node?.content)) node.content.forEach(collectMath)
    }
    collectMath(json)

    expect(result.success).toBe(true)
    expect(result.outcome).not.toBe('plainText')
    expect(mathNodes.filter(node => node.type === 'mathBlock')).toHaveLength(3)
    expect(mathNodes.filter(node => node.type === 'mathInline').length).toBeGreaterThanOrEqual(3)
    expect(editor.getHTML()).not.toContain('>[<')
    expect(editor.getText()).not.toContain('\\[')
    expect(editor.getText()).not.toContain('\\]')
  })

  it('moves an indented one-line display formula out of a list item', () => {
    const editor = makeEditor('')
    const markdown = String.raw`1. 势场不含时 $\dfrac{\partial V(\bm{r})}{\partial t}=0$：$\psi(\bm{r}, t)=\varphi(\bm{r})f(t)$
2. 代入 Schrödinger EQ：
  $$\underbrace{\mathrm{i}\hbar\dfrac{\mathrm{d}f}{\mathrm{d}t}\dfrac{1}{f(t)}}_{\text{only-}t}=\underbrace{\dfrac{1}{\varphi(\bm{r})}\left[-\dfrac{\hbar^2}{2m}\nabla^2+V(\bm{r})\right]\varphi(\bm{r})}_{\text{only-}\bm{r}}=\underbrace{E}_\text{so-constant}$$
3. eqs. and solutions:`

    const result = pasteMarkdownTextWithResult(editor, markdown)
    const mathBlocks: any[] = []
    const collect = (node: any): void => {
      if (node?.type === 'mathBlock') mathBlocks.push(node)
      if (Array.isArray(node?.content)) node.content.forEach(collect)
    }
    collect(editor.getJSON())

    expect(result.success).toBe(true)
    expect(result.outcome).not.toBe('plainText')
    expect(mathBlocks).toHaveLength(1)
    expect(mathBlocks[0].content?.[0]?.text).toContain('\\underbrace')
  })

  it('uses the same display-math normalization when initially loading a document', () => {
    const markdown = String.raw`1. 势场不含时 $\dfrac{\partial V(\bm{r})}{\partial t}=0$：$\psi(\bm{r}, t)=\varphi(\bm{r})f(t)$
2. 代入 Schrödinger EQ：
  $$\underbrace{\mathrm{i}\hbar\dfrac{\mathrm{d}f}{\mathrm{d}t}\dfrac{1}{f(t)}}_{\text{only-}t}=\underbrace{\dfrac{1}{\varphi(\bm{r})}\left[-\dfrac{\hbar^2}{2m}\nabla^2+V(\bm{r})\right]\varphi(\bm{r})}_{\text{only-}\bm{r}}=\underbrace{E}_\text{so-constant}$$
3. eqs. and solutions:`
    const element = document.createElement('div')
    document.body.append(element)
    let editor = createEditor(element, markdown)
    editors.push(editor)

    const hasMathBlock = (node: any): boolean => node?.type === 'mathBlock'
      || (Array.isArray(node?.content) && node.content.some(hasMathBlock))
    expect(hasMathBlock(editor.getJSON())).toBe(true)
    expect(editor.getText()).not.toContain('$$')

    editor = replaceEditorDocument(editor, element, markdown)
    editors.push(editor)
    expect(hasMathBlock(editor.getJSON())).toBe(true)
    expect(editor.getText()).not.toContain('$$')
  })

  it('normalizes indented multi-line display formulas consistently', () => {
    const markdown = String.raw`给定方程 $ f(x) + f'(-x) = 1 $，求通解。

1. **变量替换**
令 $ g(x) = f(-x) $，则 $ g'(x) = -f'(-x) $。
代入原方程得：
  $$
   f(x) - g'(x) = 1. \tag{A}
  $$
2. **对称形式**
将原方程中的 $ x $ 替换为 $ -x $，得：
  $$
   f(-x) + f'(x) = 1.
  $$`
    const editor = makeEditor(markdown)
    const mathBlocks: any[] = []
    const collect = (node: any): void => {
      if (node?.type === 'mathBlock') mathBlocks.push(node)
      if (Array.isArray(node?.content)) node.content.forEach(collect)
    }
    collect(editor.getJSON())

    expect(mathBlocks).toHaveLength(2)
    expect(mathBlocks[0].content?.[0]?.text).toContain('f(x) - g\'(x) = 1.')
    expect(mathBlocks[1].content?.[0]?.text).toContain("f(-x) + f'(x) = 1.")
    expect(editor.getText()).not.toContain('$$')
  })

  it.each([
    ['unordered dash', '-'],
    ['unordered plus', '+'],
    ['unordered asterisk', '*'],
    ['ordered dot', '1.'],
    ['ordered parenthesis', '1)'],
  ])('uses one display-math rule for %s list items', (_name, marker) => {
    const markdown = `${marker} first item\n  $$x_1 + y_1$$\n${marker} second item\n  \\\[\n    x_2 + y_2\n\\]`
    const editor = makeEditor(markdown)
    const mathBlocks: any[] = []
    const collect = (node: any): void => {
      if (node?.type === 'mathBlock') mathBlocks.push(node)
      if (Array.isArray(node?.content)) node.content.forEach(collect)
    }
    collect(editor.getJSON())

    expect(mathBlocks).toHaveLength(2)
    expect(mathBlocks[0].content?.[0]?.text).toBe('x_1 + y_1')
    expect(mathBlocks[1].content?.[0]?.text).toContain('x_2 + y_2')
    expect(editor.getText()).not.toContain('$$')
    expect(editor.getText()).not.toContain('\\[')
    expect(editor.getText()).not.toContain('\\]')
  })

  it('uses the same display-math rule for nested list indentation', () => {
    const markdown = String.raw`- outer
  - inner
    $$
    x + y
    $$`
    const editor = makeEditor(markdown)
    const hasMathBlock = (node: any): boolean => node?.type === 'mathBlock'
      || (Array.isArray(node?.content) && node.content.some(hasMathBlock))

    expect(hasMathBlock(editor.getJSON())).toBe(true)
    expect(editor.getText()).not.toContain('$$')
  })

  it('falls back to literal text when parsed Markdown has no insertable document content', () => {
    const editor = makeEditor('')

    const result = pasteMarkdownTextWithResult(editor, '[^1]: footnote')

    expect(result).toEqual({
      success: true,
      outcome: 'plainText',
      error: 'Invalid content for node doc',
    })
    expect(editor.getText()).toContain('[^1]: footnote')
  })

  it('recognizes Markdown source even when the clipboard also contains highlighted HTML', () => {
    const editor = makeEditor('')
    const markdown = '# Heading\n\n**bold** and $x^2$'
    const vscodeHtml = '<div style="color: #ccc"><pre><span style="color: #abc"># Heading</span>\n\n**bold** and $x^2$</pre></div>'

    expect(shouldParsePastedTextAsMarkdown(editor, markdown, vscodeHtml)).toBe(true)
  })

  it('keeps genuine rich text on the HTML paste path', () => {
    const editor = makeEditor('')

    expect(shouldParsePastedTextAsMarkdown(
      editor,
      'Already formatted text',
      '<p><strong>Already formatted</strong> text</p>',
    )).toBe(false)
  })

  it('keeps genuinely formatted web selections on the HTML paste path', () => {
    const editor = makeEditor('')

    expect(shouldParsePastedTextAsMarkdown(
      editor,
      'Heading\nbold link',
      '<h1>Heading</h1><p><strong>bold</strong> <a href="https://example.com">link</a></p>',
    )).toBe(false)
  })
})
