import { describe, expect, it } from 'vitest'
import { clearDomSelection } from '../src/native-selection'

describe('native selection cleanup', () => {
  it('removes an existing native selection so WKWebView cannot leave stale blue highlights', () => {
    const owner = document.createElement('div')
    owner.innerHTML = '<span>alpha</span><span>beta</span>'
    document.body.append(owner)
    const range = document.createRange()
    range.selectNodeContents(owner)
    const selection = document.getSelection()
    selection?.removeAllRanges()
    selection?.addRange(range)

    expect(clearDomSelection(owner.ownerDocument)).toBe(true)
    expect(document.getSelection()?.isCollapsed).toBe(true)
    owner.remove()
  })

  it('reports false when there is no active range', () => {
    document.getSelection()?.removeAllRanges()
    expect(clearDomSelection(document)).toBe(false)
  })
})
