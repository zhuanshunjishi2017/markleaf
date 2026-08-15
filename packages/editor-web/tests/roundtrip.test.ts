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
