import { afterEach, expect, it, vi } from 'vitest'
import { createEditor } from '../src/editor'
import { setMermaidStrings } from '../src/mermaid'
import { sharedEditorStrings } from '../src/shared-editor-strings'

vi.mock('mermaid', () => ({
  default: {
    initialize() {},
    render: () => new Promise<never>(() => {}),
  },
}))

const editors: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  vi.useRealTimers()
  document.body.innerHTML = ''
  setMermaidStrings(sharedEditorStrings('zh-Hans', 'ctrl'))
})

it('shows the localized timeout instead of a syntax error when rendering stalls', async () => {
  vi.useFakeTimers()
  setMermaidStrings(sharedEditorStrings('en', 'meta'))
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, '```mermaid\ngraph TD\n  A-->B\n```')
  editors.push(editor)

  // renderMermaidSvgInto installs its timeout only after the lazy Mermaid
  // module import resolves. Let that import settle before advancing the fake
  // clock, otherwise a busy full-suite worker can advance past a timer that
  // has not been registered yet.
  await vi.dynamicImportSettled()
  await vi.advanceTimersByTimeAsync(1001)

  expect(mount.querySelector('.markleaf-mermaid-message-error')?.textContent)
    .toBe('Mermaid diagram rendering timed out')
})
