import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createEditor } from '../src/editor'
import { renderMermaidInHtml, rerenderMermaidElements } from '../src/mermaid'

const mermaid = vi.hoisted(() => ({ initialize: vi.fn(), render: vi.fn() }))
vi.mock('mermaid', () => ({ default: mermaid }))
const editors: ReturnType<typeof createEditor>[] = []
const finishPending: Array<() => void> = []
const svg = (text = 'diagram') => ({ svg: `<svg xmlns="http://www.w3.org/2000/svg"><text>${text}</text></svg>` })
const markdown = (source: string) => '```mermaid\n' + source + '\n```'
// Include the task boundary used to let the browser paint between diagrams.
const flush = () => vi.advanceTimersByTimeAsync(1)

function create(sources = ['graph TD\nA-->B']) {
  const mount = document.createElement('div')
  document.body.append(mount)
  const editor = createEditor(mount, sources.map(markdown).join('\n\n'), true)
  editors.push(editor)
  return { editor, mount }
}

function pending() {
  let resolve!: (value: ReturnType<typeof svg>) => void
  const promise = new Promise<ReturnType<typeof svg>>(done => { resolve = done })
  finishPending.push(() => resolve(svg()))
  return { promise, resolve }
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date', 'setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame'] })
  mermaid.render.mockReset().mockResolvedValue(svg())
  mermaid.initialize.mockClear()
})
afterEach(async () => {
  for (const editor of editors.splice(0)) if (!editor.isDestroyed) editor.destroy()
  finishPending.splice(0).forEach(finish => finish())
  await flush()
  vi.useRealTimers()
  document.body.replaceChildren()
  document.documentElement.style.removeProperty('--bg-primary')
})

describe('Mermaid render lifecycle', () => {
  it('lets UI tasks run between queued diagrams', async () => {
    let callsAtUiTask = 0
    let firstDiagramVisible = false
    mermaid.render.mockImplementationOnce(() => {
      window.setTimeout(() => {
        callsAtUiTask = mermaid.render.mock.calls.length
        firstDiagramVisible = mount.querySelector('svg') !== null
      }, 0)
      return Promise.resolve(svg('first'))
    })
    const { mount } = create(['graph TD\nA-->B', 'graph TD\nC-->D'])
    await vi.waitFor(() => expect(mount.querySelectorAll('svg')).toHaveLength(2))
    expect(callsAtUiTask).toBe(1)
    expect(firstDiagramVisible).toBe(true)
  })

  it('coalesces initial and in-flight invalidations but allows an explicit completed rerender', async () => {
    const first = pending()
    mermaid.render.mockReturnValueOnce(first.promise)
    const { mount } = create()
    rerenderMermaidElements(mount)
    rerenderMermaidElements(mount)
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    rerenderMermaidElements(mount)
    await flush()
    expect(mermaid.render).toHaveBeenCalledTimes(1)
    first.resolve(svg('first'))
    await flush()
    expect(mount.querySelector('svg text')?.textContent).toBe('first')
    rerenderMermaidElements(mount)
    await flush()
    expect(mermaid.render).toHaveBeenCalledTimes(2)
  })

  it('gives each queued diagram its own execution timeout', async () => {
    mermaid.render.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve(svg()), 800)))
    const { mount } = create(['graph TD\nA-->B', 'graph TD\nC-->D', 'graph TD\nE-->F'])
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    await vi.advanceTimersByTimeAsync(800)
    expect(mermaid.render).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(800)
    expect(mermaid.render).toHaveBeenCalledTimes(3)
    await vi.advanceTimersByTimeAsync(800)
    expect(mount.querySelectorAll('svg')).toHaveLength(3)
    expect(mount.querySelector('.markleaf-mermaid-message-error')).toBeNull()
  })

  it('reports the active timeout and resumes queued diagrams after the renderer is released', async () => {
    const stalled = pending()
    mermaid.render.mockReturnValueOnce(stalled.promise)
    const { mount } = create(['graph TD\nA-->B', 'graph TD\nC-->D'])
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    await vi.advanceTimersByTimeAsync(1001)
    expect(mount.querySelectorAll('.markleaf-mermaid-message-error')).toHaveLength(1)
    expect(mermaid.render).toHaveBeenCalledTimes(1)
    stalled.resolve(svg('late'))
    await flush()
    expect(mount.querySelectorAll('svg')).toHaveLength(1)
    expect(mount.querySelector('svg text')?.textContent).toBe('diagram')
    expect(mermaid.render).toHaveBeenCalledTimes(2)
    const next = create(['graph TD\nE-->F'])
    await flush()
    expect(next.mount.querySelector('svg')).not.toBeNull()
  })

  it('bounds the shared wait for a stuck renderer and lets a failed node retry after release', async () => {
    const stalled = pending()
    mermaid.render.mockReturnValueOnce(stalled.promise)
    const { mount } = create(['graph TD\nA-->B', 'graph TD\nC-->D', 'graph TD\nE-->F'])
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    await vi.advanceTimersByTimeAsync(1001)
    expect(mount.querySelectorAll('.markleaf-mermaid-message-error')).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(4900)
    expect(mount.querySelectorAll('.markleaf-mermaid-message-error')).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(101)
    const messages = [...mount.querySelectorAll('.markleaf-mermaid-message-error')]
    expect(messages).toHaveLength(3)
    expect(messages[0]?.textContent).toContain('渲染超时')
    expect(messages[1]?.textContent).toContain('尚未渲染')
    expect(messages[2]?.textContent).toContain('尚未渲染')
    expect(mermaid.render).toHaveBeenCalledTimes(1)
    stalled.resolve(svg('late'))
    await flush()
    expect(mount.querySelector('svg')).toBeNull()
    messages[1]!.querySelector('button')!.click()
    await flush()
    expect(mount.querySelectorAll('svg')).toHaveLength(1)
    expect(mermaid.render).toHaveBeenCalledTimes(2)
  })

  it('drops a queued diagram destroyed while waiting for a timed-out renderer', async () => {
    const stalled = pending()
    mermaid.render.mockReturnValueOnce(stalled.promise)
    const { editor } = create(['graph TD\nA-->B', 'graph TD\nC-->D'])
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    await vi.advanceTimersByTimeAsync(1001)
    editor.destroy()
    stalled.resolve(svg())
    await flush()
    expect(mermaid.render).toHaveBeenCalledTimes(1)
  })

  it('does not let an old request overwrite newer source', async () => {
    const old = pending()
    mermaid.render.mockReturnValueOnce(old.promise).mockResolvedValue(svg('new'))
    const { editor, mount } = create()
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    editor.commands.setContent(markdown('graph TD\nC-->D'), { contentType: 'markdown' })
    await flush()
    old.resolve(svg('old'))
    await flush()
    expect(mermaid.render).toHaveBeenCalledTimes(2)
    expect(mermaid.render.mock.calls[1]?.[1]).toContain('C-->D')
    expect(mount.querySelector('svg text')?.textContent).toBe('new')
  })

  it('drops queued nodes when their editor is destroyed', async () => {
    const active = pending()
    mermaid.render.mockReturnValueOnce(active.promise)
    const { editor } = create(['graph TD\nA-->B', 'graph TD\nC-->D'])
    await vi.waitFor(() => expect(mermaid.render).toHaveBeenCalledTimes(1))
    editor.destroy()
    active.resolve(svg())
    await flush()
    expect(mermaid.render).toHaveBeenCalledTimes(1)
  })

  it('shows a node error when document theme colors cannot be resolved', async () => {
    document.documentElement.style.setProperty('--bg-primary', '#000000')
    const { mount } = create()
    // This CSS color space cannot be converted to Mermaid's hex colors.
    const root = mount.querySelector<HTMLElement>('.markleaf-document')!
    root.style.setProperty('--ml-mermaid-surface', '#111111')
    root.style.setProperty('--ml-mermaid-node', 'color(display-p3 1 0 0)')
    await flush()
    expect(mount.querySelector('.markleaf-mermaid-message-error')).not.toBeNull()
    expect(mermaid.render).not.toHaveBeenCalled()
  })

  it('keeps export failures strict and permits later valid jobs', async () => {
    mermaid.render.mockRejectedValueOnce(new Error('invalid diagram'))
    const rejected = expect(renderMermaidInHtml('<div class="markleaf-mermaid" data-mermaid="1">invalid</div>', 'default', true)).rejects.toThrow('invalid diagram')
    await flush()
    await rejected
    const pendingExport = renderMermaidInHtml('<div class="markleaf-mermaid" data-mermaid="1">graph TD\nA-->B</div>', 'default', true)
    await flush()
    const exported = await pendingExport
    expect(exported).toContain('<svg')
    expect(exported).not.toContain('markleaf-mermaid-message-error')
  })
})
