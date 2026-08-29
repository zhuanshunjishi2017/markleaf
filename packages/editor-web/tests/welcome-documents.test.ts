import { afterEach, describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { createEditor, getMarkdown } from '../src/editor'

const editors: ReturnType<typeof createEditor>[] = []
const languages = ['zh-Hans', 'zh-Hant', 'en', 'ja']

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

describe('macOS welcome documents', () => {
  for (const language of languages) {
    it(`loads the ${language} tutorial with formatted footnotes and math intact`, () => {
      const markdown = readFileSync(
        resolve(import.meta.dirname, `../../../apps/macos/Welcome/welcome.${language}.md`),
        'utf8',
      )
      const element = document.createElement('div')
      document.body.append(element)
      const editor = createEditor(element, markdown)
      editors.push(editor)

      const output = getMarkdown(editor)
      expect(output).toContain('# ')
      expect(output).toContain('[^1]:')
      expect(output).toContain('**')
      expect(output).toContain('$x^2+y^2$')
      expect(element.querySelector('.markleaf-math-inline .katex')).not.toBeNull()
    })
  }
})
