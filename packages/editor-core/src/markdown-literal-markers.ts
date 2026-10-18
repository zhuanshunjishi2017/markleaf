import type { Editor, JSONContent } from '@tiptap/core'

/** Tiptap escapes standard Markdown markers, but not our custom == syntax. */
export function serializeWithLiteralMarkers(editor: Editor): string {
  if (!editor.state.doc.textContent.includes('==')) return editor.getMarkdown()
  const replacements = new Map<string, string>()
  const prefix = `\u0000markleaf-equals-${crypto.randomUUID()}-`
  function protect(node: JSONContent, insideCode = false): JSONContent {
    const spec = editor.schema.nodes[node.type ?? '']?.spec
    const code = insideCode || Boolean(spec?.code || spec?.atom)
      || Boolean(node.marks?.some(mark => editor.schema.marks[mark.type]?.spec.code))
    if (node.type === 'text' && !code && node.text?.includes('=')) {
      return { ...node, text: node.text.replace(/={2,}/g, literal => {
        const placeholder = `${prefix}${replacements.size}\u0000`
        replacements.set(placeholder, literal.replace(/=/g, '\\='))
        return placeholder
      }) }
    }
    return node.content ? { ...node, content: node.content.map(child => protect(child, code)) } : node
  }
  const document = protect(editor.getJSON())
  let markdown = editor.markdown!.serialize(document)
  for (const [placeholder, literal] of replacements) markdown = markdown.replaceAll(placeholder, literal)
  return markdown
}
