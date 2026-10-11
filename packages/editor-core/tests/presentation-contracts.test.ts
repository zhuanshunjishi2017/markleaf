import { afterEach, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { TextSelection } from '@tiptap/pm/state'
import { createEditor } from '../src/editor'
import { assignHeadingAnchors } from '../src/outline'
import { resolveTypographyStyle } from '../src/typography'
import { getImageResourcePath, refreshImageResources, setImageResourceResolver } from '../src/image-resources'

afterEach(() => { setImageResourceResolver(); document.body.innerHTML = '' })

it('keeps inline math at the font size of its containing line', () => {
  const css = readFileSync(resolve(import.meta.dirname, '../src/styles.css'), 'utf8')
  expect(css).toMatch(/\.markleaf-math-inline\s*\{[^}]*font-size:\s*1em/s)
  expect(css).toMatch(/\.markleaf-math-inline\s*>\s*\.katex\s*\{[^}]*font-size:\s*1em/s)
})

it('keeps wide tables inside the document width and wraps cell content', () => {
  const css = readFileSync(resolve(import.meta.dirname, '../../styles/base.css'), 'utf8')
  expect(css).toMatch(/\.markleaf-document table\s*\{[^}]*max-width:\s*100%/s)
  expect(css).toMatch(/\.markleaf-document table\s*\{[^}]*width:\s*max-content/s)
  expect(css).toMatch(/\.markleaf-document table\s*\{[^}]*table-layout:\s*auto/s)
  expect(css).toMatch(/\.markleaf-document th,\s*\n\.markleaf-document td\s*\{[^}]*overflow-wrap:\s*anywhere/s)
})

it('paints selected table cells with a themeable overlay', () => {
  const css = readFileSync(resolve(import.meta.dirname, '../../styles/base.css'), 'utf8')
  expect(css).toMatch(/\.ProseMirror (?:td|th)\.selectedCell\s*\{[^}]*background-color:/s)
})

it('hides native text ranges while a CellSelection owns table highlighting', () => {
  const css = readFileSync(resolve(import.meta.dirname, '../../styles/base.css'), 'utf8')
  expect(css).toMatch(/markleaf-cell-selection-locked[^}]*td::selection[\s\S]*?background:\s*transparent\s*!important/s)
  expect(css).toMatch(/markleaf-cell-selection-locked[^}]*td \*::selection[\s\S]*?background:\s*transparent\s*!important/s)
})

it('hides the native drag selection while the editor-owned highlight is active', () => {
  const css = readFileSync(resolve(import.meta.dirname, '../src/styles.css'), 'utf8')
  expect(css).toMatch(/\.markleaf-managed-selection-active \.markleaf-document::selection[\s\S]*?background:\s*transparent\s*!important/s)
  expect(css).toMatch(/\.markleaf-managed-selection-active \.markleaf-document \*::selection[\s\S]*?-webkit-text-fill-color:\s*inherit\s*!important/s)
  expect(css).toMatch(/\.markleaf-managed-selection-active \.markleaf-document,\s*\n\.markleaf-managed-selection-active \.markleaf-document \*\s*\{[\s\S]*?user-select:\s*none\s*!important/s)
})

it('keeps the previous themed highlight when a table-edge range cannot be painted', () => {
  const originalCSS = (window as unknown as { CSS?: unknown }).CSS
  const originalHighlight = (window as unknown as { Highlight?: unknown }).Highlight
  const registry = new Map<string, unknown>()
  let addCount = 0
  class TestHighlight {
    ranges: Range[] = []
    clear() { this.ranges = [] }
    add(range: Range) {
      addCount += 1
      if (addCount > 1) throw new Error('simulated WebKit table-edge range failure')
      this.ranges.push(range)
    }
  }
  Object.defineProperty(window, 'CSS', { configurable: true, value: { highlights: registry } })
  Object.defineProperty(window, 'Highlight', { configurable: true, value: TestHighlight })

  const mount = document.createElement('div')
  document.body.appendChild(mount)
  const editor = createEditor(mount, '| left | right |\n| --- | --- |', false, { themedVisualSelection: true })
  try {
    const cells: number[] = []
    editor.state.doc.descendants((node, pos) => {
      if (node.type.name !== 'tableCell' && node.type.name !== 'tableHeader') return true
      cells.push(pos)
      return false
    })
    const firstText = cells[0]! + 2
    const firstTextEnd = firstText + 2
    const secondCellStart = cells[1]! + 2
    editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc, firstText, firstTextEnd)))
    const firstHighlight = registry.get('markleaf-selection') as TestHighlight
    expect(firstHighlight.ranges).toHaveLength(1)

    // The next update represents the pointer landing exactly at the next
    // cell's boundary. WebKit may reject this range for one frame; the
    // previous valid highlight must stay visible instead of being cleared.
    editor.view.dispatch(editor.state.tr.setSelection(
      TextSelection.create(editor.state.doc, firstText, secondCellStart),
    ))

    const currentHighlight = registry.get('markleaf-selection') as TestHighlight
    expect(currentHighlight).toBe(firstHighlight)
    expect(currentHighlight.ranges).toHaveLength(1)
  } finally {
    editor.destroy()
    if (originalCSS === undefined) delete (window as unknown as { CSS?: unknown }).CSS
    else Object.defineProperty(window, 'CSS', { configurable: true, value: originalCSS })
    if (originalHighlight === undefined) delete (window as unknown as { Highlight?: unknown }).Highlight
    else Object.defineProperty(window, 'Highlight', { configurable: true, value: originalHighlight })
  }
})

it('assigns nonempty unique anchors when titles and generated suffixes collide', () => {
  document.body.innerHTML = '<h1>Title</h1><h2>Title</h2><h3>Title-1</h3><h6>???</h6>'
  assignHeadingAnchors(document.body)
  expect([...document.querySelectorAll('h1,h2,h3,h6')].map(node => node.id)).toEqual(['title', 'title-1', 'title-1-1', 'heading'])
})

it('refreshes resolved image URLs without replacing original Markdown paths', () => {
  document.body.innerHTML = '<img data-markleaf-path="./图片.png" src="">'
  const image = document.querySelector('img')!
  setImageResourceResolver({ resolve: path => `https://resource.invalid/${encodeURIComponent(path)}` })
  refreshImageResources(document.body)
  expect(image.src).toContain('https://resource.invalid/')
  expect(getImageResourcePath(image)).toBe('./图片.png')
  setImageResourceResolver({ resolve: () => 'blob:new-resource' })
  refreshImageResources(document.body)
  expect(image.src).toBe('blob:new-resource')
  expect(getImageResourcePath(image)).toBe('./图片.png')
})

it('resolves parent typography before child typography and includes a cycle only once', () => {
  const result = resolveTypographyStyle('child', [
    { id: 'child', css: '/* @depends: parent */ .child {}' },
    { id: 'parent', css: '.parent {}', dependsOn: 'child' },
  ])
  expect(result.rootClass).toBe('markleaf-style-parent markleaf-style-child')
  expect(result.css).toBe('.parent {}\n/* @depends: parent */ .child {}')
})
