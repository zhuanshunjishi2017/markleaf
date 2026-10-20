import { describe, expect, it } from 'vitest'
import { preloadMathRenderer, renderMathElement, renderMathToString } from '../src/katex-runtime'

describe('lazy KaTeX runtime', () => {
  it('does not load KaTeX styles while importing the editor runtime', async () => {
    await import('../src/math')

    expect(document.querySelector('link[href*="katex"]')).toBeNull()
    expect(renderMathToString('x^2')).toBeNull()
  })

  it('renders source immediately and replaces it after the renderer loads', async () => {
    const element = document.createElement('span')
    document.body.append(element)

    renderMathElement(element, 'x^2', false)
    expect(element.textContent).toBe('x^2')
    expect(element.querySelector('.katex')).toBeNull()

    await preloadMathRenderer()
    expect(renderMathToString('x^2')).toContain('katex')
    expect(element.textContent).not.toBe('x^2')
    expect(element.querySelector('.katex')).not.toBeNull()
    element.remove()
  })
})
