import { afterEach, expect, it, vi } from 'vitest'

const mermaidMock = vi.hoisted(() => ({
  initialize: vi.fn(),
  render: vi.fn(async () => ({ svg: '<svg></svg>' })),
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
