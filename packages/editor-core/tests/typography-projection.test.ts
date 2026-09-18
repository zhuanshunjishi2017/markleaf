import { afterEach, describe, expect, it } from 'vitest'
import {
  getMarkdown,
  createEditor,
  extractTypographyParagraphs,
  getTypographyProjectionDiagnostics,
  setTypographyProjectionEnabled,
} from '../src/index'

const cleanup: Array<() => void> = []

function setup(markdown: string, { externalHistory = true }: { externalHistory?: boolean } = {}) {
  document.body.innerHTML = '<main id="editor"></main>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory })
  cleanup.push(() => editor.destroy())
  return { editor, mount }
}

afterEach(() => {
  cleanup.reverse().splice(0).forEach(dispose => dispose())
  document.body.innerHTML = ''
})

describe('typography projection', () => {
  it('starts disabled and leaves the document untouched', () => {
    const { editor } = setup('中文 English 中文')

    expect(getTypographyProjectionDiagnostics(editor)).toEqual({
      status: 'disabled',
      eligibleParagraphs: 0,
      plannedParagraphs: 0,
      fallbackParagraphs: 0,
      lineBreaks: 0,
    })
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-planned')).toHaveLength(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })
})

describe('typography paragraph extraction', () => {
  it('extracts top-level paragraphs and maps grapheme offsets to PM positions', () => {
    const { editor } = setup('中文\n\nA👍B')
    const paragraphs = extractTypographyParagraphs(editor.state.doc)

    expect(paragraphs).toHaveLength(2)
    expect(paragraphs[0]!).toMatchObject({
      nodeFrom: 0,
      nodeTo: 4,
      from: 1,
      to: 3,
      text: '中文',
      eligible: true,
    })
    expect(paragraphs[0]!.clusterOffsets).toEqual([0, 1, 2])
    expect(paragraphs[1]!).toMatchObject({
      nodeFrom: 4,
      nodeTo: 10,
      from: 5,
      to: 9,
      text: 'A👍B',
      eligible: true,
    })
    expect(paragraphs[1]!.clusterOffsets).toEqual([0, 1, 3, 4])
  })

  it('marks unsupported blocks and inline atoms as fallbacks', () => {
    const { editor } = setup('# Title\n\nbefore ![image](image.png) after')
    const paragraphs = extractTypographyParagraphs(editor.state.doc)

    expect(paragraphs[0]).toMatchObject({
      eligible: false,
      fallbackReason: 'unsupported-block',
    })
    expect(paragraphs[1]).toMatchObject({
      eligible: false,
      fallbackReason: 'inline-atom',
    })
  })
})

const fixedMeasure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('enabled typography projection', () => {
  it('plans line breaks without changing Markdown', () => {
    const { editor } = setup('中文 English 中文 English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 120,
      measure: fixedMeasure,
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('planned')
    expect(diagnostics.eligibleParagraphs).toBe(1)
    expect(diagnostics.plannedParagraphs).toBe(1)
    expect(diagnostics.lineBreaks).toBeGreaterThan(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-planned')).toHaveLength(1)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break').length).toBeGreaterThan(0)
    expect(
      Array.from(editor.view.dom.querySelectorAll('.markleaf-typography-break')).every(
        widget => widget.tagName === 'BR',
      ),
    ).toBe(true)
    const adjustedLines = Array.from(
      editor.view.dom.querySelectorAll<HTMLElement>('[data-markleaf-typography-line="true"]'),
    )
    expect(adjustedLines.length).toBe(diagnostics.lineBreaks)
    expect(adjustedLines.every((line) => line.classList.contains('markleaf-typography-line'))).toBe(true)
    expect(adjustedLines.every((line) => line.hasAttribute('data-markleaf-typography-space-count'))).toBe(true)
    expect(adjustedLines.every((line) => line.style.getPropertyValue('--ml-typography-line-adjust') !== '')).toBe(true)
    expect(getMarkdown(editor)).toBe('中文 English 中文 English')
  })

  it('falls back for CJK lines without spaces', () => {
    const { editor } = setup('中文English中文English中文English中文English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 240,
      measure: fixedMeasure,
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('fallback')
    expect(diagnostics.fallbackParagraphs).toBe(1)
    expect(editor.view.dom.querySelectorAll('[data-markleaf-typography-line]')).toHaveLength(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })

  it('returns to disabled state and removes decorations', () => {
    const { editor } = setup('中文 English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 120,
      measure: fixedMeasure,
    })
    setTypographyProjectionEnabled(editor, false)

    expect(getTypographyProjectionDiagnostics(editor).status).toBe('disabled')
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })

  it('marks paragraphs with inline code as fallbacks', () => {
    const { editor } = setup('中文 `code` 中文')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 120,
      measure: fixedMeasure,
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('fallback')
    expect(diagnostics.fallbackParagraphs).toBe(1)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
  })

  it('recomputes decorations after document changes', () => {
    const { editor } = setup('中文 English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 120,
      measure: fixedMeasure,
    })
    const before = getTypographyProjectionDiagnostics(editor).lineBreaks

    editor.commands.insertContentAt(editor.state.doc.content.size, '\n\n中文 English 中文 English')
    const after = getTypographyProjectionDiagnostics(editor)

    expect(after.eligibleParagraphs).toBe(2)
    expect(after.lineBreaks).toBeGreaterThan(before)
  })

  it('keeps typography projections out of undo history', () => {
  const { editor } = setup('one\n\n中文 English 中文 English 中文 English', { externalHistory: false })
  const before = getMarkdown(editor)
  editor.commands.insertContentAt(2, '!')
  const markdownAfterEdit = getMarkdown(editor)
  setTypographyProjectionEnabled(editor, true, {
    availableWidth: 120,
    measure: fixedMeasure,
  })
  editor.commands.undo()

  expect(getMarkdown(editor)).toBe(before)
  expect(markdownAfterEdit).toBe('o!ne\n\n中文 English 中文 English 中文 English')
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break').length).toBeGreaterThan(0)
  })

  it('clamps projected drag selection to the origin paragraph', () => {
    const { editor } = setup('first projected paragraph\n\n# Heading\n\nsecond paragraph')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 900,
      measure: fixedMeasure,
    })
    const paragraph = extractTypographyParagraphs(editor.state.doc)[0]!

    editor.commands.setTextSelection({ from: paragraph.from + 1, to: paragraph.to - 1 })
    editor.commands.setTextSelection({
      from: paragraph.from,
      to: editor.state.doc.content.size - 1,
    })

    const { from, to } = editor.state.selection
    expect(from).toBeGreaterThanOrEqual(paragraph.from)
    expect(to).toBeLessThanOrEqual(paragraph.to)
    expect(editor.state.doc.textBetween(from, to, ' ', ' ')).not.toContain('Heading')
  })
})
