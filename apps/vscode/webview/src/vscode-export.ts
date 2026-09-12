import { createEditor, escapeHtml, generateExportHtml } from '@markleaf/editor-core'
import { styles, resolveTypography, stylesGlobPrefix } from './vscode-styles'
import type { ExportHtmlResult, ExportOptions } from './vscode-export-options'
import { exportStrings } from './vscode-export-strings'
import type { MarkLeafSettings } from './vscode-settings'

/** Parse an immutable TextDocument snapshot without editing the visible editor. */
export async function renderExportSnapshot(markdown: string, title: string, options: ExportOptions, settings: MarkLeafSettings, language: string): Promise<ExportHtmlResult> {
  const snapshot = createEditor(document.createElement('div'), markdown, true, { externalHistory: true })
  let rawBodyHtml: string
  try { rawBodyHtml = snapshot.getHTML() } finally { snapshot.destroy() }
  const typography = resolveTypography(options.typography)
  const palette = styles[`${stylesGlobPrefix}colors-${options.colorTheme}.css`]!
  const dark = /@mode:\s*dark/.test(palette)
  const paged = ['pdf', 'print'].includes(options.format)
  const html = await generateExportHtml({
    rawBodyHtml, resolved: { rootClass: typography.classes.join(' '), css: typography.css },
    format: paged ? 'pdf' : options.format === 'html' ? 'html' : 'image',
    baseCss: styles[`${stylesGlobPrefix}base.css`]!, colorSchemeCss: palette,
    mermaidTheme: dark ? 'dark' : 'default', strictRendering: true, title, language,
    fontFamily: settings.fontFamily, fontSize: options.fontSize, lineHeight: options.lineHeight, maxWidth: options.contentWidth,
    visualCjkAutoSpacing: settings.cjkAutoSpacing,
    header: paged ? '' : escapeHtml(options.header), footer: paged ? '' : escapeHtml(options.footer),
    keepTablesTogether: options.keepTablesTogether, keepHeadingsWithNextBlock: options.keepHeadingsWithNext,
    editorLoc: exportStrings(language),
  }).catch(error => { throw new Error(`${exportStrings(language).renderFailed} ${error instanceof Error ? error.message : String(error)}`) })
  const parsed = new DOMParser().parseFromString(html, 'text/html')
  if (parsed.querySelector('.katex-error, .markleaf-mermaid-message-error')) throw new Error(exportStrings(language).renderFailed)
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
  // Page dimensions are supplied by the product's export dialog.
  const layout = parsed.createElement('style')
  layout.textContent = `@page { size: ${options.paperSize} ${options.landscape ? 'landscape' : 'portrait'}; margin: ${options.marginTop}mm ${options.marginRight}mm ${options.marginBottom}mm ${options.marginLeft}mm; }`
  parsed.head.append(layout)
  return { html: `<!DOCTYPE html>\n${parsed.documentElement.outerHTML}`, images }
}
