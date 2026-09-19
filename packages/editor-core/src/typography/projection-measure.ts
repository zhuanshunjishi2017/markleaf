import type { ScriptClass, TextMetrics } from '@markleaf/typography'
import type { TypographyMeasurement } from './projection'

export function createDomTypographyMeasurement(
  getReference: () => HTMLElement | null,
): TypographyMeasurement {
  const host = document.createElement('div')
  host.className = 'markleaf-typography-measure'
  host.setAttribute('aria-hidden', 'true')
  host.append(document.createElement('span'))
  document.body.append(host)

  return (text: string, script: ScriptClass): TextMetrics => {
    const reference = getReference()
    const referenceStyle = reference ? window.getComputedStyle(reference) : null
    if (referenceStyle) {
      host.style.fontFamily = referenceStyle.fontFamily
      host.style.fontSize = referenceStyle.fontSize
      host.style.fontWeight = referenceStyle.fontWeight
      host.style.fontStyle = referenceStyle.fontStyle
      host.style.lineHeight = referenceStyle.lineHeight
      host.style.letterSpacing = referenceStyle.letterSpacing
      // The renderer compresses halt-capable CJK punctuation with the same
      // OpenType feature. Measuring both sides with identical features keeps
      // the LayoutPlan and WebKit line boxes in agreement.
      host.style.setProperty('font-feature-settings', '"halt" 1')
    }

    const child = host.firstElementChild as HTMLElement
    child.style.setProperty('font-feature-settings', '"halt" 1')
    child.textContent = text
    const width = child.getBoundingClientRect().width

    if (text === '') {
      const fontSize = Number.parseFloat(referenceStyle?.fontSize ?? '16') || 16
      return { width: 0, stretch: fontSize * 0.25, shrink: fontSize * 0.125 }
    }
    if (script === 'space') {
      return { width, stretch: width * 0.5, shrink: width * 0.25 }
    }
    if (script === 'cjk') {
      return { width, stretch: width * 0.125, shrink: width * 0.0625 }
    }
    return { width, stretch: 0, shrink: 0 }
  }
}
