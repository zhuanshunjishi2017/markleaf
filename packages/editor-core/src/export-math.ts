import katex from 'katex'

function decodeHtmlEntities(text: string): string {
  const textarea = document.createElement('textarea')
  textarea.innerHTML = text
  return textarea.value
}

/// Exports load in an isolated WebView and cannot reuse the editor's lazy
/// KaTeX runtime, so this path keeps its synchronous pre-rendering behavior.
export function renderMathInHtml(html: string, throwOnError = false): string {
  return html
    .replace(/<span data-math-inline="1">([\s\S]*?)<\/span>/g, (_, latex: string) => {
      const source = decodeHtmlEntities(latex)
      return source
        ? `<span class="markleaf-math markleaf-math-inline">${katex.renderToString(source, { throwOnError })}</span>`
        : '<span class="markleaf-math markleaf-math-inline markleaf-math-placeholder">...</span>'
    })
    .replace(/<div data-math-block="1"([^>]*)>([\s\S]*?)<\/div>/g, (_, attrs: string, latex: string) => {
      const numberMatch = /data-math-number="([^"]*)"/.exec(attrs)
      const body = decodeHtmlEntities(latex)
      if (!body) return '<div class="markleaf-math-block markleaf-math-placeholder">...</div>'
      const number = numberMatch?.[1]
      const full = /\\tag\{[^{}]*\}\s*$/.test(body)
        ? body
        : number ? `${body} \\tag{${decodeHtmlEntities(number)}}` : body
      return katex.renderToString(full, { displayMode: true, throwOnError })
    })
}
