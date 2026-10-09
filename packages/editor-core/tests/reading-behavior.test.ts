import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createReadingBehavior } from '../src/reading-behavior'

let behavior: ReturnType<typeof createReadingBehavior> | undefined
let sheet: HTMLStyleElement
let thumb: CSSStyleDeclaration
let media: MediaQueryList

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'requestAnimationFrame', 'cancelAnimationFrame', 'performance'] })
  media = Object.assign(new EventTarget(), { matches: false }) as MediaQueryList
  vi.stubGlobal('matchMedia', () => media)
})

afterEach(() => {
  behavior?.dispose()
  behavior = undefined
  document.documentElement.className = ''
  document.body.className = ''
  vi.useRealTimers()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function mount(reduced = false) {
  Object.defineProperty(media, 'matches', { value: reduced, configurable: true })
  const previous = new Set(document.head.querySelectorAll('style'))
  behavior = createReadingBehavior(() => undefined)
  sheet = [...document.head.querySelectorAll('style')].find(style => !previous.has(style))!
  thumb = (sheet.sheet!.cssRules[0] as CSSStyleRule).style
  behavior.setAutoHideScrollbar(true)
  return behavior
}

const color = () => thumb.getPropertyValue('background-color')

describe('reading scrollbar animation', () => {
  it('fades scrollbars on scroll and inactivity without changing document-wide styles', () => {
    mount()
    const rootWrite = vi.spyOn(document.documentElement.style, 'setProperty')
    const rootStyle = document.documentElement.getAttribute('style')
    const bodyStyle = document.body.getAttribute('style')
    window.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(240)
    expect(color()).toContain('100%')
    vi.advanceTimersByTime(800)
    expect(color()).toContain(' 0%')
    expect(rootWrite).not.toHaveBeenCalled()
    expect(document.documentElement.getAttribute('style')).toBe(rootStyle)
    expect(document.body.getAttribute('style')).toBe(bodyStyle)
  })

  it('keeps scrollbars visible during continued scrolling and reveals them near the right edge', () => {
    mount()
    window.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(600)
    window.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(600)
    expect(color()).toContain('100%')
    vi.advanceTimersByTime(440)
    expect(color()).toContain(' 0%')
    window.dispatchEvent(new MouseEvent('mousemove', { clientX: 20 }))
    vi.advanceTimersByTime(240)
    expect(color()).toContain(' 0%')
    window.dispatchEvent(new MouseEvent('mousemove', { clientX: window.innerWidth - 10 }))
    vi.advanceTimersByTime(240)
    expect(color()).toContain('100%')
  })

  it('cancels a pending fade when automatic hiding is disabled', () => {
    const current = mount()
    window.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(64)
    current.setAutoHideScrollbar(false)
    expect(color()).toContain('100%')
    expect(document.documentElement.classList.contains('markleaf-auto-hide-scrollbar')).toBe(false)
    expect(document.body.classList.contains('markleaf-auto-hide-scrollbar')).toBe(false)
    window.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(1500)
    expect(color()).toContain('100%')
    expect(vi.getTimerCount()).toBe(0)
  })

  it('respects reduced motion without scheduling animation frames', () => {
    mount(true)
    const frames = vi.spyOn(window, 'requestAnimationFrame')
    window.dispatchEvent(new Event('scroll'))
    expect(color()).toContain('100%')
    vi.advanceTimersByTime(800)
    expect(color()).toContain(' 0%')
    expect(frames).not.toHaveBeenCalled()
  })

  it('removes its style and cancels callbacks and listeners on disposal', () => {
    const current = mount()
    window.dispatchEvent(new Event('scroll'))
    vi.advanceTimersByTime(64)
    current.dispose()
    behavior = undefined
    expect(sheet.isConnected).toBe(false)
    expect(document.documentElement.classList.contains('markleaf-auto-hide-scrollbar')).toBe(false)
    expect(document.body.classList.contains('markleaf-auto-hide-scrollbar')).toBe(false)
    window.dispatchEvent(new Event('scroll'))
    window.dispatchEvent(new MouseEvent('mousemove', { clientX: window.innerWidth - 5 }))
    vi.advanceTimersByTime(1500)
    expect(vi.getTimerCount()).toBe(0)
  })
})
