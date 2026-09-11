import { Marked, type Token, type TokenizerThis } from 'marked'
import { decodeHTML } from 'entities'
import { parseFragment, type DefaultTreeAdapterMap } from 'parse5'
import { protectFootnoteDefinitionsForVisualMarkdown, alertTokenizer, footnoteTokenizer, frontMatterTokenizer, highlightTokenizer, mathBlockTokenizer, mathInlineTokenizer, normalizeDisplayMathAfterList, underlineTokenizer } from './markdown-syntax'

// Use the same tokenizer definitions and order as the editor's Markdown extension.
const markdown = new Marked({ gfm: true, breaks: false })
for (const definition of [frontMatterTokenizer, highlightTokenizer, alertTokenizer, underlineTokenizer, footnoteTokenizer, mathInlineTokenizer, mathBlockTokenizer]) {
  markdown.use({ extensions: [{
    name: definition.name,
    level: definition.level ?? 'inline',
    start: typeof definition.start === 'function' ? definition.start : (source: string) => source.indexOf(String(definition.start)),
    tokenizer(this: TokenizerThis, source, tokens) {
      const result = definition.tokenize(source, tokens, {
        inlineTokens: value => this.lexer.inlineTokens(value),
        blockTokens: value => this.lexer.blockTokens(value),
      })
      return result ? { ...result, raw: result.raw ?? '' } as Token : undefined
    },
  }] })
}

function htmlText(source: string): string {
  const visit = (node: DefaultTreeAdapterMap['node']): string => {
    if ('tagName' in node && ['script', 'style', 'template'].includes(node.tagName)) return ''
    if (node.nodeName === '#text' && 'value' in node) return node.value
    if ('tagName' in node && node.tagName === 'img') return node.attrs.find(attr => attr.name === 'alt')?.value ?? ''
    if ('tagName' in node && node.tagName === 'br') return '\n'
    const content = 'childNodes' in node ? node.childNodes.map(visit).join('') : ''
    return 'tagName' in node && ['p', 'div', 'li', 'tr', 'pre', 'blockquote'].includes(node.tagName) ? `${content}\n` : content
  }
  return visit(parseFragment(source))
}

type ProjectionToken = { type: string; text?: string; raw?: string; tokens?: ProjectionToken[]; items?: ProjectionToken[]; header?: { tokens: ProjectionToken[] }[]; rows?: { tokens: ProjectionToken[] }[][] }

function projectTokens(tokens: ProjectionToken[]): string {
  return tokens.map(token => {
    switch (token.type) {
      case 'frontMatter': return ''
      case 'space': case 'hr': case 'br': return '\n'
      case 'html': return htmlText(token.text ?? token.raw ?? '')
      case 'code': case 'codespan': case 'mathInline': case 'mathBlock': return `${token.text ?? ''}${token.type === 'code' || token.type === 'mathBlock' ? '\n' : ''}`
      case 'footnoteReference': return token.text ?? ''
      case 'image': return decodeHTML(token.text ?? '')
      case 'table': return [...(token.header ?? []).map(cell => projectTokens(cell.tokens)), ...(token.rows ?? []).flatMap(row => row.map(cell => projectTokens(cell.tokens)))].join(' ') + '\n'
      case 'list': return (token.items ?? []).map(item => projectTokens(item.tokens ?? [])).join('\n') + '\n'
      default: {
        const text = token.tokens?.length ? projectTokens(token.tokens) : decodeHTML(token.text ?? '')
        return ['paragraph', 'heading', 'blockquote', 'alert', 'list_item'].includes(token.type) ? `${text}\n` : text
      }
    }
  }).join('')
}

export function documentPlainText(source: string, isMarkdown: boolean): string {
  if (!isMarkdown) return source.replace(/\s+/gu, ' ').trim()
  // Footnote definitions are paragraphs in the editor; protect them from Marked's link-definition rule.
  const input = protectFootnoteDefinitionsForVisualMarkdown(normalizeDisplayMathAfterList(source))
  return projectTokens(markdown.lexer(input) as ProjectionToken[])
    .replace(/\u2060\[\^[^\]\n]+\]:[ \t]*/g, '')
    .replace(/\s+/gu, ' ').trim()
}

export function documentSnippet(text: string, query: string): string | null {
  const needle = query.trim().toLowerCase()
  if (!needle) return null
  const points = Array.from(text)
  // Keep case-fold expansion from changing source indices, and never split a surrogate pair.
  const folded: string[] = [], offsets: number[] = []
  points.forEach((point, index) => {
    const lower = point.toLowerCase()
    folded.push(lower)
    for (let unit = 0; unit < lower.length; unit++) offsets.push(index)
  })
  const match = folded.join('').indexOf(needle)
  if (match < 0) return null
  const start = Math.max(0, offsets[match]! - 2)
  return points.slice(start, start + Array.from(query.trim()).length + 42).join('').trimEnd()
}

export function matchDocument(name: string, source: string, isMarkdown: boolean, query: string) {
  const normalized = query.trim().toLowerCase()
  if (!normalized) return { matched: false, isContentMatch: false, snippet: '' }
  const text = documentPlainText(source, isMarkdown)
  if (name.toLowerCase().includes(normalized)) return { matched: true, isContentMatch: false, snippet: text }
  const snippet = documentSnippet(text, normalized)
  return { matched: snippet !== null, isContentMatch: snippet !== null, snippet: snippet ?? '' }
}
