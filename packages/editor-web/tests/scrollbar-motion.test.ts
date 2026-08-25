import { afterEach, describe, expect, it } from 'vitest'
import {
  bindReducedMotionPreference,
  createScrollbarAlphaController,
  scrollbarAlphaAnimation,
  type MotionPreferenceSource,
} from '../src/scrollbar-motion'

class MotionPreference implements MotionPreferenceSource {
  matches: boolean
  private listeners = new Set<(event: { matches: boolean }) => void>()

  constructor(matches: boolean) {
    this.matches = matches
  }

  addEventListener(_type: 'change', listener: (event: { matches: boolean }) => void): void {
    this.listeners.add(listener)
  }

  removeEventListener(_type: 'change', listener: (event: { matches: boolean }) => void): void {
    this.listeners.delete(listener)
  }

  setReducedMotion(matches: boolean): void {
    this.matches = matches
    const event = { matches }
    for (const listener of this.listeners) listener(event)
  }
}

describe('scrollbar motion accessibility', () => {
  afterEach(() => {
    document.documentElement.className = ''
    document.body.className = ''
  })

  it('disables scrollbar fading when the system initially requests reduced motion', () => {
    const preference = new MotionPreference(true)

    bindReducedMotionPreference(preference, document.documentElement, document.body)

    expect(document.documentElement.classList.contains('markleaf-reduced-motion')).toBe(true)
    expect(document.body.classList.contains('markleaf-reduced-motion')).toBe(true)
  })

  it('tracks live changes to the system Reduce Motion setting and cleans up its listener', () => {
    const preference = new MotionPreference(false)
    const cleanup = bindReducedMotionPreference(preference, document.documentElement, document.body)

    preference.setReducedMotion(true)
    expect(document.documentElement.classList.contains('markleaf-reduced-motion')).toBe(true)

    preference.setReducedMotion(false)
    expect(document.documentElement.classList.contains('markleaf-reduced-motion')).toBe(false)

    cleanup()
    preference.setReducedMotion(true)
    expect(document.documentElement.classList.contains('markleaf-reduced-motion')).toBe(false)
  })
})

describe('scrollbar alpha animation', () => {
  type FrameCallback = (time: number) => void

  function scheduler() {
    const frames: FrameCallback[] = []
    const state = {
      nowValue: 0,
      lastRequestedId: 0,
      nextStepAt: 16,
      advance(durationMs: number) {
        state.nowValue += durationMs
      },
      requestFrame(cb: FrameCallback) {
        state.lastRequestedId += 1
        frames.push(cb)
        return state.lastRequestedId
      },
      cancelFrame() {
        frames.length = 0
      },
      now() {
        return state.nowValue
      },
      runFrame() {
        const cb = frames.shift()
        cb?.(state.nowValue)
      },
      frameCount() {
        return frames.length
      },
    }
    return state
  }

  it('keeps the current fade running when repeated scroll events request the same target', () => {
    const s = scheduler()
    const samples: number[] = []
    const controller = createScrollbarAlphaController(
      0,
      200,
      (alpha) => samples.push(alpha),
      s,
    )

    controller.animateTo(1, false)
    expect(s.frameCount()).toBe(1)

    controller.animateTo(1, false)
    expect(s.frameCount()).toBe(1)

    s.advance(100)
    s.runFrame()
    const midAnimationSample = samples[samples.length - 1]

    controller.animateTo(1, false)
    expect(s.frameCount()).toBe(1)
    expect(samples[samples.length - 1]).toBe(midAnimationSample)
  })

  it('starts from the current alpha instead of flashing the target before the first frame', () => {
    const s = scheduler()
    const samples: number[] = []

    scrollbarAlphaAnimation(
      0,
      1,
      200,
      false,
      (alpha) => samples.push(alpha),
      s,
    )

    expect(samples).toEqual([0])
    expect(s.frameCount()).toBe(1)
  })

  it('increases alpha toward the target frame by frame and lands on the target', () => {
    const s = scheduler()
    const samples: number[] = []
    const cancel = scrollbarAlphaAnimation(
      0,
      1,
      200,
      false,
      (a) => samples.push(a),
      s,
    )

    s.advance(100)
    s.runFrame()
    expect(samples[samples.length - 1]).toBeGreaterThan(0)
    expect(samples[samples.length - 1]).toBeLessThan(1)

    s.advance(150)
    s.runFrame()
    expect(samples[samples.length - 1]).toBe(1)

    cancel()
    expect(s.frameCount()).toBe(0)
  })

  it('jumps immediately to the target when reduced motion is requested', () => {
    const s = scheduler()
    const samples: number[] = []
    scrollbarAlphaAnimation(
      1,
      0,
      300,
      true,
      (a) => samples.push(a),
      s,
    )

    expect(samples).toEqual([0])
    expect(s.frameCount()).toBe(0)
  })
})
