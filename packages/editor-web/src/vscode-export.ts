import { createEditor } from './editor'
import { escapeHtml, generateExportHtml } from './export-html'
import { styles, resolveTypography } from './vscode-styles'
import type { ExportHtmlResult, ExportOptions } from './vscode-export-options'
import { exportStrings } from './vscode-export-strings'
import type { MarkLeafSettings } from './vscode-settings'

/** Parse an immutable TextDocument snapshot without editing the visible editor. */
export async function renderExportSnapshot(markdown: string, title: string, options: ExportOptions, settings: MarkLeafSettings, language: string): Promise<ExportHtmlResult> {
  const snapshot = createEditor(document.createElement('div'), markdown, true, { externalHistory: true })
  let rawBodyHtml: string
  try { rawBodyHtml = snapshot.getHTML() } finally { snapshot.destroy() }
  const typography = resolveTypography(options.typography)
  const palette = styles[`../../styles/colors-${options.colorTheme}.css`]!
  const dark = /@mode:\s*dark/.test(palette)
  const paged = ['pdf', 'print'].includes(options.format)
  const html = await generateExportHtml({
    rawBodyHtml, resolved: { rootClass: typography.classes.join(' '), css: typography.css },
    format: paged ? 'pdf' : options.format === 'html' ? 'html' : 'image',
    baseCss: styles['../../styles/base.css']!, colorSchemeCss: palette,
    mermaidTheme: dark ? 'dark' : 'default', strictRendering: true, title, language,
    fontSize: options.fontSize, lineHeight: options.lineHeight, maxWidth: options.contentWidth,
    visualCjkAutoSpacing: settings.cjkAutoSpacing,
    header: paged ? '' : escapeHtml(options.header), footer: paged ? '' : escapeHtml(options.footer),
    keepTablesTogether: options.keepTablesTogether, keepHeadingsWithNextBlock: options.keepHeadingsWithNext,
    editorLoc: exportStrings(language),
  }).catch(error => { throw new Error(`${exportStrings(language).renderFailed} ${error instanceof Error ? error.message : String(error)}`) })
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  if (parsed.querySelector('.katex-error, .markleaf-mermaid-message-error')) throw new Error(exportStrings(language).renderFailed)
  const article = parsed.querySelector<HTMLElement>('.markleaf-document')!
  if (settings.fontFamily) article.style.fontFamily = settings.fontFamily
  // Stable heading anchors keep in-document links useful outside the editor.
  const slugs = new Map<string, number>()
  for (const heading of article.querySelectorAll('h1,h2,h3,h4,h5,h6')) {
    const slug = (heading.textContent ?? '').toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-')
    const index = slugs.get(slug) ?? 0
    slugs.set(slug, index + 1)
    heading.id = index ? `${slug}-${index}` : slug
  }
  const images: string[] = []
  for (const image of parsed.querySelectorAll<HTMLImageElement>('img')) {
    const source = image.getAttribute('data-markleaf-path') || image.getAttribute('src') || ''
    if (!source) throw new Error(`${exportStrings(language).missingImage}: ${image.alt}`)
    let index = images.indexOf(source)
    if (index < 0) { index = images.length; images.push(source) }
    image.src = `markleaf-export-image:${index}`
    image.removeAttribute('srcset')
    image.removeAttribute('data-markleaf-path')
    image.loading = 'eager'
  }
  // Export uses page margins, not the editor's screen padding. Long code wraps
  // for images too, so the screenshot does not silently crop a scrollable block.
  const layout = parsed.createElement('style')
  layout.textContent = `html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
    .markleaf-export-pdf .markleaf-document { padding: 0 5px; }
    .markleaf-export-image pre, .markleaf-export-image pre code { white-space: pre-wrap; overflow-wrap: anywhere; }
    .markleaf-document table { max-width: 100%; }
    @media print { .markleaf-document { width: 100%; max-width: none; } }
    @page { size: ${options.paperSize} ${options.landscape ? 'landscape' : 'portrait'}; margin: ${options.marginTop}mm ${options.marginRight}mm ${options.marginBottom}mm ${options.marginLeft}mm; }`
  parsed.head.append(layout)
  return { html: `<!DOCTYPE html>\n${parsed.documentElement.outerHTML}`, images }
}
