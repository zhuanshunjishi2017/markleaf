import { afterEach, expect, it, vi } from 'vitest'
import { exportMermaidImage } from '../src/mermaid-image-export'

function fixture() {
  document.body.innerHTML = '<div class="markleaf-mermaid" style="background-color: rgb(20, 30, 40); color: rgb(240, 240, 240)"><div class="markleaf-mermaid-view"><svg xmlns="http://www.w3.org/2000/svg" viewBox="-10 -20 320 180"><style>.label{fill:red}</style><g><text class="label">中文 label</text></g></svg></div><button>edit</button></div>'
  return document.querySelector('.markleaf-mermaid')!
}
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); document.body.innerHTML = '' })

it('exports a self contained SVG with full viewBox, background and no editor controls', async () => {
  const output = await exportMermaidImage(fixture(), 'svg')
  const xml = new DOMParser().parseFromString(new TextDecoder().decode(Uint8Array.from(atob(output.base64), c => c.charCodeAt(0))), 'image/svg+xml')
  expect(xml.querySelector('parsererror')).toBeNull()
  expect(xml.documentElement.getAttribute('viewBox')).toBe('-10 -20 320 180')
  expect(xml.querySelector('rect')?.getAttribute('fill')).toBe('rgb(20, 30, 40)')
  expect(xml.querySelector('text')?.textContent).toBe('中文 label')
  expect(xml.querySelector('button')).toBeNull()
  expect([output.width, output.height]).toEqual([320, 180])
})

it('PNG is 2x full viewBox independently of the viewport', async () => {
  const draw = vi.fn()
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage: draw } as any)
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/png;base64,iVBORw0KGgo=')
  vi.stubGlobal('Image', class { onload?: () => void; onerror?: () => void; set src(_value: string) { queueMicrotask(() => this.onload?.()) } })
  const output = await exportMermaidImage(fixture(), 'png')
  expect([output.width, output.height, output.mediaType]).toEqual([640, 360, 'image/png'])
  expect(draw).toHaveBeenCalledWith(expect.anything(), 0, 0, 640, 360)
  expect(output.base64.length).toBeGreaterThan(0)
})

it('rejects pending, failed or invalid size renders', async () => {
  const element = fixture()
  element.querySelector('svg')!.remove()
  await expect(exportMermaidImage(element, 'svg')).rejects.toThrow()
  fixture().querySelector('svg')!.setAttribute('viewBox', '0 0 0 10')
  await expect(exportMermaidImage(document.querySelector('.markleaf-mermaid')!, 'svg')).rejects.toThrow()
})
