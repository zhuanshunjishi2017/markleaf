import { describe, expect, it } from 'vitest'
import { getEditorSemanticContext, resolveEditorActions } from '../src/command-state'

describe('shared command availability', () => {
  it('keeps formatting available at a text caret and reflects active marks', () => {
    const actions = resolveEditorActions({ paragraph: true, bold: true }, { readOnly: false })
    expect(actions.toggleBold).toEqual({ enabled: true, checked: true })
    expect(actions.copy?.enabled).toBe(false)
    expect(actions.paste?.enabled).toBe(true)
  })

  it('allows read-only selection export while disabling document mutations', () => {
    const actions = resolveEditorActions({ hasSelection: true, inTable: true, canUndo: true }, { readOnly: true })
    for (const command of ['copy', 'copyHtml', 'find', 'selectAll', 'exportDocument']) expect(actions[command]?.enabled).toBe(true)
    for (const command of ['cut', 'paste', 'undo', 'toggleBold', 'deleteTable', 'replace']) expect(actions[command]?.enabled).toBe(false)
  })

  it.each([{ sourceMode: true }, { documentType: 'plainText' as const }])('keeps literal editing separate from rendered commands: %o', context => {
    const actions = resolveEditorActions({ hasSelection: true, canUndo: true }, { readOnly: false, ...context })
    for (const command of ['copy', 'cut', 'pasteText', 'undo']) expect(actions[command]?.enabled).toBe(true)
    for (const command of ['toggleBold', 'insertTable', 'insertMathBlock', 'copyHtml', 'setEditorFocusMode']) expect(actions[command]?.enabled).toBe(false)
  })

  it('keeps table text formatting available without offering nested block insertion', () => {
    const actions = resolveEditorActions({ inTable: true, tableAlign: 'center' }, { readOnly: false })
    expect(actions.toggleBold?.enabled).toBe(true)
    expect(actions.addRowAfter?.enabled).toBe(true)
    expect(actions.alignTableCenter).toEqual({ enabled: true, checked: true })
    for (const command of ['insertTable', 'setHeading1', 'insertMermaid']) expect(actions[command]?.enabled).toBe(false)
  })

  it('distinguishes block formulas, inline formulas and opened literal source', () => {
    const block = resolveEditorActions({ mathBlock: true }, { readOnly: false })
    expect(block.setMathNumber?.enabled).toBe(true)
    expect(block.toggleBold?.enabled).toBe(false)
    expect(resolveEditorActions({ mathInline: true }, { readOnly: false }).setMathNumber?.enabled).toBe(false)
    const source = resolveEditorActions({ expandedSource: true, mathBlock: true, hasSelection: true }, { readOnly: false })
    expect(source.copy?.enabled).toBe(true)
    expect(source.pasteText?.enabled).toBe(true)
    expect(source.copyHtml?.enabled).toBe(false)
    expect(source.setMathNumber?.enabled).toBe(false)
  })

  it('preserves painter cancellation and context precedence', () => {
    const actions = resolveEditorActions({ formatPainterArmed: true }, { readOnly: false })
    expect(actions.formatPainter).toEqual({ enabled: true, checked: true })
    expect(actions.formatPainterApply?.enabled).toBe(true)
    expect(getEditorSemanticContext({ footnoteDefinitionLabel: 'note', inTable: true })).toBe('footnoteDefinition')
    expect(getEditorSemanticContext({ frontMatter: true, codeBlock: true })).toBe('frontMatter')
    expect(getEditorSemanticContext({ mathBlock: true, codeBlock: true })).toBe('math')
    expect(getEditorSemanticContext({ inTable: true }, true)).toBe('ordinaryBlock')
  })
})
