import type { EditorCommandState } from './editor-state'
import type { DocumentType } from './document-mode'
import { isFormatterSupportedLanguage } from './code-formatter'

export type EditorActionState = { enabled: boolean; checked: boolean }
export type EditorActions = Record<string, EditorActionState>
export type EditorActionContext = { readOnly: boolean; sourceMode?: boolean; documentType?: DocumentType; focusMode?: boolean; typewriterMode?: boolean }

/** Shared command semantics. Native menus and web controls consume the result. */
export function resolveEditorActions(state: Partial<EditorCommandState>, context: EditorActionContext): EditorActions {
  const actions: EditorActions = {}
  const writable = !context.readOnly
  const visual = !context.sourceMode && !state.expandedSource && context.documentType !== 'plainText'
  const editing = writable && visual
  const block = editing && !state.inTable
  const inline = editing && !state.imageSelected && !state.mathInline && !state.mathBlock && !state.mermaidSelected && !state.codeBlock && !state.frontMatter
  const add = (commands: string, enabled: boolean, checked = false) => {
    for (const command of commands.split(' ')) actions[command] = { enabled, checked }
  }
  add('undo', writable && !!state.canUndo)
  add('redo', writable && !!state.canRedo)
  add('copy copyMarkdown copyPlainText', !!state.hasSelection)
  add('copyHtml', visual && !!state.hasSelection)
  add('cut deleteSelection', writable && !!state.hasSelection)
  add('paste pasteText pasteHtml pasteMarkdown pasteClipboard pastePlainText', writable)
  add('selectAll find findText findNext findPrev findClose exportDocument exportSelection', true)
  add('replace replaceOne replaceAll', writable)
  add('toggleSourceMode', context.documentType !== 'plainText', !!context.sourceMode)
  add('setEditorFocusMode', visual, !!context.focusMode)
  add('setEditorTypewriterMode', visual, !!context.typewriterMode)
  add('setCodeHighlightVisible', true)
  add('setParagraph', block, !!state.paragraph)
  for (let level = 1; level <= 6; level++) add(`setHeading${level}`, block, state.headingLevel === level)
  add('promoteHeading', block && state.headingLevel != null && state.headingLevel > 1)
  add('demoteHeading', block && state.headingLevel != null && state.headingLevel < 6)
  for (const [command, mark] of Object.entries({ toggleBold: 'bold', toggleItalic: 'italic', toggleUnderline: 'underline', toggleStrike: 'strike', toggleHighlight: 'highlight', toggleCode: 'code', setLink: 'link' } as const)) {
    add(command, inline, !!state[mark])
  }
  for (const [command, mark] of Object.entries({ toggleBlockquote: 'blockquote', toggleCodeBlock: 'codeBlock', toggleBulletList: 'bulletList', toggleOrderedList: 'orderedList', toggleTaskList: 'taskList' } as const)) {
    add(command, block, !!state[mark])
  }
  add('indentListItem outdentListItem', block && !!(state.bulletList || state.orderedList || state.taskList))
  add('insertLineBefore insertLineAfter duplicateParagraph deleteParagraph', editing && state.paragraphOperationAvailable !== false)
  add('clearFormat insertMathInline insertFootnote insertImage insertImages', editing)
  add('cancelAlert', block && !!state.alertType && state.paragraphOperationAvailable !== false)
  add('insertMathBlock insertHorizontalRule insertMermaid insertTable', block)
  add('insertAlertNote insertAlertTip insertAlertImportant insertAlertWarning insertAlertCaution', block && state.canSetAlert !== false)
  add('showFrontMatter', editing)
  add('editMath convertMath deleteMath updateMath', editing && !!(state.mathInline || state.mathBlock))
  add('setMathNumber', editing && !!state.mathBlock)
  add('viewMathSource', visual && !!(state.mathInline || state.mathBlock))
  add('viewMermaidSource rerenderMermaid', visual && !!state.mermaidSelected)
  add('editMermaid deleteMermaid', editing && !!state.mermaidSelected)
  add('openLink copyLinkAddress', visual && !!state.linkHref)
  add('editLink removeLink', editing && !!state.linkHref)
  add('goToFootnoteDefinition', visual && !!state.footnoteReferenceLabel)
  add('deleteFootnoteReference', editing && !!state.footnoteReferenceLabel)
  add('updateMermaid', editing && state.codeBlockLanguage?.toLowerCase() === 'mermaid')
  add('rerenderAllMermaid', visual && (state.mermaidCount ?? 0) > 0)
  add('resetFootnoteLabel clearFootnoteReferences deleteFootnote', editing && !!state.footnoteDefinitionLabel)
  add('goToFootnoteReference', visual && !!state.footnoteDefinitionLabel)
  add('setCodeBlockLanguage setCodeBlockLanguageAt', editing && !!state.codeBlock)
  add('formatCodeBlock', editing && !!state.codeBlock && isFormatterSupportedLanguage(state.codeBlockLanguage))
  add('normalizeInlineCode', editing && !!state.code && !!state.hasSelection)
  add('insertCodeBlockWithLanguage', block)
  add('exitCode', editing && !!(state.codeBlock || state.frontMatter))
  add('copyCodeBlock', visual && !!(state.codeBlock || state.frontMatter) && state.codeBlockText != null)
  add('formatPainter formatPainterArm', editing && !!(state.canStartFormatPainter || state.formatPainterArmed), !!state.formatPainterArmed)
  add('formatPainterApply', editing && !!state.formatPainterArmed)
  const table = editing && !!state.inTable && state.selectionScope != null && state.selectionScope !== 'mixed'
  add('addRowBefore addRowAfter addColumnBefore addColumnAfter setTableCaption deleteTable', table)
  add('deleteRow', table && state.canDeleteTableRow === true)
  add('deleteColumn', table && state.canDeleteTableColumn === true)
  for (const [command, align] of Object.entries({ alignTableLeft: 'left', alignTableCenter: 'center', alignTableRight: 'right' })) add(command, table, state.tableAlign === align)
  add('rotateImageClockwise resizeImage changeImage setImageCaption', editing && !!state.imageSelected)
  add('saveImageAs', visual && !!state.imageSelected)
  add('saveMermaidAs', visual && !!state.mermaidSelected && state.mermaidRendered === true)
  return actions
}

export function getEditorSemanticContext(state: Partial<EditorCommandState>, sourceMode = false): string {
  if (sourceMode) return 'ordinaryBlock'
  if (state.footnoteDefinitionLabel) return 'footnoteDefinition'
  if (state.frontMatter) return 'frontMatter'
  if (state.inTable) return 'table'
  if (state.mermaidSelected) return 'mermaid'
  if (state.imageSelected) return 'image'
  if (state.mathInline || state.mathBlock) return 'math'
  if (state.codeBlock) return 'codeBlock'
  return 'ordinaryBlock'
}
