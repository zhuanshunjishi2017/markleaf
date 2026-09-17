import { afterEach, describe, expect, it } from 'vitest'
import {
  createEditor,
  extractTypographyParagraphs,
  getTypographyProjectionDiagnostics,
} from '../src/index'

const cleanup: Array<() => void> = []

function setup(markdown: string) {
  document.body.innerHTML = '<main id="editor"></main>'
  const mount = document.querySelector<HTMLElement>('#editor')!
  const editor = createEditor(mount, markdown, false, { externalHistory: true })
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
