import { afterEach, describe, expect, it, vi } from 'vitest'
import { NodeSelection } from '@tiptap/pm/state'
import { createEditor, expandSourceEditor, setMarkdownEditingSettings } from '../src/index'

const cleanup: Array<() => void> = []
function setup(markdown: string) {
  document.body.innerHTML = '<main id="editor"></main><footer><span id="count"></span></footer>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const contextMenu = vi.fn()
  const editor = createEditor(mount, markdown, false, {
    externalHistory: true,
    themedVisualSelection: true,
    sourceContextMenu: contextMenu,
  })
  editor.view.setProps({ handleScrollToSelection: () => true })
  cleanup.push(() => editor.destroy())
  return { editor, mount, contextMenu }
}
afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  setMarkdownEditingSettings({})
  document.body.innerHTML = ''
  vi.restoreAllMocks()
})

describe('formula expanded source context menu', () => {
  it('fires the source context menu without corrupting the formula document node', () => {
    const { editor, contextMenu } = setup('before $T(n) = T(2n)$ after')
    const position = 1 + 'before '.length
    const formula = editor.state.doc.nodeAt(position)
    expect(formula?.type.name).toBe('mathInline')
    expect(formula?.textContent).toBe('T(n) = T(2n)')
    editor.commands.setNodeSelection(position)
    expect(expandSourceEditor(editor, position, 'mathInline')).toBe(true)

    const source = document.querySelector<HTMLElement>('.markleaf-expanded-source-editor')!
    expect(source).not.toBeNull()
    // 右键浮层源码：与用户报障一致的交互序列（右键 mousedown → contextmenu）。
    source.dispatchEvent(new MouseEvent('mousedown', { button: 2, bubbles: true, cancelable: true }))
    source.dispatchEvent(new MouseEvent('contextmenu', { button: 2, bubbles: true, cancelable: true }))

    expect(contextMenu).toHaveBeenCalledTimes(1)
    expect(contextMenu.mock.calls[0]?.[0]).toMatchObject({ clientX: 0, clientY: 0 })

    const after = editor.state.doc.nodeAt(position)
    expect(after?.type.name).toBe('mathInline')
    expect(after?.textContent).toBe('T(n) = T(2n)')
  })
})
