import { afterEach, describe, expect, it } from 'vitest'
import { createEditor, getMarkdown } from '../src/editor'

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
  return editor
}

describe('Markdown emoji rendering', () => {
  it('keeps emoji aliases in Markdown while rendering their visual replacements', () => {
    const editor = makeEditor('hello :smile: :+1: :rocket: :heart: :tada:')

    expect(getMarkdown(editor)).toContain(':smile: :+1: :rocket: :heart: :tada:')
    expect(editor.view.dom.querySelectorAll('.markleaf-emoji')).toHaveLength(5)
    const renderedEmojis = Array.from(editor.view.dom.querySelectorAll<HTMLElement>('.markleaf-emoji'))
      .map(element => element.dataset.emoji)
    expect(renderedEmojis).toContain('😄')
    expect(renderedEmojis).toContain('👍')
    expect(renderedEmojis).toContain('🚀')
  })

  it('decorates the complete alias without leaving a leading colon visible', () => {
    const editor = makeEditor(':smile: after')
    const emoji = editor.view.dom.querySelector<HTMLElement>('.markleaf-emoji')

    expect(emoji).not.toBeNull()
    expect(emoji?.textContent).toBe(':smile:')
    expect(emoji?.nextSibling?.textContent).toBe(' after')
  })

  it('keeps complete alias ranges inside nested blocks', () => {
    const editor = makeEditor('- :smile: item')
    const emoji = editor.view.dom.querySelector<HTMLElement>('li .markleaf-emoji')

    expect(emoji).not.toBeNull()
    expect(emoji?.textContent).toBe(':smile:')
  })

  it('supports all aliases listed for common emotions, gestures, and symbols', () => {
    const aliases = [
      'smile', 'grin', 'laughing', 'joy', 'wink', 'blush', 'sad', 'confused',
      'angry', 'cry', 'sob', 'scream', 'thinking', '+1', 'thumbsup', '-1',
      'ok_hand', 'clap', 'muscle', 'pray', 'wave', 'heart', 'eyes', 'fire',
      'rocket', 'star', 'tada', 'coffee', 'poop',
    ]
    const editor = makeEditor(aliases.map(alias => `:${alias}:`).join(' '))

    expect(editor.view.dom.querySelectorAll('.markleaf-emoji')).toHaveLength(aliases.length)
    expect(getMarkdown(editor)).toContain(':thinking:')
    expect(getMarkdown(editor)).toContain(':+1:')
  })

  it('does not render unknown aliases or aliases inside code', () => {
    const editor = makeEditor('`:smile:`\n\n```\n:rocket:\n```\n\n:unknown:')

    expect(editor.view.dom.querySelectorAll('.markleaf-emoji')).toHaveLength(0)
    expect(getMarkdown(editor)).toContain(':smile:')
    expect(getMarkdown(editor)).toContain(':rocket:')
    expect(getMarkdown(editor)).toContain(':unknown:')
  })
})
