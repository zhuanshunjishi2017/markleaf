import { afterEach, describe, expect, it, vi } from 'vitest'
import { preserveViewportDuringLayoutChange } from '../src/zoom-anchor'

describe('zoom viewport anchoring', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
  })

  it('keeps the visible document block at the same viewport position after layout changes', () => {
    const documentRoot = document.createElement('main')
    const paragraph = document.createElement('p')
    documentRoot.className = 'markleaf-document'
    documentRoot.append(paragraph)
    document.body.append(documentRoot)

    Object.defineProperty(document, 'scrollingElement', {
      configurable: true,
      value: document.documentElement,
    })
    document.documentElement.scrollTop = 240

    let top = 180
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: vi.fn(() => paragraph),
    })
    vi.spyOn(paragraph, 'getBoundingClientRect').mockImplementation(() => ({
      x: 0,
      y: top,
      top,
      right: 100,
      bottom: top + 40,
      left: 0,
      width: 100,
      height: 40,
      toJSON: () => ({}),
    }))

    preserveViewportDuringLayoutChange(() => {
      top = 420
    })

    expect(document.documentElement.scrollTop).toBe(480)
  })

  it('anchors CodeMirror source mode to its internal scroller', () => {
    const scroller = document.createElement('div')
    const line = document.createElement('div')
    scroller.className = 'cm-scroller'
    line.className = 'cm-line'
    scroller.append(line)
    document.body.append(scroller)
    scroller.scrollTop = 120

    let top = 100
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: vi.fn(() => line),
    })
    vi.spyOn(line, 'getBoundingClientRect').mockImplementation(() => ({
      x: 0,
      y: top,
      top,
      right: 100,
      bottom: top + 24,
      left: 0,
      width: 100,
      height: 24,
      toJSON: () => ({}),
    }))

    preserveViewportDuringLayoutChange(() => {
      top = 260
    })

    expect(scroller.scrollTop).toBe(280)
  })

  it('uses the current caret screen position as the anchor when one is provided', () => {
    Object.defineProperty(document, 'scrollingElement', {
      configurable: true,
      value: document.documentElement,
    })
    document.documentElement.scrollTop = 100

    let caretTop = 260
    preserveViewportDuringLayoutChange(
      () => {
        caretTop = 510
      },
      () => ({ top: caretTop, container: document }),
    )

    expect(document.documentElement.scrollTop).toBe(350)
  })

  it('keeps a pointer-resolved logical position instead of re-resolving the pointer after reflow', () => {
    Object.defineProperty(document, 'scrollingElement', {
      configurable: true,
      value: document.documentElement,
    })
    document.documentElement.scrollTop = 100

    let resolvedTop = 260
    let livePointerTop = 260
    preserveViewportDuringLayoutChange(
      () => {
        // A pointer lookup after reflow can resolve to a different line. The anchor must
        // re-read the original logical position captured before the layout change instead.
        livePointerTop = 510
      },
      () => ({
        top: livePointerTop,
        container: document,
        readTop: () => resolvedTop,
      }),
    )

    expect(document.documentElement.scrollTop).toBe(100)
  })
})
