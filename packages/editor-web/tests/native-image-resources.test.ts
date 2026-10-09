import { afterEach, expect, it } from 'vitest'
import { createEditor, executeEditorCommand, getMarkdown, setImageResourceResolver } from '@markleaf/editor-core'
import { nativeImageResources } from '../src/native-capabilities'

const editors: ReturnType<typeof createEditor>[] = []
afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  setImageResourceResolver()
  document.body.replaceChildren()
})

function create(markdown: string) {
  const mount = document.createElement('div')
  document.body.append(mount)
  setImageResourceResolver(nativeImageResources)
  const editor = createEditor(mount, markdown)
  editors.push(editor)
  return { editor, mount }
}

it('loads a Windows image through the native asset host without changing its Markdown path', () => {
  const { editor, mount } = create('![diagram](C:/Pictures/my%20image.png)')
  const image = mount.querySelector('img')!
  expect(image.getAttribute('src')).toBe('https://assets.local/image?path=C%3A%2FPictures%2Fmy%20image.png')
  expect(image.getAttribute('data-markleaf-path')).toBe('C:/Pictures/my%20image.png')
  expect(getMarkdown(editor)).toContain('![diagram](C:/Pictures/my%20image.png)')
  expect(nativeImageResources.originalPath!(image.getAttribute('src')!)).toBe('C:/Pictures/my image.png')
})

it('resolves a native image insertion while preserving the original host command path', () => {
  const { editor, mount } = create('')
  expect(executeEditorCommand(editor, 'insertImage', 'C:/Pictures/image.png\npasted image')).toBe(true)
  expect(mount.querySelector('img')?.getAttribute('src')).toBe('https://assets.local/image?path=C%3A%2FPictures%2Fimage.png')
  expect(getMarkdown(editor)).toContain('![pasted image](C:/Pictures/image.png)')
})
