export type ScrollContainer = Document | HTMLElement

export type ViewportAnchorReader = () => {
  top: number
  container?: ScrollContainer
  /**
   * Optional stable logical-position reader. Pointer-based anchors resolve a document position
   * before reflow, then use this callback to measure that same position afterwards.
   */
  readTop?: () => number
} | null

type ViewportAnchor = {
  readTop: () => number
  container: ScrollContainer
  viewportTop: number
}

const ANCHOR_SELECTOR = 'h1, h2, h3, h4, h5, h6, p, li, pre, table, blockquote, hr, img, .cm-line'

function scrollTopOf(container: ScrollContainer): number {
  if (container instanceof HTMLElement) {
    return container.scrollTop
  }

  return container.scrollingElement?.scrollTop
    ?? document.documentElement.scrollTop
    ?? document.body.scrollTop
    ?? 0
}

function setScrollTop(container: ScrollContainer, value: number): void {
  const top = Math.max(0, value)
  if (container instanceof HTMLElement) {
    container.scrollTop = top
    return
  }

  if (container.scrollingElement) {
    container.scrollingElement.scrollTop = top
  }
  document.documentElement.scrollTop = top
  document.body.scrollTop = top
}

function anchorElementAtViewport(): HTMLElement | null {
  const y = Math.min(
    Math.max(24, window.innerHeight * 0.35),
    Math.max(24, window.innerHeight - 24),
  )
  const point = document.elementFromPoint?.(window.innerWidth * 0.5, y)
  const direct = point instanceof HTMLElement ? point.closest<HTMLElement>(ANCHOR_SELECTOR) : null
  if (direct) {
    return direct
  }

  const candidates = Array.from(document.querySelectorAll<HTMLElement>(ANCHOR_SELECTOR))
  return candidates
    .map((element) => ({ element, rect: element.getBoundingClientRect() }))
    .filter(({ rect }) => rect.bottom > 0 && rect.top < window.innerHeight)
    .sort((left, right) => Math.abs(left.rect.top - y) - Math.abs(right.rect.top - y))[0]?.element ?? null
}

function scrollContainerFor(element: HTMLElement): ScrollContainer {
  const codeMirrorScroller = element.closest<HTMLElement>('.cm-scroller')
  return codeMirrorScroller ?? document
}

function captureViewportAnchor(reader?: ViewportAnchorReader): ViewportAnchor | null {
  if (reader) {
    const current = reader()
    if (current && Number.isFinite(current.top)) {
      return {
        readTop: current.readTop ?? (() => reader()?.top ?? current.top),
        container: current.container ?? document,
        viewportTop: current.top,
      }
    }
  }

  const element = anchorElementAtViewport()
  if (!element) {
    return null
  }

  return {
    readTop: () => element.getBoundingClientRect().top,
    container: scrollContainerFor(element),
    viewportTop: element.getBoundingClientRect().top,
  }
}

function restoreViewportAnchor(anchor: ViewportAnchor): void {
  const currentTop = anchor.readTop()
  if (!Number.isFinite(currentTop)) {
    return
  }
  const delta = currentTop - anchor.viewportTop
  if (Math.abs(delta) < 0.5) {
    return
  }

  setScrollTop(anchor.container, scrollTopOf(anchor.container) + delta)
}

/**
 * Run a layout-changing operation without changing the document block shown in the viewport.
 * Zoom changes font size and line wrapping, so preserving the numeric scrollTop alone is not
 * enough: the same scrollTop can point at a completely different part of the document.
 */
export function preserveViewportDuringLayoutChange(
  change: () => void,
  reader?: ViewportAnchorReader,
): void {
  const anchor = captureViewportAnchor(reader)
  change()
  if (!anchor) {
    return
  }

  // The synchronous pass avoids a visible jump. WebKit may settle font metrics on the next
  // frame, so repeat once after layout has been committed.
  restoreViewportAnchor(anchor)
  if (typeof window.requestAnimationFrame === 'function') {
    window.requestAnimationFrame(() => restoreViewportAnchor(anchor))
  }
}
