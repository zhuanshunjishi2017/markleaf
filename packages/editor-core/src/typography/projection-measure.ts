import type { ScriptClass, TextMetrics } from '@markleaf/typography'
import type { TypographyMeasurement } from './projection'
import type { GapSlot, LineMeasurement, LineRequest, ParagraphSnapshot } from '@markleaf/typography'
import { buildTypographyInlineDom, typographyInlinePieces } from './projection-dom'

export type MeasurementService = {
  onBatch?: (stats: { requested: number; cacheHits: number; domMeasured: number }) => void
  measureBatch(snapshot: ParagraphSnapshot, lines: readonly LineRequest[], slots: readonly GapSlot[]): readonly LineMeasurement[]
  dispose(): void
}

export function createTypographyMeasurementService(doc: Document): MeasurementService {
  const host = doc.createElement('div')
  host.dataset.typographyMeasure = 'true'
  host.setAttribute('aria-hidden', 'true')
  host.style.cssText = 'position:fixed;left:-100000px;top:0;visibility:hidden;pointer-events:none;white-space:pre;width:max-content;'
  doc.body.append(host)
  // Keep the large environment/slot signature once per domain, not once per
  // range. Repeated text with identical shaping can share an actual DOM read.
  const cache = new Map<string, { ranges: Map<string, number>; shapes: Map<string, number> }>()
  let cacheEntries = 0, disposed = false
  const service: MeasurementService = {
    measureBatch(snapshot, lines, slots) {
      if (disposed) throw new Error('Typography measurement disposed')
      if (!lines.length) { service.onBatch?.({ requested: 0, cacheHits: 0, domMeasured: 0 }); return [] }
      try {
      const domainKey = `${snapshot.environmentKey}:${JSON.stringify(slots)}`
      let domain = cache.get(domainKey)
      if (!domain) { domain = { ranges: new Map(), shapes: new Map() }; cache.set(domainKey, domain) }
      const pending = new Map<string, HTMLElement>(), requests = new Map<string, string>()
      for (const line of lines) {
        const rangeKey = `${line.from}:${line.contentTo}`
        if (domain.ranges.has(rangeKey)) continue
        const pieces = typographyInlinePieces(snapshot, line, slots, [])
        const shapeKey = JSON.stringify(pieces.map(p => [snapshot.text.slice(p.from, p.to), p.css, p.spacing]))
        requests.set(rangeKey, shapeKey)
        if (domain.shapes.has(shapeKey) || pending.has(shapeKey)) continue
        const element = doc.createElement('span')
        element.style.cssText = 'display:inline-block;white-space:pre;direction:ltr;text-align:left;'
        element.lang = snapshot.language
        element.append(buildTypographyInlineDom(doc, snapshot, line, slots, []))
        host.append(element)
        pending.set(shapeKey, element)
      }
      for (const [key, element] of pending) { domain.shapes.set(key, element.getBoundingClientRect().width); cacheEntries++ }
      for (const [key, shape] of requests) { domain.ranges.set(key, domain.shapes.get(shape)!); cacheEntries++ }
      service.onBatch?.({ requested: lines.length, cacheHits: lines.length - pending.size, domMeasured: pending.size })
      const results = lines.map(line => ({ key: line.key, naturalWidth: domain.ranges.get(`${line.from}:${line.contentTo}`)! }))
      // Bound both stored widths and offset aliases during long sessions.
      if (cacheEntries > 2048) { cache.clear(); cacheEntries = 0 }
      return results
      } finally { host.replaceChildren() }
    },
    dispose() { disposed = true; host.remove(); cache.clear(); cacheEntries = 0 },
  }
  return service
}

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
