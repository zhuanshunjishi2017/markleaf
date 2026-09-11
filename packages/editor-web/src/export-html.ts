// Document serialization and typography shared by native hosts and VS Code.
import { renderEscapedCaptionHtml } from './editor'
import { applyExportPagination, exportPaginationCss, type ExportPaginationOptions } from './export-pagination'
import { katexCss, renderMathInHtml } from './math'
import { renderMermaidInHtml, type MermaidThemeName } from './mermaid'

export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function renderEditorHtmlForExport(
  html: string,
  preserveEmptyParagraphs = false,
  pagination: ExportPaginationOptions,
  visualCjkAutoSpacing = true,
): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html')

  for (const frontMatter of Array.from(parsed.body.querySelectorAll('[data-markleaf-front-matter]'))) {
    frontMatter.remove()
  }

  for (const caption of Array.from(parsed.body.querySelectorAll<HTMLElement>('figcaption.markleaf-figcaption'))) {
    caption.innerHTML = renderEscapedCaptionHtml(escapeHtml(caption.textContent ?? ''))
  }

  for (const paragraph of Array.from(parsed.body.querySelectorAll<HTMLParagraphElement>('p'))) {
    if (preserveEmptyParagraphs && isEmptyExportParagraph(paragraph)) {
      paragraph.innerHTML = '&nbsp;'
      continue
    }

    const match = new RegExp(`^\\s*\\u2060?\\[\\^([^\\]\\n]+)\\]:[ \\t]*(.*)$`, 's').exec(paragraph.textContent ?? '')
    if (!match) continue

    const label = match[1]!.trim()
    const body = match[2] ?? ''
    const prefixLength = match[0].length - body.length
    paragraph.classList.add('markleaf-footnote-def')
    paragraph.classList.add('markleaf-footnote-def-export')
    paragraph.dataset.footnoteLabel = label
    removeTextPrefix(paragraph, prefixLength)
    const labelElement = parsed.createElement('span')
    labelElement.className = 'markleaf-footnote-def-label'
    labelElement.textContent = `[${label}] `
    paragraph.insertBefore(labelElement, paragraph.firstChild)
  }

  if (visualCjkAutoSpacing) {
    applyCjkAutoSpacingToExport(parsed)
  }

  return applyExportPagination(parsed.body.innerHTML, pagination)
}

function applyCjkAutoSpacingToExport(parsed: Document): void {
  const textNodes: Text[] = []
  const walker = parsed.createTreeWalker(parsed.body, NodeFilter.SHOW_TEXT)
  while (walker.nextNode()) {
    if (walker.currentNode instanceof Text) textNodes.push(walker.currentNode)
  }

  for (const textNode of textNodes) {
    const parent = textNode.parentElement
    if (!parent || parent.closest('pre, code, .katex, .markleaf-mermaid')) continue
    const text = textNode.data
    const boundaries: number[] = []
    for (let index = 1; index < text.length; index += 1) {
      const previous = text[index - 1]!
      const current = text[index]!
      if ((isCjkAutoSpacingCharacter(previous) && isWesternAutoSpacingCharacter(current))
        || (isWesternAutoSpacingCharacter(previous) && isCjkAutoSpacingCharacter(current))) {
        boundaries.push(index)
      }
    }
    if (boundaries.length === 0) continue

    const fragment = parsed.createDocumentFragment()
    let start = 0
    for (const boundary of boundaries) {
      fragment.append(parsed.createTextNode(text.slice(start, boundary)))
      const spacer = parsed.createElement('span')
      spacer.className = 'markleaf-cjk-autospace-widget'
      spacer.setAttribute('aria-hidden', 'true')
      fragment.append(spacer)
      start = boundary
    }
    fragment.append(parsed.createTextNode(text.slice(start)))
    textNode.replaceWith(fragment)
  }
}

function isCjkAutoSpacingCharacter(character: string): boolean {
  return /[\u2e80-\u9fff\uf900-\ufaff\u3040-\u30ff\uac00-\ud7af]/u.test(character)
}

function isWesternAutoSpacingCharacter(character: string): boolean {
  return /[A-Za-z0-9]/.test(character)
}

function isEmptyExportParagraph(paragraph: HTMLParagraphElement): boolean {
  if ((paragraph.textContent ?? '').replace(/\u00a0/g, '').trim().length > 0) {
    return false
  }
  return !Array.from(paragraph.childNodes).some((node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      return ((node.textContent ?? '').replace(/\u00a0/g, '').trim().length > 0)
    }
    if (!(node instanceof HTMLElement)) {
      return false
    }
    return node.tagName.toLowerCase() !== 'br'
  })
}

function removeTextPrefix(element: HTMLElement, length: number): void {
  let remaining = Math.max(0, length)
  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
  const emptyTextNodes: Text[] = []

  while (remaining > 0) {
    const node = walker.nextNode()
    if (!(node instanceof Text)) break

    if (node.data.length <= remaining) {
      remaining -= node.data.length
      emptyTextNodes.push(node)
      continue
    }

    node.data = node.data.slice(remaining)
    remaining = 0
  }

  for (const node of emptyTextNodes) {
    node.remove()
  }
}

function resolveMermaidTheme(css: string): MermaidThemeName | undefined {
  const declarations = Array.from(css.matchAll(
    /--ml-mermaid-theme\s*:\s*(default|dark|forest|neutral|base)\s*;/gi,
  ))
  return declarations.at(-1)?.[1]?.toLowerCase() as MermaidThemeName | undefined
}

export type ExportHtmlInput = {
  rawBodyHtml: string
  resolved: { rootClass: string; css: string }
  format: string
  mermaidTheme?: MermaidThemeName
  strictRendering?: boolean
  header?: string
  footer?: string
  fontSize?: number
  lineHeight?: number
  maxWidth?: number
  visualCjkAutoSpacing?: boolean
  colorSchemeCss?: string
  baseCss: string
  title?: string
  language?: string
  keepTablesTogether?: boolean
  keepHeadingsWithNextBlock?: boolean
  editorLoc: Partial<Record<'alertNote' | 'alertTip' | 'alertImportant' | 'alertWarning' | 'alertCaution', string>>
}

export async function generateExportHtml({
  rawBodyHtml, resolved, format, mermaidTheme, strictRendering = false, header = '', footer = '', fontSize = 16,
  lineHeight = 1.6, maxWidth = 820, visualCjkAutoSpacing = true,
  colorSchemeCss = '', baseCss, title = '', language = 'zh-CN',
  keepTablesTogether = false, keepHeadingsWithNextBlock = false, editorLoc,
}: ExportHtmlInput): Promise<string> {
  const isPdf = format === 'pdf'
  const isImage = format === 'image'
  const bodyHtml = await renderMermaidInHtml(renderEditorHtmlForExport(
    renderMathInHtml(rawBodyHtml),
    isPdf,
    { keepTablesTogether, keepHeadingsWithNextBlock },
    visualCjkAutoSpacing,
  ).replace(
    /https:\/\/assets\.local\/image\?path=([^"']+)/g,
    (_, encoded: string) => {
      try { return decodeURIComponent(encoded) } catch { return encoded }
    },
  ), mermaidTheme ?? resolveMermaidTheme(resolved.css))
  const rootClass = [
    resolved.rootClass,
    isPdf ? 'markleaf-export-pdf' : '',
    isImage ? 'markleaf-export-image' : '',
  ].filter(Boolean).join(' ')

  return `<!DOCTYPE html>
<html lang="${escapeHtml(language)}">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>${escapeHtml(title || 'MarkLeaf')}</title>
<style>
* { box-sizing: border-box; }
${katexCss}
${baseCss}
.markleaf-document { text-autospace: ${visualCjkAutoSpacing ? 'normal' : 'no-autospace'}; }
.markleaf-document .markleaf-cjk-autospace-widget {
  display: inline-block;
  width: 0.35em;
  min-width: 0.35em;
  height: 1px;
  overflow: hidden;
  vertical-align: baseline;
  pointer-events: none;
}
${colorSchemeCss}
${resolved.css}
${exportPaginationCss}
/* 导出文档的排版内边距（编辑器侧由 #editor 承担）。 */
.markleaf-document {
  padding: 44px 56px 96px;
}
:root {
  --ml-font-size: ${fontSize}px;
  --ml-line-height: ${lineHeight};
  --ml-max-width: ${maxWidth}px;
  --markleaf-alert-note-title: ${JSON.stringify(editorLoc.alertNote ?? '备注')};
  --markleaf-alert-tip-title: ${JSON.stringify(editorLoc.alertTip ?? '提示')};
  --markleaf-alert-important-title: ${JSON.stringify(editorLoc.alertImportant ?? '重要')};
  --markleaf-alert-warning-title: ${JSON.stringify(editorLoc.alertWarning ?? '警告')};
  --markleaf-alert-caution-title: ${JSON.stringify(editorLoc.alertCaution ?? '注意')};
}
html { font-size: var(--ml-font-size); }
body { margin: 0; background: var(--bg-primary); }
.markleaf-export-image,
.markleaf-export-image .markleaf-document {
  width: calc(var(--ml-max-width) + 112px);
  max-width: none;
  margin-left: 0;
  margin-right: 0;
}
.markleaf-export-image #export-root {
  width: calc(var(--ml-max-width) + 112px);
  max-width: none;
  margin-left: 0;
  margin-right: 0;
}
.markleaf-export-image {
  /* WebView2 expands the viewport and captures one full surface before
     slicing. Do not create a scroll container: Chromium may reuse its first
     viewport texture for the expanded area, producing repeated content. */
  overflow: hidden !important;
}
/* Keep scrollbars out of the captured surface. */
.markleaf-export-image,
.markleaf-export-image html,
.markleaf-export-image body,
.markleaf-export-image * {
  scrollbar-width: none !important;
  -ms-overflow-style: none !important;
}
.markleaf-export-image::-webkit-scrollbar,
.markleaf-export-image html::-webkit-scrollbar,
.markleaf-export-image body::-webkit-scrollbar,
.markleaf-export-image *::-webkit-scrollbar {
  width: 0 !important;
  height: 0 !important;
  display: none !important;
}
.markleaf-export-image html,
html:has(body.markleaf-export-image) {
  scrollbar-width: none !important;
  -ms-overflow-style: none !important;
}
html:has(body.markleaf-export-image)::-webkit-scrollbar {
  width: 0 !important;
  height: 0 !important;
  display: none !important;
}
/* ---- PDF export: let print-dialog margins control spacing ---- */
.markleaf-export-pdf .markleaf-document {
  padding-left: 5px;
  padding-right: 5px;
  max-width: none;
  width: 100%;
  margin-left: 0;
  margin-right: 0;
}
.markleaf-export-pdf.markleaf-style-print .markleaf-document {
  padding-left: 5px;
  padding-right: 5px;
  max-width: none;
  width: 100%;
  margin-left: 0;
  margin-right: 0;
}
.markleaf-export-pdf .export-header,
.markleaf-export-pdf .export-footer {
  padding-left: 5px;
  padding-right: 5px;
  width: 100%;
}

/* PDF export: prevent horizontal overflow (scrollbars) and wrap long lines. */
.markleaf-export-pdf .markleaf-document pre {
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
  word-break: break-all;
  overflow-x: hidden;
  box-decoration-break: clone;
}
.markleaf-export-pdf .markleaf-document pre code {
  white-space: pre-wrap;
  word-wrap: break-word;
  overflow-wrap: break-word;
  word-break: break-all;
}
.markleaf-export-pdf .markleaf-document blockquote {
  box-decoration-break: clone;
  -webkit-box-decoration-break: clone;
}
.markleaf-export-pdf .markleaf-document .markleaf-alert {
  box-decoration-break: clone;
  -webkit-box-decoration-break: clone;
  overflow: visible;
}
.markleaf-export-pdf .markleaf-document table {
  width: auto;
  max-width: 100%;
  table-layout: auto;
  word-wrap: break-word;
  overflow-wrap: break-word;
}
.markleaf-export-pdf .markleaf-document .markleaf-mermaid,
.markleaf-export-pdf .markleaf-document .markleaf-mermaid-view,
.markleaf-export-pdf .markleaf-document .markleaf-mermaid-export {
  display: flex;
  justify-content: center;
}

.export-header, .export-footer {
  width: min(100%, var(--ml-max-width));
  margin: 0 auto;
  padding: 8px 56px;
}
.export-header { border-bottom: 1px solid #d8dee4; }
.export-footer { border-top: 1px solid #d8dee4; margin-top: 24px; }
</style>
</head>
<body${rootClass ? ` class="${rootClass}"` : ''}>
<div id="export-root">
${header ? `<div class="export-header">${header}</div>` : ''}
<div class="markleaf-document">${bodyHtml}</div>
${footer ? `<div class="export-footer">${footer}</div>` : ''}
</div>
<script>
(function () {
  function fitMath() {
    var doc = document.querySelector('.markleaf-document');
    if (!doc) return;
    var items = doc.querySelectorAll('.katex-display');
    for (var i = 0; i < items.length; i++) {
      var el = items[i];
      el.style.fontSize = '';
      var available = el.clientWidth;
      if (available <= 0) continue;
      // 让容器收缩包裹到内容宽度后再量，避免居中溢出与内联片段导致的测量失真。
      var display = el.style.display;
      var width = el.style.width;
      el.style.display = 'inline-block';
      el.style.width = 'max-content';
      var content = el.getBoundingClientRect().width;
      el.style.display = display;
      el.style.width = width;
      if (content <= available) continue;
      var base = parseFloat(getComputedStyle(el).fontSize) || 16;
      el.style.fontSize = (base * available / content).toFixed(2) + 'px';
    }
  }
  window.__markleafFitMath = fitMath;
  // 等待 KaTeX 字体加载完成后再测量，避免用回退字体度量导致公式被误缩放。
  if (document.fonts && document.fonts.ready) {
    document.fonts.ready.then(fitMath);
  } else {
    fitMath();
  }
})();
</script>
</body>
</html>`
}
