import { afterEach, describe, expect, it, vi } from 'vitest'
import { SourceEditor } from '../src/source-editor'

describe('SourceEditor runtime mode', () => {
  it('blocks edits after switching to read-only and allows edits again', () => {
    document.body.innerHTML = '<main id="source"></main>'
    const mount = document.querySelector<HTMLElement>('#source')!
    const editor = new SourceEditor(mount, 'before', () => {})
    editor.setSelection(6)

    expect(editor.replaceSelection('!')).toBe(true)
    expect(editor.view.state.doc.toString()).toBe('before!')

    editor.setReadOnly(true)
    expect(editor.replaceSelection('?')).toBe(false)
    expect(editor.view.state.doc.toString()).toBe('before!')

    editor.setReadOnly(false)
    expect(editor.replaceSelection('?')).toBe(true)
    expect(editor.view.state.doc.toString()).toBe('before!?')

    editor.destroy()
  })

  it('keeps document statistics stable across selection moves and refreshes after edits', () => {
    document.body.innerHTML = '<main id="source"></main>'
    const mount = document.querySelector<HTMLElement>('#source')!
    const editor = new SourceEditor(mount, 'one two\nthree', () => {})

    const initial = editor.getStatus()
    editor.setSelection(0, 3)
    expect(editor.getStatus()).toMatchObject({
      totalCharacterCount: initial.totalCharacterCount,
      nonWhitespaceCharacterCount: initial.nonWhitespaceCharacterCount,
      paragraphCount: initial.paragraphCount,
      selectedCharacterCount: 3,
    })

    editor.replaceSelection('a much longer line')
    expect(editor.getStatus().totalCharacterCount).toBeGreaterThan(initial.totalCharacterCount)
    editor.destroy()
  })
})

describe('SourceEditor long-document chapters', () => {
  it('detects source chapters and jumps between them', () => {
    document.body.innerHTML = '<main id="source"></main>'
    const mount = document.querySelector<HTMLElement>('#source')!
    const editor = new SourceEditor(
      mount,
      '# Intro\nbody\n\n## Data\ntext\n\n第三章 结论\nfinal text',
      () => {},
    )

    expect(editor.getSourceChapters()).toEqual([
      { level: 1, text: 'Intro', position: 0 },
      { level: 2, text: 'Data', position: 14 },
      { level: 1, text: '第三章 结论', position: 28 },
    ])

    editor.gotoSourceChapter(editor.getSourceChapters()[2]!)
    expect(editor.view.state.selection.main.from).toBe(28)
    expect(editor.getActiveSourceChapterPosition()).toBe(28)
    editor.destroy()
  })
})

describe('SourceEditor chapter jump lifecycle', () => {
  const editors: SourceEditor[] = []
  afterEach(() => {
    editors.splice(0).forEach(editor => editor.destroy())
    vi.restoreAllMocks(); vi.useRealTimers(); document.body.replaceChildren()
  })

  function setup() {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    const mount = document.createElement('main')
    document.body.append(mount)
    const editor = new SourceEditor(mount, '# First\ntext\n\n## Second\nmore text', () => {})
    editors.push(editor)
    const measure = vi.spyOn(editor.view, 'coordsAtPos').mockReturnValue(null)
    return { editor, measure, chapters: editor.getSourceChapters() }
  }

  it('does not measure or reschedule a chapter jump after destruction', () => {
    const { editor, measure, chapters } = setup()
    editor.gotoSourceChapter(chapters[1]!)
    editor.destroy()
    vi.runAllTimers()
    expect(measure).not.toHaveBeenCalled()
  })

  it('calibrates only the latest target when chapter jumps arrive before a frame', () => {
    const { editor, measure, chapters } = setup()
    editor.gotoSourceChapter(chapters[0]!)
    editor.gotoSourceChapter(chapters[1]!)
    vi.advanceTimersToNextFrame()
    expect(measure.mock.calls.map(([position]) => position)).toEqual([chapters[1]!.position + 1])
  })

  it('stops using an old chapter position when the document changes before calibration', () => {
    const { editor, measure, chapters } = setup()
    editor.gotoSourceChapter(chapters[1]!)
    editor.view.dispatch({ changes: { from: 0, to: editor.view.state.doc.length, insert: 'short' } })
    expect(() => vi.runAllTimers()).not.toThrow()
    expect(measure).not.toHaveBeenCalled()
  })
})
