import { afterEach, describe, expect, it } from 'vitest'
import { SourceEditor } from '../src/source-editor'
import { createEditor, getMarkdown, replaceEditorDocument } from '../src/editor'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
// 模拟宿主注入的排版样式：base.css 曾在源码模式下隐藏原生光标，
// 该交互正是“光标不可见”回归的复现条件。
const baseCss = readFileSync(resolve(import.meta.dirname, '../../MarkLeaf/Resources/Styles/base.css'), 'utf8')
const stylesCss = readFileSync(resolve(import.meta.dirname, '../src/styles.css'), 'utf8')

const sources: SourceEditor[] = []
const editorsForSourceTests: ReturnType<typeof createEditor>[] = []

afterEach(() => {
  for (const source of sources.splice(0)) source.destroy()
  for (const editor of editorsForSourceTests.splice(0)) editor.destroy()
  document.body.innerHTML = ''
})

describe('source editor', () => {
  it('keeps a visible caret in source mode', () => {
    const hostStyles = document.createElement('style')
    hostStyles.textContent = `${baseCss}\n${stylesCss}`
    document.head.append(hostStyles)
    const rules = (Array.from(hostStyles.sheet?.cssRules ?? []) as CSSRule[]).filter(
      (rule): rule is CSSStyleRule => rule instanceof CSSStyleRule
    )

    // jsdom 无法按 !important 计算 caret-color，这里直接校验样式契约：
    // 源码编辑器不得再隐藏原生光标，且必须提供可见的 caret 颜色。
    const caretHidingRules = rules.filter(rule =>
      rule.selectorText.includes('.cm-content') &&
      rule.style.getPropertyValue('caret-color').trim() === 'transparent'
    )
    expect(caretHidingRules).toHaveLength(0)

    const visibleCaretRules = rules.filter(rule =>
      rule.selectorText.includes('#source-editor') &&
      rule.selectorText.includes('.cm-content') &&
      !['', 'transparent', 'auto'].includes(rule.style.getPropertyValue('caret-color').trim())
    )
    expect(visibleCaretRules.length).toBeGreaterThan(0)
    hostStyles.remove()
  })

  it('finds and replaces Markdown without losing source text', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const source = new SourceEditor(parent, '# Title\n\nleaf leaf', () => {})
    sources.push(source)

    expect(source.find('leaf', true, false)).toEqual({ current: 1, total: 2 })
    expect(source.replaceCurrent('leaf', 'branch', true, false)).toEqual({ current: 1, total: 1 })
    expect(source.replaceAll('leaf', 'tree', true, false)).toBe(1)
    expect(source.getText()).toBe('# Title\n\nbranch tree')
  })

  it('preserves Markdown across visual and source editor reconstruction', () => {
    const visualParent = document.createElement('div')
    const sourceParent = document.createElement('div')
    document.body.append(visualParent, sourceParent)
    const initialVisual = createEditor(visualParent, '# Title\n\n**bold**')
    editorsForSourceTests.push(initialVisual)
    const source = new SourceEditor(sourceParent, getMarkdown(initialVisual), () => {})
    sources.push(source)

    editorsForSourceTests.splice(editorsForSourceTests.indexOf(initialVisual), 1)
    const visual = replaceEditorDocument(initialVisual, visualParent, source.getText())
    editorsForSourceTests.push(visual)
    expect(getMarkdown(visual)).toContain('# Title')
    expect(getMarkdown(visual)).toContain('**bold**')
  })

  it('preserves consecutive blank lines in plain text exactly', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const content = 'first\n\n\nsecond\n'
    const source = new SourceEditor(parent, content, () => {})
    sources.push(source)

    expect(source.getText()).toBe(content)
  })

  it('exports and replaces the selected Markdown source', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const source = new SourceEditor(parent, '**bold**', () => {})
    sources.push(source)
    source.view.dispatch({ selection: { anchor: 0, head: 8 } })

    expect(source.getSelectedText()).toBe('**bold**')
    expect(source.replaceSelection('plain')).toBe(true)
    expect(source.getText()).toBe('plain')
  })

  it('deletes only a non-empty source selection', () => {
    const parent = document.createElement('div')
    document.body.append(parent)
    const source = new SourceEditor(parent, 'leaf', () => {})
    sources.push(source)

    expect(source.deleteSelection()).toBe(false)
    source.view.dispatch({ selection: { anchor: 0, head: 4 } })
    expect(source.deleteSelection()).toBe(true)
    expect(source.getText()).toBe('')
  })
})
