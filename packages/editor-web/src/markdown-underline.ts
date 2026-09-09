import Underline from '@tiptap/extension-underline'

let htmlUnderlineDepth = 0

// Keep legacy ++text++ input, but require delimiter boundaries so ordinary
// C++ / C++17 and increment operators cannot consume surrounding prose.
export const MarkdownUnderline = Underline.extend({
  markdownTokenizer: {
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
  },
  // A visual selection can begin inside a word or contain literal ++. HTML
  // preserves those selections without inventing ambiguous plus delimiters.
  renderMarkdown(node, helpers) {
    return `<u>${helpers.renderChildren(node)}</u>`
  },
  markdownOptions: { htmlReopen: { open: '<u>', close: '</u>' } },
})
