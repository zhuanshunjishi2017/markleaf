import { describe, expect, it } from 'vitest'
import { clearDomSelection } from '../src/native-selection'

describe('native selection cleanup', () => {
  it('clears an active DOM selection for WebKit hosts', () => {
    const owner = document.createElement('div')
    owner.textContent = 'alpha beta'
    document.body.append(owner)
    const range = document.createRange()
    range.selectNodeContents(owner)
    const selection = document.getSelection()!
    selection.removeAllRanges()
    selection.addRange(range)
    expect(clearDomSelection(owner.ownerDocument)).toBe(true)
    expect(selection.isCollapsed).toBe(true)
    owner.remove()
  })

  it('returns false when there is no active selection', () => {
    document.getSelection()?.removeAllRanges()
    expect(clearDomSelection(document)).toBe(false)
  })
})
