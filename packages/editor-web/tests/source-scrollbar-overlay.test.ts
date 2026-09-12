import { describe, expect, it } from 'vitest'
import { sourceScrollbarThumbGeometry } from '../src/source-scrollbar'

describe('source scrollbar overlay geometry', () => {
  it('hides the thumb when the source content does not scroll', () => {
    const geometry = sourceScrollbarThumbGeometry({
      scrollTop: 0,
      clientHeight: 600,
      scrollHeight: 600,
      trackHeight: 600,
    })

    expect(geometry.visible).toBe(false)
  })

  it('keeps a minimum-sized thumb at the top while scrolled to the top', () => {
    const geometry = sourceScrollbarThumbGeometry({
      scrollTop: 0,
      clientHeight: 600,
      scrollHeight: 6000,
      trackHeight: 600,
    })

    expect(geometry.visible).toBe(true)
    expect(geometry.height).toBe(60)
    expect(geometry.top).toBe(0)
  })

  it('places the thumb at the bottom when scrolled to the bottom', () => {
    const geometry = sourceScrollbarThumbGeometry({
      scrollTop: 5400,
      clientHeight: 600,
      scrollHeight: 6000,
      trackHeight: 600,
    })

    expect(geometry.visible).toBe(true)
    expect(geometry.top).toBe(540)
    expect(geometry.top + geometry.height).toBe(600)
  })
})
