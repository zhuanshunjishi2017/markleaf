import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

function makeEditor(markdown: string): ReturnType<typeof createEditor> {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  editor.commands.setTextSelection(1)
  return editor
}

describe('heading level boundaries', () => {
  it('does not promote a level-one heading to a paragraph', () => {
    const editor = makeEditor('# 一级标题')

    const result = executeEditorCommand(editor, 'promoteHeading')

    expect(result).toBe(false)
    expect(getMarkdown(editor)).toContain('# 一级标题')
  })

  it('does not demote a level-six heading to a paragraph', () => {
    const editor = makeEditor('###### 六级标题')

    const result = executeEditorCommand(editor, 'demoteHeading')

    expect(result).toBe(false)
    expect(getMarkdown(editor)).toContain('###### 六级标题')
  })
})
