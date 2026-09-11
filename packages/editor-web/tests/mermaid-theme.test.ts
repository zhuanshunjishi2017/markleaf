import { afterEach, expect, it, vi } from 'vitest'

const mermaidMock = vi.hoisted(() => ({
  initialize: vi.fn(),
  render: vi.fn(async (_id: string, _source: string) => ({ svg: '<svg></svg>' })),
}))

vi.mock('mermaid', () => ({
  default: mermaidMock,
}))

import { renderMermaidInHtml } from '../src/mermaid'

const mermaidPlaceholder = '<div class="markleaf-mermaid" data-mermaid="1">graph TD\nA--&gt;B</div>'

function setTheme(colors: Record<string, string>): void {
  for (const [name, value] of Object.entries(colors)) {
    document.documentElement.style.setProperty(`--${name}`, value)
  }
}

afterEach(() => {
  mermaidMock.initialize.mockClear()
  mermaidMock.render.mockClear()
  document.documentElement.removeAttribute('style')
  document.body.innerHTML = ''
})

it('uses Mermaid complete dark and default palettes and reinitializes after a theme change', async () => {
  setTheme({
    'bg-primary': 'rgb(24, 26, 30)',
    'bg-secondary': 'rgb(31, 34, 39)',
    'bg-hover': 'rgb(43, 47, 54)',
    'bg-selected': 'rgb(70, 75, 84)',
    'text-primary': 'rgb(232, 234, 237)',
    'text-secondary': 'rgb(177, 181, 187)',
  })

  await renderMermaidInHtml(mermaidPlaceholder)

  expect(mermaidMock.initialize).toHaveBeenCalledTimes(1)
  expect(mermaidMock.initialize).toHaveBeenLastCalledWith(expect.objectContaining({
    theme: 'dark',
    themeVariables: expect.objectContaining({
      fontFamily: expect.any(String),
    }),
  }))
  expect(mermaidMock.initialize.mock.lastCall?.[0].themeVariables)
    .not.toHaveProperty('primaryColor')

  setTheme({
    'bg-primary': 'rgb(250, 250, 250)',
    'bg-secondary': 'rgb(255, 255, 255)',
    'bg-hover': 'rgb(242, 242, 242)',
    'bg-selected': 'rgb(210, 210, 210)',
    'text-primary': 'rgb(32, 32, 32)',
    'text-secondary': 'rgb(96, 96, 96)',
  })

  await renderMermaidInHtml(mermaidPlaceholder)

  expect(mermaidMock.initialize).toHaveBeenCalledTimes(2)
  expect(mermaidMock.initialize).toHaveBeenLastCalledWith(expect.objectContaining({
    theme: 'default',
    themeVariables: expect.objectContaining({
      fontFamily: expect.any(String),
    }),
  }))
})

it('allows print styles to request Mermaid neutral theme', async () => {
  await renderMermaidInHtml(mermaidPlaceholder, 'neutral')

  expect(mermaidMock.initialize).toHaveBeenLastCalledWith(expect.objectContaining({
    theme: 'neutral',
  }))
})

function mountSoftDarkDocument(): HTMLElement {
  setTheme({ 'bg-primary': '#1e1e1e' })
  const root = document.createElement('div')
  root.className = 'markleaf-document'
  document.body.append(root)
  const colors = { surface: '#222224', node: '#2c2c2f', 'node-border': '#62626a', line: '#92929a', text: '#dedee3' }
  for (const [name, color] of Object.entries(colors)) root.style.setProperty(`--ml-mermaid-${name}`, color)
  return root
}

it('uses document neutral colors per diagram while preserving the complete series palette and source', async () => {
  const root = mountSoftDarkDocument()
  const source = 'graph TD\nA[文档]-->B[保存]\nclassDef warning fill:#a44232,color:#fff\nclass B warning'
  await renderMermaidInHtml(`<div class="markleaf-mermaid" data-mermaid="1">${source}</div>`)

  const renderedSource = mermaidMock.render.mock.lastCall![1]
  const configLine = renderedSource.split('\n')[1]!
  const { themeVariables } = JSON.parse(configLine.slice('config: '.length))
  expect(themeVariables).toMatchObject({ nodeBkg: '#2c2c2f', nodeBorder: '#62626a', edgeLabelBackground: '#222224', signalColor: '#92929a' })
  for (const name of ['primaryColor', 'secondaryColor', 'pie1', 'git0', 'cScale0']) expect(themeVariables).not.toHaveProperty(name)
  expect(renderedSource.slice(renderedSource.indexOf('\n---\n') + 5)).toBe(source)
  expect(root.childNodes).toHaveLength(0)
  expect(mermaidMock.initialize.mock.lastCall?.[0].themeVariables).not.toHaveProperty('nodeBkg')
})

it('removes dark display colors when switching to light and when the host opts out', async () => {
  const root = mountSoftDarkDocument()
  await renderMermaidInHtml(mermaidPlaceholder)
  expect(mermaidMock.render.mock.lastCall?.[1]).toMatch(/^---\nconfig:/)

  setTheme({ 'bg-primary': '#ffffff' })
  await renderMermaidInHtml(mermaidPlaceholder)
  expect(mermaidMock.render.mock.lastCall?.[1]).toBe('graph TD\nA-->B')

  setTheme({ 'bg-primary': '#1e1e1e' })
  root.removeAttribute('style')
  await renderMermaidInHtml(mermaidPlaceholder)
  expect(mermaidMock.render.mock.lastCall?.[1]).toBe('graph TD\nA-->B')
})

it('leaves explicit export and document themes in control of their colors', async () => {
  const root = mountSoftDarkDocument()
  await renderMermaidInHtml(mermaidPlaceholder, 'dark')
  expect(mermaidMock.render.mock.lastCall?.[1]).toBe('graph TD\nA-->B')
  root.style.setProperty('--ml-mermaid-theme', 'dark')
  await renderMermaidInHtml(mermaidPlaceholder)
  expect(mermaidMock.render.mock.lastCall?.[1]).toBe('graph TD\nA-->B')
})

it.each([
  '---\nconfig:\n  theme: default\n---\ngraph TD\nA-->B',
  '%%{init: {"theme":"forest"}}%%\ngraph TD\nA-->B',
  '%%{initialize: {"themeVariables":{"primaryColor":"#ffffff"}}}%%\ngraph TD\nA-->B',
])('preserves diagram-owned theme configuration: %s', async source => {
  mountSoftDarkDocument()
  await renderMermaidInHtml(`<div class="markleaf-mermaid" data-mermaid="1">${source}</div>`)
  expect(mermaidMock.render.mock.lastCall?.[1]).toBe(source)
})
