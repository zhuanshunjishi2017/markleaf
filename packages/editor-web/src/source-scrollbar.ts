export type SourceScrollbarGeometryInput = {
  scrollTop: number
  clientHeight: number
  scrollHeight: number
  trackHeight: number
  minimumThumbHeight?: number
}

export type SourceScrollbarGeometry = {
  visible: boolean
  top: number
  height: number
}

const clamp = (value: number, minimum: number, maximum: number): number => {
  return Math.min(maximum, Math.max(minimum, value))
}

export function sourceScrollbarThumbGeometry(
  input: SourceScrollbarGeometryInput,
): SourceScrollbarGeometry {
  const minimumThumbHeight = input.minimumThumbHeight ?? 32
  const trackHeight = Math.max(0, input.trackHeight)
  const maximumScroll = Math.max(0, input.scrollHeight - input.clientHeight)

  if (trackHeight === 0 || maximumScroll === 0) {
    return { visible: false, top: 0, height: 0 }
  }

  const height = clamp(
    (input.clientHeight / input.scrollHeight) * trackHeight,
    Math.min(minimumThumbHeight, trackHeight),
    trackHeight,
  )
  const maximumThumbTop = trackHeight - height
  const top = clamp(
    (input.scrollTop / maximumScroll) * maximumThumbTop,
    0,
    maximumThumbTop,
  )

  return { visible: true, top, height }
}

export class SourceScrollbarOverlay {
  private readonly container: HTMLDivElement
  private readonly thumb: HTMLDivElement
  private readonly scroller: HTMLElement
  private readonly resizeObserver: ResizeObserver
  private readonly mutationObserver: MutationObserver
  private updateScheduled = false

  constructor(
    private readonly host: HTMLElement,
    scroller: HTMLElement,
  ) {
    this.container = document.createElement('div')
    this.container.className = 'markleaf-source-scrollbar'
    this.container.setAttribute('aria-hidden', 'true')

    this.thumb = document.createElement('div')
    this.thumb.className = 'markleaf-source-scrollbar-thumb'
    this.container.appendChild(this.thumb)
    host.appendChild(this.container)

    this.scroller = scroller
    scroller.addEventListener('scroll', this.scheduleUpdate, { passive: true })
    window.addEventListener('resize', this.scheduleUpdate, { passive: true })
    this.resizeObserver = new ResizeObserver(this.scheduleUpdate)
    this.resizeObserver.observe(scroller)
    this.resizeObserver.observe(host)
    this.mutationObserver = new MutationObserver(this.scheduleUpdate)
    this.mutationObserver.observe(scroller, {
      childList: true,
      subtree: true,
      attributes: true,
    })
    this.update()
  }

  update(): void {
    this.updateScheduled = false
    const hostRect = this.host.getBoundingClientRect()
    const scrollerRect = this.scroller.getBoundingClientRect()
    const geometry = sourceScrollbarThumbGeometry({
      scrollTop: this.scroller.scrollTop,
      clientHeight: this.scroller.clientHeight,
      scrollHeight: this.scroller.scrollHeight,
      trackHeight: scrollerRect.height,
    })

    this.container.style.top = `${scrollerRect.top - hostRect.top}px`
    this.container.style.height = `${scrollerRect.height}px`
    this.container.style.right = `${hostRect.right - scrollerRect.right + 3}px`
    this.container.style.display = geometry.visible ? 'block' : 'none'
    this.thumb.style.transform = `translateY(${geometry.top}px)`
    this.thumb.style.height = `${geometry.height}px`
  }

  destroy(): void {
    this.scroller.removeEventListener('scroll', this.scheduleUpdate)
    window.removeEventListener('resize', this.scheduleUpdate)
    this.resizeObserver.disconnect()
    this.mutationObserver.disconnect()
    this.container.remove()
  }

  private readonly scheduleUpdate = (): void => {
    if (this.updateScheduled) return
    this.updateScheduled = true
    requestAnimationFrame(() => this.update())
  }
}
