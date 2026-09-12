import { underlineTokenizer } from './document/markdown-syntax'
import Underline from '@tiptap/extension-underline'


// Keep legacy ++text++ input, but require delimiter boundaries so ordinary
// C++ / C++17 and increment operators cannot consume surrounding prose.
export const MarkdownUnderline = Underline.extend({
  markdownTokenizer: underlineTokenizer,
  // A visual selection can begin inside a word or contain literal ++. HTML
  // preserves those selections without inventing ambiguous plus delimiters.
  renderMarkdown(node, helpers) {
    return `<u>${helpers.renderChildren(node)}</u>`
  },
  markdownOptions: { htmlReopen: { open: '<u>', close: '</u>' } },
})
