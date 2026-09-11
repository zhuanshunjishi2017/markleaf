import { access, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { constants } from 'node:fs'
import { join } from 'node:path'
import { homedir, tmpdir } from 'node:os'
import { pathToFileURL } from 'node:url'
import puppeteer, { type Browser, type Page } from 'puppeteer-core'
import type { ExportOptions } from '../webview/src/vscode-export-options'
import { exportStrings } from '../webview/src/vscode-export-strings'

export async function browserExecutable(configured = ''): Promise<string | undefined> {
  const candidates = configured ? [configured] : process.platform === 'darwin' ? [
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge',
    join(homedir(), 'Applications/Google Chrome.app/Contents/MacOS/Google Chrome'), '/Applications/Chromium.app/Contents/MacOS/Chromium',
  ] : process.platform === 'win32' ? [process.env.PROGRAMFILES, process.env['PROGRAMFILES(X86)'], process.env.LOCALAPPDATA]
    .filter((root): root is string => !!root).flatMap(root => [join(root, 'Google/Chrome/Application/chrome.exe'), join(root, 'Microsoft/Edge/Application/msedge.exe')])
    : ['/usr/bin/google-chrome', '/usr/bin/google-chrome-stable', '/usr/bin/microsoft-edge', '/usr/bin/microsoft-edge-stable', '/usr/bin/chromium', '/usr/bin/chromium-browser', '/snap/bin/chromium']
  for (const candidate of candidates) {
    try { await access(candidate, constants.X_OK); return candidate } catch { /* Try only known executable locations. */ }
  }
  return undefined
}

export function imageSlices(height: number, scale: number, maxHeight: number): Array<{ y: number; height: number }> {
  const step = Math.floor(maxHeight / scale)
  if (step < 1 || !Number.isFinite(height) || height < 1) throw new Error('Invalid image dimensions')
  const slices: Array<{ y: number; height: number }> = []
  for (let y = 0; y < Math.ceil(height); y += step) slices.push({ y, height: Math.min(step, Math.ceil(height) - y) })
  return slices
}

const escapeText = (value: string): string => value.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!)
function pdfOptions(o: ExportOptions) {
  return {
    format: o.paperSize, landscape: o.landscape, printBackground: true, preferCSSPageSize: true,
    margin: { top: `${o.marginTop}mm`, right: `${o.marginRight}mm`, bottom: `${o.marginBottom}mm`, left: `${o.marginLeft}mm` },
    displayHeaderFooter: !!(o.header || o.footer || o.pageNumbers),
    headerTemplate: `<div style="font:10px sans-serif;width:100%;text-align:center;">${escapeText(o.header)}</div>`,
    footerTemplate: `<div style="font:10px sans-serif;width:100%;text-align:center;">${escapeText(o.footer)}${o.pageNumbers ? ' <span class="pageNumber"></span> / <span class="totalPages"></span>' : ''}</div>`,
    timeout: 30000,
  }
}

async function ready(page: Page, language: string): Promise<void> {
  // Content is fully embedded before reaching this renderer. Decode failures
  // must stop output, rather than silently publishing missing pictures/fonts.
  const failures = await page.evaluate(`(async () => {
    await document.fonts.ready;
    const errors = [];
    for (const font of document.fonts) if (font.status === 'error') errors.push(font.family);
    for (const image of document.images) {
      try { await image.decode(); } catch { errors.push(image.alt || 'image'); }
      if (!image.naturalWidth) errors.push(image.alt || 'image');
    }
    if (window.__markleafFitMath) window.__markleafFitMath();
    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    return errors;
  })()`) as string[]
  if (failures.length) throw new Error(`${exportStrings(language).readyError}: ${failures.join(', ')}`)
}

export type BrowserExport = {
  html: string
  options: ExportOptions
  executablePath: string
  signal: AbortSignal
  language: string
  preview?: boolean
  report(message: string): void
  prepareFiles(count: number): Promise<void>
  writeFile(bytes: Uint8Array, index: number): Promise<void>
}

/** Owns one temporary browser profile. Never attaches to the user's browser. */
export async function exportWithBrowser(job: BrowserExport): Promise<void> {
  const { options: o, signal } = job
  const s = exportStrings(job.language)
  signal.throwIfAborted()
  const directory = await mkdtemp(join(tmpdir(), 'markleaf-export-'))
  let browser: Browser | undefined
  let closing: Promise<void> | undefined
  const close = (): Promise<void> => closing ??= browser ? browser.close() : Promise.resolve()
  const abort = (): void => { if (browser) void close().catch(() => {}) }
  signal.addEventListener('abort', abort, { once: true })
  try {
    try {
      browser = await puppeteer.launch({ executablePath: job.executablePath, headless: !job.preview && o.format !== 'print',
        userDataDir: join(directory, 'profile'), pipe: true, timeout: 30000,
        handleSIGINT: false, handleSIGTERM: false, handleSIGHUP: false,
        args: ['--no-first-run', '--no-default-browser-check'],
      })
    } catch (error) { throw new Error(`${s.browserFailed}: ${error instanceof Error ? error.message : String(error)}`) }
    signal.throwIfAborted()
    const page = (await browser.pages())[0] ?? await browser.newPage()
    page.setDefaultTimeout(30000)
    page.setDefaultNavigationTimeout(30000)
    const image = o.format === 'png' || o.format === 'jpg'
    const paper = { A4: [210, 297], A5: [148, 210], Letter: [215.9, 279.4], Legal: [215.9, 355.6] }[o.paperSize]!
    const width = image ? o.contentWidth + 112 : ['pdf', 'print'].includes(o.format)
      ? Math.floor((paper[o.landscape ? 1 : 0]! - o.marginLeft - o.marginRight) * 96 / 25.4) : 1100
    await page.setViewport({ width, height: image ? Math.floor(o.imageMaxHeight / o.imageScale) : 900, deviceScaleFactor: image ? o.imageScale : 1 })
    await page.emulateMediaType(o.format === 'pdf' || o.format === 'print' ? 'print' : 'screen')
    // The document's images and fonts were embedded by the owning host.
    await page.setRequestInterception(true)
    page.on('request', request => {
      void (/^(data:|about:)/.test(request.url()) ? request.continue() : request.abort()).catch(() => {})
    })
    job.report(s.rendering)
    await page.setContent(job.html, { waitUntil: 'load', timeout: 30000 })
    await ready(page, job.language)
    signal.throwIfAborted()
    if (o.format === 'print') {
      // Chromium print CSS supplies plain-text page furniture. The browser
      // dialog remains authoritative for the selected printer and final options.
      await page.addStyleTag({ content: `@page { @top-center { content: ${JSON.stringify(o.header)}; font: 10px sans-serif; } @bottom-center { content: ${JSON.stringify(o.footer)} ${o.pageNumbers ? '" " counter(page) " / " counter(pages)' : ''}; font: 10px sans-serif; } }` })
      await page.bringToFront()
      const finished = new Promise<void>((resolve, reject) => {
        page.once('close', () => resolve())
        browser!.once('disconnected', () => resolve())
        void page.exposeFunction('__markleafAfterPrint', resolve).then(() => page.evaluate(`(() => {
          window.addEventListener('afterprint', () => window.__markleafAfterPrint(), { once: true });
          setTimeout(() => window.print(), 0);
        })()`)).catch(reject)
      })
      job.report(s.printing)
      await finished
      signal.throwIfAborted()
      return
    }
    const previews: string[] = []
    const write = async (bytes: Uint8Array, index: number, extension: string): Promise<void> => {
      signal.throwIfAborted()
      if (job.preview) {
        const path = join(directory, `preview-${index + 1}.${extension}`)
        await writeFile(path, bytes)
        previews.push(path)
      } else await job.writeFile(bytes, index)
    }
    if (o.format === 'pdf') {
      if (!job.preview) await job.prepareFiles(1)
      await write(await page.pdf(pdfOptions(o)), 0, 'pdf')
    } else if (image) {
      const height = await page.evaluate("Math.ceil(document.querySelector('#export-root').getBoundingClientRect().height)") as number
      const slices = imageSlices(height, o.imageScale, o.imageMaxHeight)
      if (!job.preview) await job.prepareFiles(slices.length)
      signal.throwIfAborted()
      await page.addStyleTag({ content: 'html, body { overflow: hidden !important; } #export-root { transform-origin: top left; }' })
      for (const [index, slice] of slices.entries()) {
        signal.throwIfAborted()
        // Move the entire export root, then capture the viewport origin. This
        // also works for the final short slice without scroll clamping/repeats.
        await page.evaluate(`document.querySelector('#export-root').style.transform = 'translateY(-${slice.y}px)'`)
        await page.evaluate('new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')
        job.report(`${s.rendering} ${index + 1}/${slices.length}`)
        const bytes = await page.screenshot({ type: o.format === 'jpg' ? 'jpeg' : 'png', ...(o.format === 'jpg' ? { quality: o.jpegQuality } : {}),
          clip: { x: 0, y: 0, width, height: slice.height }, captureBeyondViewport: false })
        await write(bytes, index, o.format)
      }
    }
    if (job.preview) {
      await page.setRequestInterception(false)
      page.removeAllListeners('request')
      // Raster capture may use a very tall viewport. Preview uses a normal
      // window size after the output bytes have been generated.
      await page.setViewport({ width: 1100, height: 850, deviceScaleFactor: 1 })
      if (o.format === 'pdf') await page.goto(pathToFileURL(previews[0]!).href)
      else if (image) {
        const gallery = join(directory, 'preview.html')
        await writeFile(gallery, `<html><body style="margin:0;background:#e8e8e8;text-align:center">${previews.map(path => `<img style="display:block;max-width:100%;margin:12px auto" src="${pathToFileURL(path).href}">`).join('')}</body></html>`)
        await page.goto(pathToFileURL(gallery).href)
      }
      // HTML previews are already loaded; all other formats show actual bytes.
      await page.bringToFront()
      job.report(s.previewing)
      await new Promise<void>(resolve => {
        if (!browser!.connected || page.isClosed()) { resolve(); return }
        browser!.once('disconnected', () => resolve())
        page.once('close', () => resolve())
      })
    }
    signal.throwIfAborted()
  } catch (error) {
    signal.throwIfAborted()
    throw error
  } finally {
    signal.removeEventListener('abort', abort)
    try { await close() } finally { await rm(directory, { recursive: true, force: true, maxRetries: 3, retryDelay: 100 }) }
  }
}
