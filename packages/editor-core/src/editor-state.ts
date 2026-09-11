// Shared value types; safe to import in hosts without DOM dependencies.
export type EditorCommandState = {
  canUndo: boolean
  canRedo: boolean
  expandedSource?: boolean
  hasSelection: boolean
  paragraph: boolean
  headingLevel: number | null
  bold: boolean
  italic: boolean
  underline: boolean
  strike: boolean
  highlight: boolean
  code: boolean
  link: boolean
  blockquote: boolean
  codeBlock: boolean
  frontMatter: boolean
  codeBlockLanguage: string | null
  codeBlockText: string | null
  mermaid: boolean
  mermaidSelected: boolean
  mermaidSource: string | null
  mermaidCount: number
  bulletList: boolean
  orderedList: boolean
  taskList: boolean
  inTable: boolean
  tableAlign: 'left' | 'center' | 'right' | null
  imageSelected: boolean
  mathInline: boolean
  mathBlock: boolean
  mathLatex: string | null
  mathNumber: string | null
  caption: string | null
  footnoteDefinitionLabel: string | null
  canStartFormatPainter: boolean
  formatPainterArmed: boolean
}

export type EditorStatus = {
  characterCount: number
  selectedCharacterCount: number
  totalCharacterCount: number
  nonWhitespaceCharacterCount: number
  cjkCharacterCount: number
  westernWordCount: number
  formulaCount: number
  codeLineCount: number
  paragraphCount: number
  blockType: 'paragraph' | 'heading1' | 'heading2' | 'heading3' | 'heading4' | 'heading5' | 'heading6'
    | 'blockquote' | 'alert' | 'codeBlock' | 'bulletList' | 'orderedList' | 'taskList' | 'table' | 'image' | 'footnoteDefinition'
  line: number
  column: number
}
