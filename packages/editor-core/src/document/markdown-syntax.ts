import type { MarkdownTokenizer } from '@tiptap/core'

// Shared by the visual editor and the DOM-free document projection.
let htmlUnderlineDepth = 0

export const frontMatterTokenizer: MarkdownTokenizer = {
    name: 'frontMatter',
    level: 'block',
    start: (src: string) => src.startsWith('---\n') || src.startsWith('---\r\n') ? 0 : -1,
    tokenize: (src: string, tokens: unknown[]) => {
      if (tokens.length > 0) return undefined
      const match = /^---[ \t]*\r?\n([\s\S]*?)\r?\n---[ \t]*(?:\r?\n|$)/.exec(src)
      if (!match) return undefined
      return {
        type: 'frontMatter',
        raw: match[0],
        text: match[1] ?? '',
      }
    },
  }

export const highlightTokenizer: MarkdownTokenizer = {
    name: 'highlight',
    level: 'inline',
    start: (src: string) => src.indexOf('=='),
    tokenize: (src: string, _tokens: unknown[], helpers: any) => {
      const match = /^==([^=\n]+?)==/.exec(src)
      if (!match) return undefined
      return {
        type: 'highlight',
        raw: match[0],
        text: match[1],
        tokens: helpers.inlineTokens(match[1]),
      }
    },
  }

export const alertTokenizer: MarkdownTokenizer = {
    name: 'alert',
    level: 'block',
    start: (src: string) => src.search(/^ {0,3}>[ \t]*\[!(?:NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*$/m),
    tokenize: (src: string, _tokens: unknown[], helpers: any) => {
      const first = /^ {0,3}>[ \t]*\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*(?:\n|$)/i.exec(src)
      if (!first) return undefined
      const type = first[1]!.toUpperCase()
      const lines = [first[0].replace(/\r?\n$/, '')]
      let offset = first[0].length
      while (offset < src.length) {
        const lineEnd = src.indexOf('\n', offset)
        const end = lineEnd < 0 ? src.length : lineEnd
        const line = src.slice(offset, end)
        if (!/^ {0,3}>[ \t]?/.test(line)) break
        lines.push(line)
        offset = lineEnd < 0 ? src.length : lineEnd + 1
      }
      const body = lines.slice(1)
        .map(line => line.replace(/^ {0,3}>[ \t]?/, ''))
        .join('\n')
        .replace(/\n+$/, '')
      return {
        type: 'alert',
        alertType: type,
        raw: src.slice(0, offset),
        tokens: helpers.blockTokens(body),
      }
    },
  }

export const footnoteTokenizer: MarkdownTokenizer = {
    name: 'footnoteReference',
    level: 'inline',
    start: (src: string) => src.indexOf('[^'),
    tokenize: (src: string) => {
      const match = /^\[\^([^\]\n]+)\](?!:)/.exec(src)
      if (!match) return undefined
      return { type: 'footnoteReference', raw: match[0], text: match[1] }
    },
  }

export function normalizeDisplayMathAfterList(markdown: string): string {
  const lines = markdown.match(/[^\n]*\n|[^\n]+$/g) ?? []
  const defaultEol = /\r?\n/.exec(markdown)?.[0] ?? '\n'
  const result: string[] = []
  let fence = ''
  let mathClose = ''
  let dedentMath = false
  for (const [index, line] of lines.entries()) {
    const trimmed = line.trim()
    const eol = line.endsWith('\r\n') ? '\r\n' : line.endsWith('\n') ? '\n' : defaultEol
    const codeFence = /^\s*(?:[-+*] |\d+[.)] )?(`{3,}|~{3,})/.exec(line)?.[1]
    if (fence) {
      if (new RegExp(`^${fence[0]}{${fence.length},}\\s*$`).test(trimmed)) fence = ''
      result.push(line)
      continue
    }
    if (mathClose) {
      const closesMath = dedentMath ? trimmed === mathClose : line.includes(mathClose)
      result.push(closesMath && dedentMath ? line.replace(/^[ \t]+/, '') : line)
      if (closesMath) {
        if (dedentMath && lines[index + 1]?.trim()) result.push(eol)
        mathClose = ''
        dedentMath = false
      }
      continue
    }
    if (codeFence) { fence = codeFence; result.push(line); continue }

    // List generators indent complete display formulas, including nested
    // lists. Dedent their delimiters only; keep formula payload and code
    // fences intact, and do not turn an unclosed delimiter into a block.
    const indentedDisplay = /^[ \t]{2,}(\$\$[^\r\n]*\$\$|\\\[[^\r\n]*\\\]|\$\$|\\\[)[ \t]*(?:\r?\n)?$/.test(line)
    const math = (indentedDisplay ? /^[ \t]*(\$\$|\\\[)/ : /^[ \t]{0,3}(\$\$|\\\[)/).exec(line)
    if (!math) { result.push(line); continue }
    const close = math[1] === '$$' ? '$$' : '\\]'
    const singleLine = line.slice(math[0].length).includes(close)
    if (!singleLine) {
      const hasCloser = lines.slice(index + 1).some(next => indentedDisplay ? next.trim() === close : next.includes(close))
      if (!hasCloser) { result.push(line); continue }
      mathClose = close
      dedentMath = indentedDisplay
    }
    if (result.at(-1)?.trim()) result.push(eol)
    result.push(indentedDisplay ? line.replace(/^[ \t]+/, '') : line)
    if (indentedDisplay && singleLine && lines[index + 1]?.trim()) result.push(eol)
  }
  return result.join('')
}

export function normalizeMathSource(source: string): string {
  // Formula source is opaque user data. Do not trim, normalize whitespace, or
  // otherwise alter any character while parsing/serializing Markdown.
  return source === '...' ? '' : source
}

export const mathInlineTokenizer: MarkdownTokenizer = {
    name: 'mathInline',
    level: 'inline',
    start: (src: string) => {
      const dollar = src.indexOf('$')
      const parenthesis = src.indexOf('\\(')
      if (dollar < 0) return parenthesis
      if (parenthesis < 0) return dollar
      return Math.min(dollar, parenthesis)
    },
    tokenize: (src: string) => {
      const match = /^(?:(?<!\$)\$(?!\$)([^$\n]+?)\$(?!\$)|\\\(((?:\\(?!\))|[^\\\n])*?)\\\))/.exec(src)
      if (!match) return undefined
      return { type: 'mathInline', raw: match[0], text: normalizeMathSource(match[1] ?? match[2] ?? '') }
    },
  }

export const mathBlockTokenizer: MarkdownTokenizer = {
    name: 'mathBlock',
    level: 'block',
    start: (src: string) => {
      const dollars = src.indexOf('$$')
      const brackets = src.indexOf('\\[')
      if (dollars < 0) return brackets
      if (brackets < 0) return dollars
      return Math.min(dollars, brackets)
    },
    tokenize: (src: string) => {
      const match = /^(?:\$\$([\s\S]+?)\$\$|\\\[([\s\S]+?)\\\])/.exec(src)
      if (!match) return undefined
      return {
        type: 'mathBlock',
        raw: match[0],
        text: normalizeMathSource(match[1] ?? match[2] ?? ''),
      }
    },
  }

export const underlineTokenizer: MarkdownTokenizer = {
    name: 'underline',
    level: 'inline',
    start(src) {
      const html = src.search(/<u>/i)
      const legacy = htmlUnderlineDepth === 0 ? src.indexOf('++') : -1
      return html < 0 ? legacy : legacy < 0 ? html : Math.min(html, legacy)
    },
    tokenize(src, tokens, lexer) {
      const html = /^<u>([\s\S]*?)<\/u>/i.exec(src)
      if (html) {
        // Tiptap's generic HTML parser collapses spaces and treats nested
        // Markdown as literal text. Parse our inline wrapper as Markdown,
        // while keeping any literal ++ inside it opaque to the legacy rule.
        htmlUnderlineDepth += 1
        try {
          return { type: 'underline', raw: html[0], tokens: lexer.inlineTokens(html[1]!) }
        } finally { htmlUnderlineDepth -= 1 }
      }
      if (htmlUnderlineDepth > 0) return undefined
      const previous = tokens.at(-1)?.raw?.at(-1) ?? ''
      if (/[A-Za-z0-9_+\\]/.test(previous)) return undefined
      const match = /^\+\+(?![\s+])((?:\\[^\r\n]|(?!\+\+)[^\r\n])+?)\+\+(?![A-Za-z0-9_+])/.exec(src)
      const content = match?.[1]
      if (!match || !content || /\s$/.test(content)) return undefined
      return { type: 'underline', raw: match[0], text: content, tokens: lexer.inlineTokens(content) }
    },
  }

export const FOOTNOTE_DEFINITION_SENTINEL = '\u2060'
export function protectFootnoteDefinitionsForVisualMarkdown(markdown: string): string {
  return markdown.replace(
    /(^|\n)( {0,3})(\[\^[^\]\n]+\]:)/g,
    (_match, lineStart: string, indent: string, marker: string) => `${lineStart}${indent}${FOOTNOTE_DEFINITION_SENTINEL}${marker}`,
  )
}
