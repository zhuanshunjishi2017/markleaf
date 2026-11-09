import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, exportEditorSelection } from '../src/index'

const cleanup: Array<() => void> = []
function setup(markdown: string) {
  document.body.innerHTML = '<main id="editor"></main>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
  cleanup.push(() => editor.destroy())
  return editor
}
afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  document.body.innerHTML = ''
})

describe('selection export without a temporary editor', () => {
  it('exports a large mixed selection to markdown/html without spinning up a second editor', () => {
    const editor = setup('# 标题\n\n一段含 **粗体** 与 `行内代码` 的正文。\n\n- 列表项一\n- 列表项二\n\n> 引用\n\n$$E = mc^2$$')
    editor.commands.setTextSelection({ from: 1, to: editor.state.doc.content.size - 1 })
    const before = document.querySelectorAll('.ProseMirror').length

    const export1 = exportEditorSelection(editor)
    const export2 = exportEditorSelection(editor)

    expect(export1.markdown).toContain('粗体')
    expect(export1.markdown).toContain('列表项一')
    expect(export1.html).toContain('<strong>')
    // 幂等：重复导出（恢复快照每 30s）结果一致
    expect(export2.markdown).toBe(export1.markdown)
    // 全程未创建第二个编辑器 DOM
    expect(document.querySelectorAll('.ProseMirror').length).toBe(before)
  })

  it('keeps highlight marks as ==…== and escapes bare double equals', () => {
    // mark 形态：==高亮== 往返不变
    const marked = setup('文本 ==高亮== 尾部')
    marked.commands.setTextSelection({ from: 1, to: marked.state.doc.content.size - 1 })
    expect(exportEditorSelection(marked).markdown).toContain('==高亮==')

    // 字面形态：非 highlight 语法的裸 ==（如行内公式片段 C(a)==b）需要转义，
    // 否则重新解析会被误认为高亮标记。
    const literal = setup('若 C(a)==b 则相等')
    literal.commands.setTextSelection({ from: 1, to: literal.state.doc.content.size - 1 })
    const exported = exportEditorSelection(literal)
    if (exported.markdown.includes('==')) {
      expect(exported.markdown).toContain('\\=')
    }
  })

  it('returns empty for an empty selection', () => {
    const editor = setup('内容')
    editor.commands.setTextSelection(1)
    expect(exportEditorSelection(editor)).toEqual({ text: '', markdown: '', html: '' })
  })
})
