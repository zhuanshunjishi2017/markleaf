import type { Editor } from '@tiptap/core'
import { executeEditorCommand, isAllowedLink, scrollToFootnoteDefinition } from './editor'
import { scrollToHeadingAnchor } from './outline'

export type DocumentLinkActions = {
  primaryModifier: 'meta' | 'ctrl'
  openLink(href: string): void
  missingFootnote?(kind: 'definition' | 'reference', label: string): void
  topInset?(): number
}

export function bindDocumentLinks(mount: HTMLElement, getEditor: () => Editor, actions: DocumentLinkActions): () => void {
  const events = new AbortController()
  const activate = (event: MouseEvent): void => {
    if (!(event.target instanceof Element)) return
    const target = event.target.closest('a[href], sup[data-footnote-ref], p.markleaf-footnote-def')
    if (!target) return
    // Navigation always stays in the shared handler; browsers must not navigate the webview.
    event.preventDefault()
    const editor = getEditor()
    if (editor.isEditable && !(actions.primaryModifier === 'meta' ? event.metaKey : event.ctrlKey)) return
    event.stopPropagation()
    if (target.matches('sup[data-footnote-ref]')) {
      const label = target.getAttribute('data-footnote-ref') ?? ''
      if (!scrollToFootnoteDefinition(editor, label)) actions.missingFootnote?.('definition', label)
    } else if (target.matches('p.markleaf-footnote-def')) {
      const label = target.getAttribute('data-footnote-label') ?? ''
      if (!executeEditorCommand(editor, 'goToFootnoteReference', label)) actions.missingFootnote?.('reference', label)
    } else {
      const href = target.getAttribute('href') ?? ''
      if (href.startsWith('#')) scrollToHeadingAnchor(editor, href, actions.topInset?.() ?? 0)
      else if (isAllowedLink(href)) actions.openLink(href)
    }
  }
  mount.addEventListener('click', activate, { signal: events.signal })
  return () => events.abort()
}
