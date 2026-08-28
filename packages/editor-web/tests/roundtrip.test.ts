import { afterEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  createEditor,
  executeEditorCommand,
  getEditorCommandState,
  getEditorStatus,
  getMarkdown,
  replaceEditorDocument,
  resetEditorViewport,
  sanitizePastedHtml,
  findInEditor,
  findFootnoteDefinitionBody,
  exportEditorSelection,
  replaceAllInEditor,
  replaceCurrentInEditor,
} from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

if (!globalThis.ClipboardEvent) {
  globalThis.ClipboardEvent = class ClipboardEvent extends Event {} as unknown as typeof ClipboardEvent
}

afterEach(() => {
  for (const editor of editors.splice(0)) {
    editor.destroy()
  }
  document.body.innerHTML = ''
  document.documentElement.style.removeProperty('--highlight')
  document.documentElement.style.removeProperty('--text-primary')
})

function roundTrip(markdown: string): string {
  const element = document.createElement('div')
  document.body.append(element)
  const editor = createEditor(element, markdown)
  editors.push(editor)
  return getMarkdown(editor)
}

describe('selection export', () => {
  it('exports formatted HTML, Markdown source, and plain text independently', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'before **bold** after')
    editors.push(editor)
    const boldPosition = editor.state.doc.textContent.indexOf('bold') + 1
    editor.commands.setTextSelection({ from: boldPosition, to: boldPosition + 4 })

    const selection = exportEditorSelection(editor)

    expect(selection.text).toBe('bold')
    expect(selection.markdown).toContain('**bold**')
    expect(selection.html).toContain('<strong>bold</strong>')
  })

  it('pastes an exported visual image back as an image', () => {
    const sourceElement = document.createElement('div')
    const targetElement = document.createElement('div')
    document.body.append(sourceElement, targetElement)
    const source = createEditor(sourceElement, '![diagram](C:/Pictures/image.png)')
    const target = createEditor(targetElement, '')
    editors.push(source, target)
    source.commands.setNodeSelection(0)

    const selection = exportEditorSelection(source)

    expect(selection.html).toContain('data-markleaf-path')
    expect(executeEditorCommand(target, 'pasteHtml', selection.html)).toBe(true)
    expect(targetElement.querySelector('img')?.getAttribute('data-markleaf-path')).toBe('C:/Pictures/image.png')
    expect(getMarkdown(target)).toContain('![diagram](C:/Pictures/image.png)')
  })

  it('pastes an exported visual image caption as image metadata instead of caption-only text', () => {
    const sourceElement = document.createElement('div')
    const targetElement = document.createElement('div')
    document.body.append(sourceElement, targetElement)
    const source = createEditor(sourceElement, '![diagram](C:/Pictures/image.png "markleaf:caption=Figure%201")')
    const target = createEditor(targetElement, '')
    editors.push(source, target)
    source.commands.setNodeSelection(0)

    const selection = exportEditorSelection(source)

    expect(selection.html).toContain('markleaf-figure')
    expect(executeEditorCommand(target, 'pasteHtml', selection.html)).toBe(true)
    expect(targetElement.querySelectorAll('img')).toHaveLength(1)
    expect(getMarkdown(target)).toContain('![diagram](C:/Pictures/image.png "markleaf:caption=Figure%201")')
    expect(getMarkdown(target)).not.toBe('Figure 1')
  })
})

describe('Markdown semantic round trip', () => {
  it('preserves the Stage 5 golden document semantically', () => {
    const markdown = readFileSync(resolve(import.meta.dirname, 'fixtures/stage5-golden.md'), 'utf8')
    const output = roundTrip(markdown)

    expect(output).toContain('# 阶段 5 黄金样例')
    expect(output).toContain('**粗体**')
    expect(output).toMatch(/[*_]中文斜体[*_]/)
    expect(output).toContain('[安全链接](https://example.com)')
    expect(output).toContain('日本語：かなカナ漢字。')
    expect(output).toContain('한국어: 한글 입력.')
    expect(output).toMatch(/^> 引用第一段。/m)
    expect(output).toContain('- 嵌套项目')
    expect(output).toContain('1. 有序列表第一项')
    expect(output).toContain('var leaf = "MarkLeaf";')
    expect(output).toMatch(/^---$/m)
  })

  it('preserves the MVP block and inline structures', () => {
    const markdown = `# MarkLeaf 原型验证

这是包含 **粗体**、*斜体*、~~删除线~~、\`行内代码\` 和 [链接](https://example.com) 的段落。

> 多段引用第一段。
>
> 第二段包含中文标点：“你好，世界！”

- 无序列表
  1. 嵌套有序列表
  2. 第二项
- 尾项

- [x] 已完成任务
- [ ] 待处理任务

| 功能 | 状态 |
| :--- | ---: |
| 表格 | 可用 |
| 中文 | 正常 |

\`\`\`ts
const leaf = 'MarkLeaf'
console.log(leaf)
\`\`\`

---
`

    const output = roundTrip(markdown)

    expect(output).toContain('# MarkLeaf 原型验证')
    expect(output).toContain('**粗体**')
    expect(output).toContain('~~删除线~~')
    expect(output).toContain('- [x] 已完成任务')
    expect(output).toMatch(/\|\s*功能\s*\|\s*状态\s*\|/)
    expect(output).toContain("const leaf = 'MarkLeaf'")
  })

  it('preserves emoji, escaped punctuation, and relative images semantically', () => {
    const markdown = `## 字符测试 😀

转义字符：\\*不是斜体\\*，全角标点：，。！？

![占位图片](./prototype.assets/image-placeholder.png "后期资源占位")
`

    const output = roundTrip(markdown)

    expect(output).toContain('## 字符测试 😀')
    expect(output).toContain('不是斜体')
    expect(output).toContain('全角标点：，。！？')
    expect(output).toContain('./prototype.assets/image-placeholder.png')
  })

  it('does not claim character-level fidelity for equivalent Markdown forms', () => {
    const markdown = `Heading
=======

* item
`

    const output = roundTrip(markdown)

    expect(output).toContain('# Heading')
    expect(output).toContain('- item')
    expect(output).not.toBe(markdown)
  })

  it('does not silently delete unsupported directive-like text', () => {
    const markdown = `:::note custom-option\n未知扩展内容 **保持可见**\n:::\n`
    const output = roundTrip(markdown)

    expect(output).toContain(':::note custom-option')
    expect(output).toContain('未知扩展内容')
    expect(output).toContain(':::')
  })
})

describe('IME composition safety', () => {
  it('keeps composition events available to the editor surface', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '中文输入')
    editors.push(editor)
    const compositionStart = vi.fn()
    const compositionEnd = vi.fn()

    editor.view.dom.addEventListener('compositionstart', compositionStart)
    editor.view.dom.addEventListener('compositionend', compositionEnd)
    editor.view.dom.dispatchEvent(new CompositionEvent('compositionstart', { data: '中' }))
    editor.view.dom.dispatchEvent(new CompositionEvent('compositionend', { data: '中文' }))

    expect(compositionStart).toHaveBeenCalledOnce()
    expect(compositionEnd).toHaveBeenCalledOnce()
    expect(getMarkdown(editor)).toContain('中文输入')
  })

  it('preserves multilingual text, full-width punctuation, and emoji', () => {
    const output = roundTrip('中文，。！？\n\n日本語かな。\n\n한국어 문장.\n\nemoji 😀🌿')

    expect(output).toContain('中文，。！？')
    expect(output).toContain('日本語かな。')
    expect(output).toContain('한국어 문장.')
    expect(output).toContain('😀🌿')
  })

  it('does not duplicate a full-width symbol inserted through text input', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '中文')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    const handled = editor.view.someProp('handleTextInput', handler => handler(editor.view, editor.state.selection.from, editor.state.selection.to, '，', () => editor.state.tr.insertText('，')))
    if (!handled) {
      editor.view.dispatch(editor.state.tr.insertText('，', editor.state.selection.from, editor.state.selection.to))
    }

    expect(getMarkdown(editor)).toBe('中文，')
  })
})

describe('visual Markdown shortcuts', () => {
  it('preserves inline formatting in footnote definitions and previews it as plain text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '[^1]: **加粗**、*斜体*和`代码`')
    editors.push(editor)

    expect(editor.getHTML()).toContain('<strong>加粗</strong>')
    expect(editor.getHTML()).toContain('<em>斜体</em>')
    expect(editor.getHTML()).toContain('<code>代码</code>')
    expect(getMarkdown(editor)).toBe('[^1]: **加粗**、*斜体*和`代码`')
    expect(findFootnoteDefinitionBody(editor, '1')).toBe('加粗、斜体和代码')
  })

  it('renders inline math in footnote definitions and previews its source as plain text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '[^1]: 公式 $x^2 + y^2$')
    editors.push(editor)

    const math = element.querySelector('.markleaf-math-inline')
    expect(math).not.toBeNull()
    expect(math?.querySelector('.katex')).not.toBeNull()
    expect(getMarkdown(editor)).toBe('[^1]: 公式 $x^2 + y^2$')
    expect(findFootnoteDefinitionBody(editor, '1')).toBe('公式 x^2 + y^2')
  })

  it('turns a closed inline formula typed in a footnote definition into rendered math', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '[^1]: 公式 ')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent('$x^2')
    const handled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      '$',
      () => editor.state.tr.insertText('$'),
    ))

    expect(handled).toBe(true)
    expect(element.querySelector('.markleaf-math-inline .katex')).not.toBeNull()
    expect(getMarkdown(editor)).toBe('[^1]: 公式 $x^2$')
    expect(findFootnoteDefinitionBody(editor, '1')).toBe('公式 x^2')
  })

  it('turns a closed footnote reference after paragraph text into superscript', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '正文')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent('[^注释')
    const handled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      ']',
      () => editor.state.tr.insertText(']'),
    ))

    expect(handled).toBe(true)
    expect(editor.getHTML()).toContain('<sup data-footnote-ref="注释" class="markleaf-footnote-ref">[注释]</sup>')
    expect(getMarkdown(editor)).toBe('正文[^注释]')
  })

  it('keeps a closed footnote reference as text at the start of a paragraph', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '')
    editors.push(editor)

    editor.commands.insertContent('[^注释')
    const handled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      ']',
      () => editor.state.tr.insertText(']'),
    ))

    expect(handled).toBeFalsy()
    expect(editor.getHTML()).not.toContain('data-footnote-ref')
  })

  it.each(['注释', '1'])(
    'turns a footnote marker %s followed by a colon at paragraph start into a definition',
    (label) => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '')
    editors.push(editor)

    editor.commands.insertContent(`[^${label}]`)
    const handled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      ':',
      () => editor.state.tr.insertText(':'),
    ))

    expect(handled).toBe(true)
    expect(editor.state.selection.$from.parent.textContent).toBe(`\u2060[^${label}]: `)
    expect(editor.state.selection.empty).toBe(true)
    expect(editor.state.selection.$from.parentOffset).toBe(`\u2060[^${label}]: `.length)
    const paragraph = element.querySelector('p')
    const hiddenPrefix = paragraph?.querySelector('.markleaf-footnote-def-prefix')
    expect(paragraph?.classList.contains('markleaf-footnote-def')).toBe(true)
    expect(hiddenPrefix?.textContent).toBe(`\u2060[^${label}]:`)
    expect(paragraph?.lastChild?.textContent).toBe(' ')
    expect(getMarkdown(editor)).toBe(`[^${label}]: `)
    },
  )

  it('applies bold and italic when closed triple asterisks are typed', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '前')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent('***粗斜体**')
    const handled = editor.view.someProp('handleTextInput', handler => handler(
      editor.view,
      editor.state.selection.from,
      editor.state.selection.to,
      '*',
      () => editor.state.tr.insertText('*'),
    ))

    expect(handled).toBe(true)
    expect(editor.getHTML()).toMatch(/<(strong|em)><(em|strong)>粗斜体<\/\2><\/\1>/)
    expect(getMarkdown(editor)).toContain('前***粗斜体***')
    expect(editor.isActive('bold')).toBe(false)
    expect(editor.isActive('italic')).toBe(false)
  })

  it('applies bold when closed asterisks are typed next to Chinese text', async () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '前')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent('**加粗*')
    const handled = editor.view.someProp('handleTextInput', handler => handler(editor.view, editor.state.selection.from, editor.state.selection.to, '*', () => editor.state.tr.insertText('*')))

    expect(handled).toBe(true)
    expect(editor.getHTML()).toContain('<strong>加粗</strong>')
    expect(getMarkdown(editor)).toContain('前**加粗**')
  })

  it('turns a leading greater-than marker into a blockquote', async () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '')
    editors.push(editor)

    editor.commands.insertContent('>')
    const handled = editor.view.someProp('handleTextInput', handler => handler(editor.view, editor.state.selection.from, editor.state.selection.to, ' ', () => editor.state.tr.insertText(' ')))

    expect(handled).toBe(true)
    expect(editor.isActive('blockquote')).toBe(true)
  })
})

describe('editing history', () => {
  it('supports insert, undo, and redo through ProseMirror history', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '初始内容')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent('，新增内容')
    expect(getMarkdown(editor)).toContain('新增内容')

    editor.commands.undo()
    expect(getMarkdown(editor)).not.toContain('新增内容')

    editor.commands.redo()
    expect(getMarkdown(editor)).toContain('新增内容')
  })

  it('supports multiple undo and redo steps', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '开始')
    editors.push(editor)

    editor.commands.setTextSelection(editor.state.doc.content.size)
    editor.commands.insertContent('一')
    editor.commands.insertContent('二')
    editor.commands.insertContent('三')
    editor.commands.undo()
    editor.commands.undo()
    expect(getMarkdown(editor)).not.toContain('二三')
    editor.commands.redo()
    editor.commands.redo()
    expect(getMarkdown(editor)).toContain('一二三')
  })

  it('applies italic emphasis to Chinese text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '中文斜体')
    editors.push(editor)

    editor.commands.setTextSelection({ from: 1, to: 5 })
    editor.commands.toggleItalic()

    expect(editor.getHTML()).toContain('<em>中文斜体</em>')
    expect(getMarkdown(editor)).toMatch(/[*_]中文斜体[*_]/)
  })

  it('serializes unsafe bold closing boundaries as HTML', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '文本"a')
    editors.push(editor)

    editor.commands.setTextSelection({ from: 1, to: 4 })
    editor.commands.toggleBold()

    const markdown = getMarkdown(editor)
    expect(markdown).toBe('<strong>文本"</strong>a')

    const reloaded = createEditor(document.createElement('div'), markdown)
    editors.push(reloaded)
    expect(reloaded.getHTML()).toContain('<strong>文本"</strong>a')
  })

  it('serializes unsafe italic opening boundaries as HTML', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'a"文本')
    editors.push(editor)

    editor.commands.setTextSelection({ from: 2, to: 5 })
    editor.commands.toggleItalic()

    const markdown = getMarkdown(editor)
    expect(markdown).toBe('a<em>"文本</em>')

    const reloaded = createEditor(document.createElement('div'), markdown)
    editors.push(reloaded)
    expect(reloaded.getHTML()).toContain('a<em>"文本</em>')
  })

  it('keeps safe emphasis boundaries as Markdown', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '文本" 后文')
    editors.push(editor)

    editor.commands.setTextSelection({ from: 1, to: 4 })
    editor.commands.toggleBold()

    expect(getMarkdown(editor)).toBe('**文本"** 后文')
  })

  it('keeps bold-italic asterisk runs intact even when adjacent to CJK text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '前文***粗斜体***后文')
    editors.push(editor)

    const markdown = getMarkdown(editor)
    expect(markdown).toBe('前文***粗斜体***后文')

    const reloaded = createEditor(document.createElement('div'), markdown)
    editors.push(reloaded)
    expect(reloaded.getHTML()).toContain('<strong><em>粗斜体</em></strong>')
  })

  it('keeps bold-italic asterisk runs intact inside punctuation boundaries', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '"***粗斜体***"')
    editors.push(editor)

    const markdown = getMarkdown(editor)
    expect(markdown).toBe('"***粗斜体***"')

    const reloaded = createEditor(document.createElement('div'), markdown)
    editors.push(reloaded)
    expect(reloaded.getHTML()).toContain('<strong><em>粗斜体</em></strong>')
  })
})

describe('find and replace', () => {
  it('finds text across blocks with case and whole-word options', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'Leaf leaf\n\nleaflet leaf')
    editors.push(editor)

    const selectionBefore = editor.state.selection
    expect(findInEditor(editor, 'leaf', false, true)).toEqual({ current: 1, total: 3 })
    expect(editor.state.selection.eq(selectionBefore)).toBe(true)
    expect(element.querySelectorAll('.markleaf-find-match')).toHaveLength(3)
    expect(element.querySelectorAll('.markleaf-find-match-current')).toHaveLength(1)
    expect(findInEditor(editor, 'leaf', true, true)).toEqual({ current: 1, total: 2 })
    expect(element.querySelectorAll('.markleaf-find-match')).toHaveLength(2)
  })

  it('replaces the active match and all remaining matches', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'leaf leaf\n\nleaf')
    editors.push(editor)

    findInEditor(editor, 'leaf', true, false)
    replaceCurrentInEditor(editor, 'leaf', 'branch', true, false)
    expect(getMarkdown(editor)).toContain('branch leaf')
    expect(replaceAllInEditor(editor, 'leaf', 'tree', true, false)).toBe(2)
    expect(getMarkdown(editor)).toContain('branch tree\n\ntree')
  })
})

describe('rendered task and table structure', () => {
  it('renders task items as direct children with checkbox and content siblings', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '- [x] 同行任务')
    editors.push(editor)

    const taskItem = element.querySelector("ul[data-type='taskList'] > li")

    expect(taskItem?.querySelector(':scope > label input[type="checkbox"]')).not.toBeNull()
    expect(taskItem?.querySelector(':scope > div > p')?.textContent).toBe('同行任务')
  })

  it('updates Markdown when a task checkbox is clicked', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '- [ ] task')
    editors.push(editor)

    element.querySelector<HTMLInputElement>("input[type='checkbox']")?.click()

    expect(getMarkdown(editor)).toContain('- [x] task')
    expect(getEditorCommandState(editor).taskList).toBe(true)
  })
})

describe('paragraph menu commands', () => {
  it.each([
    ['setHeading1', /^# 段落命令/m],
    ['setHeading2', /^## 段落命令/m],
    ['setHeading3', /^### 段落命令/m],
    ['setHeading4', /^#### 段落命令/m],
    ['setHeading5', /^##### 段落命令/m],
    ['setHeading6', /^###### 段落命令/m],
    ['toggleBlockquote', /^> 段落命令/m],
    ['toggleCodeBlock', /^```[\s\S]*段落命令/m],
    ['toggleBulletList', /^- 段落命令/m],
    ['toggleOrderedList', /^1\. 段落命令/m],
    ['toggleTaskList', /^- \[ \] 段落命令/m],
  ])('executes %s', (command, expected) => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '段落命令')
    editors.push(editor)

    expect(executeEditorCommand(editor, command)).toBe(true)
    expect(getMarkdown(editor)).toMatch(expected)
  })

  it('toggles bold and italic on the selected Chinese text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '中文格式')
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 5 })

    expect(executeEditorCommand(editor, 'toggleBold')).toBe(true)
    expect(executeEditorCommand(editor, 'toggleItalic')).toBe(true)
    expect(getMarkdown(editor)).toMatch(/\*\*[*_]中文格式[*_]\*\*/)
  })

  it('inserts provided inline-code text at an empty selection', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'before after')
    editors.push(editor)
    editor.commands.setTextSelection(8)

    expect(executeEditorCommand(editor, 'toggleCode', 'const leaf = 1')).toBe(true)
    expect(getMarkdown(editor)).toContain('before `const leaf = 1`after')
  })

  it('applies context-menu inline formatting to the current text block without a selection', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'first line\n\nsecond line')
    editors.push(editor)
    editor.commands.setTextSelection(3)

    expect(executeEditorCommand(editor, 'toggleBold', undefined, undefined, true)).toBe(true)
    expect(getMarkdown(editor)).toContain('**first line**\n\nsecond line')
    expect(editor.state.selection.empty).toBe(true)
    expect(editor.state.selection.from).toBe(3)
  })

  it('scrolls to an AST position supplied by the outline tree', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '# First\n\n## Second')
    editors.push(editor)
    const secondHeadingPosition = editor.state.doc.content.size - 'Second'.length - 2
    const heading = element.querySelector<HTMLElement>('h2')!
    Object.defineProperty(document, 'scrollingElement', { configurable: true, value: document.documentElement })
    document.documentElement.scrollTop = 400
    const selectionBefore = editor.state.selection.from
    vi.spyOn(heading, 'getBoundingClientRect').mockReturnValue({
      x: 0,
      y: 120,
      top: 120,
      right: 0,
      bottom: 0,
      left: 0,
      width: 0,
      height: 0,
      toJSON: () => ({}),
    })

    expect(executeEditorCommand(editor, 'scrollToPosition', String(secondHeadingPosition))).toBe(true)
    expect(editor.state.selection.from).toBe(selectionBefore)
    expect(document.documentElement.scrollTop).toBe(508)
    expect(heading.classList.contains('markleaf-outline-highlight')).toBe(true)
  })

  it('uses the dedicated theme highlight for outline animations', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '# First\n\n## Second')
    editors.push(editor)
    const secondHeadingPosition = editor.state.doc.content.size - 'Second'.length - 2
    const heading = element.querySelector<HTMLElement>('h2')!
    const animate = vi.fn()
    Object.defineProperties(heading, {
      animate: { configurable: true, value: animate },
      getAnimations: { configurable: true, value: () => [] },
      getBoundingClientRect: {
        configurable: true,
        value: () => ({
          x: 0, y: 120, top: 120, right: 0, bottom: 0, left: 0, width: 0, height: 0,
          toJSON: () => ({}),
        }),
      },
    })
    document.documentElement.style.setProperty('--highlight', '#FFF36D')
    document.documentElement.style.setProperty('--text-primary', '#1A1A1A')

    expect(executeEditorCommand(editor, 'scrollToPosition', String(secondHeadingPosition))).toBe(true)
    expect(animate).toHaveBeenCalledWith(
      expect.arrayContaining([
        expect.objectContaining({ backgroundColor: '#FFF36D', color: '#1A1A1A' }),
      ]),
      { duration: 1800, easing: 'ease-out' },
    )
  })

  it('inserts a GFM table', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '')
    editors.push(editor)

    expect(executeEditorCommand(editor, 'insertTable')).toBe(true)
    expect(getMarkdown(editor)).toMatch(/\|.*\|/)
  })

  it('edits table rows, columns, alignment, and deletion at boundaries', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '')
    editors.push(editor)

    expect(executeEditorCommand(editor, 'insertTable')).toBe(true)
    expect(element.querySelectorAll('th').length).toBe(3)
    expect(getEditorCommandState(editor).inTable).toBe(true)
    expect(executeEditorCommand(editor, 'addRowAfter')).toBe(true)
    expect(executeEditorCommand(editor, 'addColumnAfter')).toBe(true)
    expect(executeEditorCommand(editor, 'alignTableCenter')).toBe(true)
    expect(getEditorCommandState(editor).tableAlign).toBe('center')
    expect(getMarkdown(editor)).toMatch(/:\s*-+:\s*\|/)
    expect(executeEditorCommand(editor, 'deleteRow')).toBe(true)
    expect(executeEditorCommand(editor, 'deleteColumn')).toBe(true)
    expect(executeEditorCommand(editor, 'deleteTable')).toBe(true)
    expect(getEditorCommandState(editor).inTable).toBe(false)
  })

  it('handles last-row and last-column deletion without corrupting the table', () => {
    const rowElement = document.createElement('div')
    document.body.append(rowElement)
    const rowEditor = createEditor(rowElement, '| A |\n| --- |\n| B |')
    editors.push(rowEditor)
    expect(executeEditorCommand(rowEditor, 'deleteRow')).toBe(true)
    expect(executeEditorCommand(rowEditor, 'deleteRow')).toBe(false)
    expect(getEditorCommandState(rowEditor).inTable).toBe(true)
    expect(executeEditorCommand(rowEditor, 'deleteTable')).toBe(true)

    const columnElement = document.createElement('div')
    document.body.append(columnElement)
    const columnEditor = createEditor(columnElement, '| A |\n| --- |\n| B |')
    editors.push(columnEditor)
    expect(executeEditorCommand(columnEditor, 'deleteColumn')).toBe(false)
    expect(getEditorCommandState(columnEditor).inTable).toBe(true)
    expect(executeEditorCommand(columnEditor, 'deleteTable')).toBe(true)
  })

  it('round trips task list checks and nesting', () => {
    const markdown = '- [x] done\n  - [ ] nested\n- [ ] pending'
    const output = roundTrip(markdown)

    expect(output).toContain('- [x] done')
    expect(output).toContain('  - [ ] nested')
    expect(output).toContain('- [ ] pending')
  })

  it('uses task item split and join commands for Enter and Backspace behavior', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '- [ ] task')
    editors.push(editor)
    editor.commands.setTextSelection(editor.state.doc.content.size - 2)

    expect(editor.commands.splitListItem('taskItem')).toBe(true)
    expect(getMarkdown(editor).match(/- \[ \]/g)).toHaveLength(2)
    expect(editor.commands.joinBackward()).toBe(true)
    expect(getMarkdown(editor).match(/- \[ \]/g)).toHaveLength(1)
  })

  it('renders absolute image paths through the isolated asset host', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '![diagram](C:/Pictures/my%20image.png)')
    editors.push(editor)

    const image = element.querySelector('img')
    expect(image?.getAttribute('src')).toBe('https://assets.local/image?path=C%3A%2FPictures%2Fmy%20image.png')
    expect(image?.getAttribute('data-markleaf-path')).toMatch(/C:\/Pictures\/my(?:%20| )image\.png/)
    expect(getMarkdown(editor)).toMatch(/C:\/Pictures\/my(?:%20| )image\.png/)
  })

  it('inserts an image node through the host command payload', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '')
    editors.push(editor)

    expect(executeEditorCommand(editor, 'insertImage', 'C:/Pictures/image.png\npasted image')).toBe(true)
    expect(element.querySelector('img')?.getAttribute('src')).toBe('https://assets.local/image?path=C%3A%2FPictures%2Fimage.png')
    expect(getMarkdown(editor)).toContain('![pasted image](C:/Pictures/image.png)')
  })

  it('exposes four corner resize handles and reports image selection state', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '![diagram](image.png)\n\ntext')
    editors.push(editor)

    const handles = element.querySelectorAll('[data-resize-handle]')
    expect(Array.from(handles, handle => handle.getAttribute('data-resize-handle'))).toEqual([
      'top-left',
      'top-right',
      'bottom-left',
      'bottom-right',
    ])
    editor.commands.setTextSelection(editor.state.doc.content.size - 1)
    expect(getEditorCommandState(editor).imageSelected).toBe(false)
    editor.commands.setNodeSelection(0)
    expect(getEditorCommandState(editor).imageSelected).toBe(true)
  })

  it('rotates a selected image clockwise and persists dimensions and rotation', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(
      element,
      '![diagram](image.png "caption || markleaf:width=320;height=180;rotation=0")',
    )
    editors.push(editor)
    editor.commands.setNodeSelection(0)

    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(true)
    const image = element.querySelector<HTMLImageElement>('.markleaf-image-content')
    expect(image?.style.transform).toContain('rotate(90deg)')
    // 旋转后统一为百分比 + 宽高比；宽度保持设定宽度（320px = 39% of 820px），宽高比取倒数。
    expect(getMarkdown(editor)).toContain(
      '"caption || markleaf:widthPct=39;ratio=1.7778;rotation=90"',
    )

    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(true)
    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(true)
    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(true)
    expect(getMarkdown(editor)).toContain(
      '"caption || markleaf:widthPct=39;ratio=0.5625;rotation=0"',
    )
  })

  it('commits a corner resize as one persistent image size change', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(
      element,
      '![diagram](image.png "markleaf:width=320;height=180;rotation=0")',
    )
    editors.push(editor)
    editor.commands.setNodeSelection(0)

    const frame = element.querySelector<HTMLElement>('.markleaf-image-frame')!
    Object.defineProperty(frame, 'offsetWidth', {
      configurable: true,
      get: () => Number.parseFloat(frame.style.width) || 320,
    })
    Object.defineProperty(frame, 'offsetHeight', {
      configurable: true,
      get: () => Number.parseFloat(frame.style.height) || 180,
    })
    const handle = element.querySelector<HTMLElement>('[data-resize-handle="bottom-right"]')!
    handle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: 0, clientY: 0 }))
    document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: 80, clientY: 40 }))
    document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, clientX: 80, clientY: 40 }))

    expect(getMarkdown(editor)).toContain('markleaf:widthPct=49;ratio=0.5625;rotation=0')
    expect(editor.can().undo()).toBe(true)
  })

  it('does not let responsive image layout overwrite an active resize preview', () => {
    const originalResizeObserver = globalThis.ResizeObserver
    const callbacks: ResizeObserverCallback[] = []
    class ControlledResizeObserver {
      constructor(callback: ResizeObserverCallback) {
        callbacks.push(callback)
      }
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    }
    globalThis.ResizeObserver = ControlledResizeObserver as unknown as typeof ResizeObserver

    try {
      const element = document.createElement('div')
      document.body.append(element)
      const editor = createEditor(
        element,
        '![diagram](image.png "markleaf:widthPct=39;ratio=0.5625;rotation=0")',
      )
      editors.push(editor)
      editor.commands.setNodeSelection(0)

      const frame = element.querySelector<HTMLElement>('.markleaf-image-frame')!
      Object.defineProperty(frame, 'offsetWidth', {
        configurable: true,
        get: () => Number.parseFloat(frame.style.width) || 320,
      })
      Object.defineProperty(frame, 'offsetHeight', {
        configurable: true,
        get: () => Number.parseFloat(frame.style.height) || 180,
      })
      const handle = element.querySelector<HTMLElement>('[data-resize-handle="bottom-right"]')!
      handle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, clientX: 0, clientY: 0 }))
      document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: 80, clientY: 40 }))
      const previewWidth = frame.style.width
      const previewHeight = frame.style.height

      for (const callback of callbacks) {
        callback([], {} as ResizeObserver)
      }

      expect(frame.style.width).toBe(previewWidth)
      expect(frame.style.height).toBe(previewHeight)
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, clientX: 80, clientY: 40 }))
    } finally {
      globalThis.ResizeObserver = originalResizeObserver
    }
  })

  it('does not rotate when the selection is not an image', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'text\n\n![diagram](image.png)')
    editors.push(editor)
    editor.commands.setTextSelection(1)

    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(false)
  })

  it('round trips image metadata without changing an ordinary title', () => {
    const ordinary = roundTrip('![diagram](image.png "ordinary title")')
    const managed = roundTrip(
      '![diagram](image.png "ordinary title || markleaf:width=240;height=135;rotation=270")',
    )

    expect(ordinary).toContain('"ordinary title"')
    expect(ordinary).not.toContain('markleaf:')
    expect(managed).toContain(
      '"ordinary title || markleaf:width=240;height=135;rotation=270"',
    )
  })

  it('supports undo and redo for image rotation', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(
      element,
      '![diagram](image.png "markleaf:width=320;height=180;rotation=0")',
    )
    editors.push(editor)
    editor.commands.setNodeSelection(0)

    expect(executeEditorCommand(editor, 'rotateImageClockwise')).toBe(true)
    expect(getMarkdown(editor)).toContain('rotation=90')
    expect(executeEditorCommand(editor, 'undo')).toBe(true)
    expect(getMarkdown(editor)).toContain('rotation=0')
    expect(executeEditorCommand(editor, 'redo')).toBe(true)
    expect(getMarkdown(editor)).toContain('rotation=90')
  })

  it('applies percentage width in exported HTML images', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(
      element,
      '![diagram](image.png "markleaf:widthPct=49;ratio=0.5625;rotation=0")',
    )
    editors.push(editor)

    const html = editor.getHTML()
    // 49% of the default 820px max-width = 402px; height = 402 * 0.5625 = 226px
    expect(html).toMatch(/display:\s*block/)
    expect(html).toMatch(/margin:\s*0.85em auto/)
    expect(html).toMatch(/width:\s*402px/)
    expect(html).toMatch(/aspect-ratio:\s*402\s*\/\s*226/)
  })

  it('inserts a dropped image at the document position resolved from mouse coordinates', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, 'first\n\nsecond')
    editors.push(editor)
    editor.commands.setTextSelection(editor.state.doc.content.size - 1)
    vi.spyOn(editor.view, 'posAtCoords').mockReturnValue({ pos: 2, inside: 0 })

    expect(executeEditorCommand(
      editor,
      'insertImage',
      'drop.png\ndropped',
      { left: 100, top: 200 },
    )).toBe(true)

    expect(editor.view.posAtCoords).toHaveBeenCalledWith({ left: 100, top: 200 })
    expect(getMarkdown(editor).indexOf('drop.png')).toBeLessThan(getMarkdown(editor).indexOf('second'))
  })

  it('inserts a horizontal rule between blocks', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '分隔线上方')
    editors.push(editor)
    editor.commands.setTextSelection(editor.state.doc.content.size)

    expect(executeEditorCommand(editor, 'insertHorizontalRule')).toBe(true)
    expect(getMarkdown(editor)).toMatch(/分隔线上方\n\n---/)
  })

  it('formats a selection spanning multiple paragraphs without dropping text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '第一段\n\n第二段')
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: editor.state.doc.content.size - 1 })

    expect(executeEditorCommand(editor, 'toggleBlockquote')).toBe(true)
    const markdown = getMarkdown(editor)
    expect(markdown).toContain('第一段')
    expect(markdown).toContain('第二段')
    expect(markdown.match(/^>/gm)?.length).toBeGreaterThanOrEqual(2)
  })

  it('adds an allowed link to selected text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '链接文本')
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 5 })

    expect(executeEditorCommand(editor, 'setLink', 'https://example.com')).toBe(true)
    expect(getMarkdown(editor)).toContain('[链接文本](https://example.com)')
    expect(executeEditorCommand(editor, 'setLink', 'javascript:alert(1)')).toBe(false)
  })

  it('inserts a visible link when there is no selection', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '链接前文')
    editors.push(editor)
    editor.commands.setTextSelection(editor.state.doc.content.size)

    expect(executeEditorCommand(editor, 'setLink', 'https://example.com/path')).toBe(true)
    expect(getMarkdown(editor)).toContain('[https://example.com/path](https://example.com/path)')
    expect(editor.getHTML()).toContain('href="https://example.com/path"')
  })
})

describe('command state synchronization', () => {
  it('reports selection, format, block type, and history state', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '状态同步')
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 5 })
    editor.commands.toggleBold()

    const state = getEditorCommandState(editor)
    expect(state.hasSelection).toBe(true)
    expect(state.bold).toBe(true)
    expect(state.paragraph).toBe(true)
    expect(state.canUndo).toBe(true)
    expect(state.canRedo).toBe(false)
  })

  it('reports headings and list blocks', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '块状态')
    editors.push(editor)
    editor.commands.setHeading({ level: 3 })
    expect(getEditorCommandState(editor).headingLevel).toBe(3)
    editor.commands.toggleBulletList()
    expect(getEditorCommandState(editor).bulletList).toBe(true)
  })
})

describe('editor status synchronization', () => {
  it('counts visible Unicode characters and the selected range', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '中文 😀 test')
    editors.push(editor)
    editor.commands.setTextSelection({ from: 1, to: 3 })

    const status = getEditorStatus(editor)
    expect(status.characterCount).toBe(7)
    expect(status.selectedCharacterCount).toBe(2)
    expect(status.blockType).toBe('paragraph')
  })

  it('reports the current block and cursor line and column', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '# 标题\n\n第二段')
    editors.push(editor)
    editor.commands.setTextSelection(2)
    expect(getEditorStatus(editor).blockType).toBe('heading1')

    editor.commands.setTextSelection(editor.state.doc.content.size - 1)
    const status = getEditorStatus(editor)
    expect(status.blockType).toBe('paragraph')
    expect(status.line).toBe(2)
    expect(status.column).toBeGreaterThan(1)
  })
})

describe('document history isolation', () => {
  it('recreates the editor so undo cannot restore the previous document', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const first = createEditor(element, '第一个文件')
    first.commands.setTextSelection(first.state.doc.content.size)
    first.commands.insertContent('的修改')
    expect(first.can().undo()).toBe(true)

    const second = replaceEditorDocument(first, element, '第二个文件')
    editors.push(second)

    expect(second.can().undo()).toBe(false)
    expect(second.commands.undo()).toBe(false)
    expect(getMarkdown(second)).toContain('第二个文件')
    expect(getMarkdown(second)).not.toContain('第一个文件')
  })

  it('resets the editor viewport after loading another document', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '# New document')
    editors.push(editor)
    Object.defineProperty(document, 'scrollingElement', { configurable: true, value: document.documentElement })
    document.documentElement.scrollTop = 600
    element.scrollTop = 200

    resetEditorViewport(editor, element)

    expect(document.documentElement.scrollTop).toBe(0)
    expect(element.scrollTop).toBe(0)
    expect(editor.state.selection.from).toBe(1)
  })
})

describe('paste safety', () => {
  it('removes executable and embedded content while keeping safe formatting', () => {
    const output = sanitizePastedHtml(`
      <p onclick="alert(1)" style="color:red">保留 <strong>粗体</strong></p>
      <script>alert(1)</script>
      <iframe src="https://example.com"></iframe>
      <img src="file:///secret.png" onerror="alert(1)">
      <a href="javascript:alert(1)">危险链接</a>
      <a href="https://example.com">安全链接</a>
    `)

    expect(output).toContain('<strong>粗体</strong>')
    expect(output).toContain('https://example.com')
    expect(output).not.toMatch(/script|iframe|img|onclick|style=|javascript:/i)
  })

  it('pastes multiline Unicode text as literal text', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '前文')
    editors.push(editor)
    editor.commands.setTextSelection(editor.state.doc.content.size)

    expect(executeEditorCommand(editor, 'pasteText', '<b>不是 HTML</b>\n中文 😀')).toBe(true)
    const markdown = getMarkdown(editor)
    expect(markdown).toContain('&lt;b&gt;不是 HTML&lt;/b&gt;')
    expect(markdown).toContain('中文 😀')
  })
})

describe('captions', () => {
  it('round-trips an image caption stored in the title metadata', () => {
    const markdown = '![alt](image.png "markleaf:caption=figure%20caption")'
    const out = roundTrip(markdown)
    expect(out).toContain('markleaf:caption=figure%20caption')
  })

  it('round-trips a table caption from the blockquote prefix', () => {
    const markdown = '> tablecaption: 表格标题\n\n| a | b |\n| - | - |\n| 1 | 2 |'
    const out = roundTrip(markdown)
    expect(out).toContain('> tablecaption: 表格标题')
    expect(out).toContain('| a')
    expect(out).toContain('| 1')
  })

  it('sets and persists an image caption via the command', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '![alt](image.png)')
    editors.push(editor)
    let imagePos = -1
    editor.state.doc.descendants((node, p) => {
      if (imagePos === -1 && node.type.name === 'image') imagePos = p
    })
    editor.commands.setNodeSelection(imagePos)
    expect(executeEditorCommand(editor, 'setImageCaption', '图注 with 空格')).toBe(true)
    expect(getEditorCommandState(editor).caption).toBe('图注 with 空格')
    expect(roundTrip(getMarkdown(editor))).toContain('markleaf:caption=')
  })

  it('sets and persists a table caption via the command', () => {
    const element = document.createElement('div')
    document.body.append(element)
    const editor = createEditor(element, '| a | b |\n| - | - |\n| 1 | 2 |')
    editors.push(editor)
    let pos = -1
    editor.state.doc.descendants((node, p) => {
      if (pos === -1 && node.isText) pos = p
    })
    editor.commands.setTextSelection(pos)
    expect(executeEditorCommand(editor, 'setTableCaption', '表格标题')).toBe(true)
    expect(getMarkdown(editor)).toContain('> tablecaption: 表格标题')
  })

  it('preserves inline formatting in table captions', () => {
    const markdown = '> tablecaption: **加粗** 与 *斜体*\n\n| a | b |\n| - | - |\n| 1 | 2 |'
    const out = roundTrip(markdown)
    expect(out).toContain('> tablecaption: **加粗** 与 *斜体*')
  })

  it('preserves inline formatting in image captions', () => {
    const markdown = '![alt](image.png "markleaf:caption=**%E5%8A%A0%E7%B2%97**")'
    const out = roundTrip(markdown)
    expect(out).toContain('**%E5%8A%A0%E7%B2%97**')
  })

  it('normalizes multiple table captions without corrupting positions', () => {
    const markdown = [
      '> tablecaption: **表 1：** 第一个表格',
      '',
      '| a | b |',
      '| - | - |',
      '| 1 | 2 |',
      '',
      '> tablecaption: **表 2：** 第二个表格',
      '',
      '| c | d |',
      '| - | - |',
      '| 3 | 4 |',
    ].join('\n')
    const out = roundTrip(markdown)
    expect(out).toContain('> tablecaption: **表 1：** 第一个表格')
    expect(out).toContain('> tablecaption: **表 2：** 第二个表格')
    expect(out).toContain('| a')
    expect(out).toContain('| c')
  })
})
