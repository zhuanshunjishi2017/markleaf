import type { DocumentType } from './document-mode'

const READ_ONLY_ALLOWED_COMMANDS = new Set([
  'find',
  'toggleSourceMode',
  'setStyle',
  'setSourceSelection',
  'findText',
  'findNext',
  'findPrev',
  'findClose',
  'setLanguage',
  'setSourceIndent',
  'setAutoConvertUnsafeEmphasis',
  'setMarkdownEditingSettings',
  'setAutoHideScrollbar',
  'setReadOnly',
  'setEditorFocusMode',
  'setEditorTypewriterMode',
  'setBlockHandleVisible',
  'setCodeHighlightVisible',
  'rerenderAllMermaid',
  'exportSelection',
  'exportMermaidImage',
  'exportDocument',
  'selectAll',
  'scrollToPosition',
  'scrollToSourceHeading',
  'goToFootnoteReference',
  'goToFootnoteDefinition',
  'openLink',
  'viewMathSource',
  'viewMermaidSource',
  'rerenderMermaid',
  'setBlockHighlight',
  'clearBlockHighlight',
  'previewFormat',
  'endContextMenu',
])

export function isHostCommandAllowed(
  command: string,
  context: { readOnly: boolean; documentType: DocumentType },
): boolean {
  if (context.readOnly && !READ_ONLY_ALLOWED_COMMANDS.has(command)) {
    return false
  }
  if (context.documentType === 'plainText' && command === 'insertMermaid') {
    return false
  }
  return true
}
