import { afterEach, beforeEach, expect, it, vi } from 'vitest'

const html = '<div class="markleaf-mermaid" data-mermaid="1">graph TD\nA-->B</div>'
const svg = { svg: '<svg xmlns="http://www.w3.org/2000/svg"><text>ready</text></svg>' }
let resolveModule: (value: { default: typeof mermaid }) => void
let rejectModule: (reason: unknown) => void
const loaded = vi.fn()
const mermaid = { initialize: vi.fn(), render: vi.fn() }

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  loaded.mockClear()
  mermaid.initialize.mockClear()
  mermaid.render.mockReset().mockResolvedValue(svg)
  vi.doMock('mermaid', () => {
    loaded()
    return new Promise<{ default: typeof mermaid }>((resolve, reject) => { resolveModule = resolve; rejectModule = reject })
  })
})

afterEach(async () => {
  resolveModule?.({ default: mermaid })
  await vi.advanceTimersByTimeAsync(0)
  vi.doUnmock('mermaid')
  vi.useRealTimers()
  document.body.replaceChildren()
})

it('gives cold module preparation a separate budget from each diagram execution', async () => {
  const { renderMermaidInHtml } = await import('../src/mermaid')
  mermaid.render.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve(svg), 800)))
  const result = renderMermaidInHtml(html, 'default', true)
  await vi.waitFor(() => expect(loaded).toHaveBeenCalledOnce())
  await vi.advanceTimersByTimeAsync(1800)
  expect(mermaid.render).not.toHaveBeenCalled()
  resolveModule({ default: mermaid })
  await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledOnce())
  await vi.advanceTimersByTimeAsync(800)
  expect(await result).toContain('<svg')
})

it('bounds a shared preparation timeout and permits a later request once the module arrives', async () => {
  const { renderMermaidInHtml } = await import('../src/mermaid')
  const result = renderMermaidInHtml(html + html, 'default', true).catch(error => error)
  await vi.waitFor(() => expect(loaded).toHaveBeenCalledOnce())
  await vi.advanceTimersByTimeAsync(10001)
  // Do not await an indefinitely stalled old implementation on the red run.
  let settled = false
  void result.then(() => { settled = true })
  await vi.advanceTimersByTimeAsync(0)
  expect(settled).toBe(true)
  expect(await result).toMatchObject({ reason: 'load-timeout' })
  expect(mermaid.render).not.toHaveBeenCalled()
  resolveModule({ default: mermaid })
  await vi.advanceTimersByTimeAsync(0)
  const retry = renderMermaidInHtml(html, 'default', true)
  await vi.advanceTimersByTimeAsync(1)
  expect(await retry).toContain('<svg')
})

it('retains the module loading failure and reports it separately from invalid diagram text', async () => {
  const { renderMermaidInHtml } = await import('../src/mermaid')
  const result = renderMermaidInHtml(html, 'default', true).catch(error => error)
  await vi.waitFor(() => expect(loaded).toHaveBeenCalledOnce())
  const cause = new Error('Failed to fetch local Mermaid module')
  rejectModule(cause)
  const failure = await result
  expect(failure).toMatchObject({ reason: 'load-error' })
  expect(failure.cause).toBeInstanceOf(Error)
  // Vitest wraps a rejected mock factory once; retain the original error chain.
  expect(failure.cause.cause ?? failure.cause).toBe(cause)
  expect(await renderMermaidInHtml(html, 'default')).toContain('图表组件加载失败')
  expect(mermaid.render).not.toHaveBeenCalled()
})
