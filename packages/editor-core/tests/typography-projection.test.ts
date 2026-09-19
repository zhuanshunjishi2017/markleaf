import { afterEach, describe, expect, it } from 'vitest'
import {
  getMarkdown,
  createEditor,
  extractTypographyParagraphs,
  getTypographyProjectionDiagnostics,
  setTypographyProjectionEnabled,
  shouldApplyTypographyProjection,
  calculateTypographySpacingCorrection,
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

  it('treats a hard break as an explicit segment boundary', () => {
    const { editor } = setup('中文English中文English  \nEnglish中文English')
    const paragraphs = extractTypographyParagraphs(editor.state.doc)

    expect(paragraphs).toHaveLength(2)
    expect(paragraphs.map(paragraph => paragraph.text)).toEqual([
      '中文English中文English',
      'English中文English',
    ])
    expect(paragraphs.every(paragraph => paragraph.eligible)).toBe(true)
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

  it('keeps nested list paragraphs on native justified fallback', () => {
    const { editor } = setup('- nested list text\n\n- another item')
    const paragraphs = extractTypographyParagraphs(editor.state.doc)
    const nested = paragraphs.filter(paragraph => (
      paragraph.fallbackReason === 'unsupported-block'
      && editor.state.doc.resolve(paragraph.from).depth > 1
    ))

    expect(nested).toHaveLength(2)
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 400,
      measure: value => ({
        width: value.length * 10,
        stretch: value.trim() === '' ? 4 : value.length,
        shrink: value.trim() === '' ? 2 : value.length,
      }),
    })
    expect(editor.view.dom.querySelectorAll('li > p.markleaf-typography-native')).toHaveLength(2)
    expect(editor.view.dom.querySelectorAll('li > p.markleaf-typography-planned')).toHaveLength(0)
  })
})

describe('typography projection refresh gate', () => {
  const editorA = {}
  const editorB = {}

  it('skips only when the same editor keeps the same enabled state and width', () => {
    expect(shouldApplyTypographyProjection({
      previousEditor: editorA,
      nextEditor: editorA,
      previousEnabled: true,
      nextEnabled: true,
      previousWidth: 896,
      nextWidth: 896,
    })).toBe(false)
  })

  it(' reapplies when a replaced editor has the same enabled state and width', () => {
    expect(shouldApplyTypographyProjection({
      previousEditor: editorA,
      nextEditor: editorB,
      previousEnabled: true,
      nextEnabled: true,
      previousWidth: 896,
      nextWidth: 896,
    })).toBe(true)
  })

  it('reapplies when enabled state or width changes', () => {
    expect(shouldApplyTypographyProjection({
      previousEditor: editorA,
      nextEditor: editorA,
      previousEnabled: false,
      nextEnabled: true,
      previousWidth: 896,
      nextWidth: 896,
    })).toBe(true)
    expect(shouldApplyTypographyProjection({
      previousEditor: editorA,
      nextEditor: editorA,
      previousEnabled: true,
      nextEnabled: true,
      previousWidth: 896,
      nextWidth: 900,
    })).toBe(true)
  })
})

describe('typography projection render correction', () => {
  it('reduces character spacing when browser shaping overflows the measure', () => {
    const correction = calculateTypographySpacingCorrection({
      mode: 'character',
      currentSpacing: 0.4,
      currentWidth: 820,
      targetWidth: 800,
      gapCount: 100,
    })
    expect(correction).toEqual({
      mode: 'character',
      spacing: 0.2,
      delta: -20,
    })
  })

  it('increases word spacing when browser shaping underfills the measure', () => {
    const correction = calculateTypographySpacingCorrection({
      mode: 'word',
      currentSpacing: 1,
      currentWidth: 790,
      targetWidth: 800,
      gapCount: 10,
    })
    expect(correction).toEqual({
      mode: 'word',
      spacing: 2,
      delta: 10,
    })
  })
})

const fixedMeasure = (text: string) => ({
  width: text.length * 10,
  stretch: text.trim() === '' ? 40 : valueLength(text) * 4,
  shrink: text.trim() === '' ? 20 : valueLength(text) * 2,
})

function valueLength(text: string) {
  return Array.from(text).length
}

const rigidMeasure = (text: string) => ({
  width: valueLength(text) * 10,
  stretch: text.trim() === '' ? 4 : 0,
  shrink: text.trim() === '' ? 2 : 0,
})

describe('enabled typography projection', () => {
  it('falls back rather than accepting over-loose intermediate lines', () => {
    const { editor } = setup('中文 English 中文 English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 140,
      measure: rigidMeasure,
    })
    expect(getTypographyProjectionDiagnostics(editor).status).toBe('fallback')
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break')).toHaveLength(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-native')).toHaveLength(1)
    expect(getMarkdown(editor)).toBe('中文 English 中文 English')
    setTypographyProjectionEnabled(editor, false)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-native')).toHaveLength(0)
  })

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
    expect(adjustedLines.filter(line => !line.classList.contains('markleaf-typography-line-tail')).length)
      .toBe(diagnostics.lineBreaks)
    expect(adjustedLines.every((line) => line.classList.contains('markleaf-typography-line'))).toBe(true)
    expect(adjustedLines.every((line) => line.hasAttribute('data-markleaf-typography-space-count'))).toBe(true)
    expect(adjustedLines.every((line) => line.style.getPropertyValue('--ml-typography-line-adjust') !== '')).toBe(true)
    expect(adjustedLines.every((line) => line.style.getPropertyValue('--ml-typography-word-spacing') !== '')).toBe(true)
    expect(getMarkdown(editor)).toBe('中文 English 中文 English')
  })

  it('hides source spaces consumed by a planned break', () => {
    const { editor } = setup('AAAA BBBB AAAA')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 92,
      measure: fixedMeasure,
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('planned')
    expect(diagnostics.lineBreaks).toBeGreaterThan(0)
    const consumedSpaces = Array.from(
      editor.view.dom.querySelectorAll('.markleaf-typography-consumed'),
    )
    expect(consumedSpaces).toHaveLength(diagnostics.lineBreaks)
    expect(consumedSpaces.every(space => space.textContent?.trim() === '')).toBe(true)
  })


  it('plans CJK lines without visible spaces using character spacing', () => {
    const { editor } = setup('中文English中文English中文English中文English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 175,
      measure: value => ({
        width: value.length * 10,
        stretch: value.trim() === '' ? 0 : value.length * 2,
        shrink: value.length,
      }),
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('planned')
    expect(diagnostics.fallbackParagraphs).toBe(0)
    expect(editor.view.dom.querySelectorAll('[data-markleaf-typography-line]').length).toBeGreaterThan(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-break').length).toBeGreaterThan(0)
    expect(editor.view.dom.querySelectorAll('.markleaf-typography-native')).toHaveLength(0)
    const lines = Array.from(
      editor.view.dom.querySelectorAll<HTMLElement>('[data-markleaf-typography-line="true"]'),
    )
    const measured = lines.some(line => Number.isFinite(Number.parseFloat(
      line.style.getPropertyValue('--ml-typography-character-spacing'),
    )))
    expect(measured).toBe(true)
  })

  it('distributes positive space with word spacing when a line has visible spaces', () => {
    const { editor } = setup('AAAA BBBB CCCC')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 94,
      measure: value => ({
        width: value.length * 10,
        stretch: value.trim() === '' ? 4 : 0,
        shrink: value.trim() === '' ? 2 : 0,
      }),
    })

    const line = editor.view.dom.querySelector<HTMLElement>('[data-markleaf-typography-line="true"]')
    expect(Number.parseFloat(line?.style.getPropertyValue('--ml-typography-word-spacing') ?? '0')).toBeGreaterThan(0)
    expect(Number.parseFloat(line?.style.getPropertyValue('--ml-typography-character-spacing') ?? '0')).toBe(0)
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
    expect(editor.view.dom.querySelectorAll('p.markleaf-typography-native')).toHaveLength(1)
  })

  it('projects hard-break segments instead of falling back to browser wrapping', () => {
    const { editor } = setup('中文English中文English  \nEnglish中文English')
    setTypographyProjectionEnabled(editor, true, {
      availableWidth: 240,
      measure: fixedMeasure,
    })

    const diagnostics = getTypographyProjectionDiagnostics(editor)
    expect(diagnostics.status).toBe('planned')
    expect(diagnostics.plannedParagraphs).toBe(2)
    expect(editor.view.dom.querySelectorAll('p.markleaf-typography-planned')).toHaveLength(1)
    expect(editor.view.dom.querySelectorAll('p.markleaf-typography-native')).toHaveLength(0)
    expect(editor.view.dom.querySelectorAll('br')).toHaveLength(1)
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

  it('allows projected selection to extend across paragraphs and headings', () => {
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
    expect(from).toBeGreaterThanOrEqual(1)
    expect(to).toBeLessThanOrEqual(editor.state.doc.content.size - 1)
    expect(from).toBeLessThan(to)
  })
})
