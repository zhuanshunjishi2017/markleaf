import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor, getMarkdown, setImageResourceResolver } from '@markleaf/editor-core'
import { renderExportSnapshot } from '../src/vscode-export'
import { defaultSettings } from '../src/vscode-settings'
import { exportDefaults, isExportOptions } from '../src/vscode-export-options'
import { exportStrings } from '../src/vscode-export-strings'
import { isWebviewMessage } from '../src/vscode-protocol'
import { createExportDialog } from '../src/vscode-export-dialog'
import { generateExportHtml } from '@markleaf/editor-core'

// This suite checks document generation, not browser rasterization or printing.
afterEach(() => { document.body.innerHTML = ''; setImageResourceResolver(); vi.restoreAllMocks() })

describe('VS Code complete-document exports', () => {
  it('renders a full snapshot with local image references, KaTeX and minimal typography without editing the visible document', async () => {
    const markdown = '# 标题 Title\n\nHello **world** $x^2$.\n\n![Picture](assets/my%20image.png)\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\nNote[^n]\n\n[^n]: Footnote body'
    const visible = createEditor(document.createElement('div'), markdown)
    const state = visible.state
    const before = getMarkdown(visible)
    setImageResourceResolver({ resolve: () => 'https://webview.invalid/picture' })
    try {
      const result = await renderExportSnapshot(markdown, 'Title <1>', exportDefaults, defaultSettings, 'en')
      const html = new DOMParser().parseFromString(result.html, 'text/html')
      expect(html.title).toBe('Title <1>')
      expect(html.body.classList.contains('markleaf-style-minimal')).toBe(true)
      expect(html.querySelector('strong')?.textContent).toBe('world')
      expect(html.querySelector('.katex')).not.toBeNull()
      expect(html.querySelector('table')?.textContent).toContain('A')
      expect(html.body.textContent).toContain('Footnote body')
      expect(result.images).toEqual(['assets/my%20image.png'])
      expect(html.querySelector('img')?.getAttribute('src')).toBe('markleaf-export-image:0')
      expect(result.html).toContain('data:font/woff2;base64,')
      expect(result.html).not.toContain('https://webview.invalid')
      expect(visible.state).toBe(state)
      expect(getMarkdown(visible)).toBe(before)
    } finally { visible.destroy() }
  })

  it('keeps selected typography dependencies, page settings and localized labels in standalone HTML', async () => {
    const result = await renderExportSnapshot('# A\n\nParagraph', '文書', { ...exportDefaults, typography: 'print-double', format: 'html', header: '<header>', footer: 'Footer', landscape: true, paperSize: 'Letter' }, defaultSettings, 'ja')
    const html = new DOMParser().parseFromString(result.html, 'text/html')
    expect(html.documentElement.lang).toBe('ja')
    expect(html.body.className).toContain('markleaf-style-print markleaf-style-print-double')
    expect(html.querySelector('.export-header')?.textContent).toBe('<header>')
    expect(html.querySelector('.export-header header')).toBeNull()
    expect(result.html).toContain('Letter landscape')
    expect(result.html).toContain('注記')
  })

  it('refuses malformed formulas instead of reporting a successful export', async () => {
    await expect(renderExportSnapshot('$$\\definitelyInvalidCommand$$', '', exportDefaults, defaultSettings, 'en')).rejects.toThrow('formula or diagram')
  })

  it('preserves literal angle brackets and inline formatting in exported captions', async () => {
    const result = await generateExportHtml({
      rawBodyHtml: '<figure><figcaption class="markleaf-figcaption">**Figure 1**: &lt;sample&gt; &amp; values</figcaption></figure>',
      resolved: { rootClass: 'markleaf-style-minimal', css: '' }, baseCss: '', format: 'html', editorLoc: {},
    })
    const caption = new DOMParser().parseFromString(result, 'text/html').querySelector('figcaption')!
    expect(caption.textContent).toBe('Figure 1: <sample> & values')
    expect(caption.querySelector('strong')?.textContent).toBe('Figure 1')
    expect(caption.querySelector('sample')).toBeNull()
  })

  it('rejects invalid options and ambiguous render responses at the transport boundary', () => {
    expect(isExportOptions(exportDefaults)).toBe(true)
    for (const patch of [{ imageScale: 0 }, { imageMaxHeight: Infinity }, { marginTop: -1 }, { typography: 'unknown' }, { colorTheme: 'vscode' }, { header: '<h1>'.repeat(150) }]) {
      expect(isWebviewMessage({ type: 'export', options: { ...exportDefaults, ...patch }, preview: false })).toBe(false)
    }
    expect(isWebviewMessage({ type: 'exportRendered', requestId: 7, result: { html: '<p>OK</p>', images: [] } })).toBe(true)
    expect(isWebviewMessage({ type: 'exportRendered', requestId: 7, result: { html: '', images: [] }, error: 'failed' })).toBe(false)
    expect(isWebviewMessage({ type: 'exportRendered', requestId: 7, result: { html: '', images: [7] } })).toBe(false)
  })

  it('submits selected options, keeps cancellation separate from closing and localizes the export dialog', () => {
    Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value: function (this: HTMLDialogElement) { this.open = true } })
    Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value: function (this: HTMLDialogElement) { this.open = false } })
    const post = vi.fn()
    const dialog = createExportDialog(post, vi.fn())
    dialog.open(exportDefaults, 'zh-TW')
    expect(document.querySelector('#export-title')?.textContent).toBe('匯出文件')
    const format = document.querySelector<HTMLSelectElement>('#markleaf-export [name=format]')!
    format.value = 'jpg'; format.dispatchEvent(new Event('change'))
    document.querySelector<HTMLFormElement>('#markleaf-export form')!.dispatchEvent(new Event('submit', { cancelable: true }))
    expect(post).toHaveBeenLastCalledWith({ type: 'export', preview: false, options: { ...exportDefaults, format: 'jpg' } })
    document.querySelector<HTMLButtonElement>('#markleaf-export [data-cancel]')!.click()
    expect(post).toHaveBeenLastCalledWith({ type: 'cancelExport' })
    expect(dialog.isOpen).toBe(true)
    dialog.finished('Done')
    document.querySelector<HTMLButtonElement>('#markleaf-export [data-close]')!.click()
    expect(dialog.isOpen).toBe(false)
    expect(exportStrings('en').print).toBe('Print…')
    expect(exportStrings('ja').print).toBe('印刷…')
    expect(exportStrings('zh-CN').print).toBe('打印…')
    dialog.dispose()
    delete (HTMLDialogElement.prototype as Partial<HTMLDialogElement>).showModal
    delete (HTMLDialogElement.prototype as Partial<HTMLDialogElement>).close
  })
})
