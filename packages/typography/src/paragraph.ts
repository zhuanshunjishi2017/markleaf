import type { BreakCandidate, LayoutItem, ScriptClass, TextMetrics } from './model'
import { classifyCharacter, splitGraphemes } from './script'
import { canBreakAfter, canBreakBefore } from './punctuation'

export type ParagraphMeasurements = (
  text: string,
  script: ScriptClass,
) => TextMetrics

function appendBox(
  items: LayoutItem[],
  text: string,
  script: ScriptClass,
  from: number,
  to: number,
  measure: ParagraphMeasurements,
): void {
  items.push({
    kind: 'box',
    text,
    script,
    from,
    to,
    width: measure(text, script).width,
  })
}

export function buildParagraph(input: {
  text: string
  availableWidth: number
  measure: ParagraphMeasurements
}): { items: LayoutItem[]; breaks: BreakCandidate[] } {
  const items: LayoutItem[] = []
  const breaks: BreakCandidate[] = []
  const clusters = splitGraphemes(input.text)
  let index = 0

  while (index < clusters.length) {
    const cluster = clusters[index]!
    const script = classifyCharacter(cluster)
    const from = index

    if (script === 'space') {
      let end = index
      let text = ''
      while (end < clusters.length && classifyCharacter(clusters[end]!) === 'space') {
        text += clusters[end]!
        end += 1
      }
      const metrics = input.measure(text, 'space')
      items.push({
        kind: 'glue',
        text,
        script: 'space',
        from,
        to: end,
        width: metrics.width,
        stretch: metrics.stretch,
        shrink: metrics.shrink,
      })
      if (items.length >= 2) {
        breaks.push({
          itemIndex: items.length - 2,
          sourceTo: end,
          penalty: 0,
          flagged: false,
        })
      }
      index = end
      continue
    }

    if (script === 'latin' || script === 'digit') {
      let end = index
      let text = ''
      while (
        end < clusters.length &&
        ['latin', 'digit'].includes(classifyCharacter(clusters[end]!))
      ) {
        text += clusters[end]!
        end += 1
      }
      const metrics = input.measure(text, script)
      const previous = items.at(-1)
      if (previous?.kind === 'box' && previous.script === 'cjk') {
        const boundary = input.measure('', 'space')
        items.push({
          kind: 'glue',
          text: '',
          script: 'space',
          from,
          to: from,
          width: boundary.width,
          stretch: boundary.stretch,
          shrink: boundary.shrink,
        })
      }
      if (metrics.width > input.availableWidth) {
        appendBox(items, text, script, from, end, input.measure)
        return { items, breaks: [] }
      }
      appendBox(items, text, script, from, end, input.measure)
      index = end
      continue
    }

    const metrics = input.measure(cluster, script)
    const previous = items.at(-1)
    if (
      previous?.kind === 'box' &&
      (previous.script === 'latin' || previous.script === 'digit')
    ) {
      const boundary = input.measure('', 'space')
      items.push({
        kind: 'glue',
        text: '',
        script: 'space',
        from: index,
        to: index,
        width: boundary.width,
        stretch: boundary.stretch,
        shrink: boundary.shrink,
      })
    }
    if (metrics.width > input.availableWidth) {
      appendBox(items, cluster, script, from, index + 1, input.measure)
      return { items, breaks: [] }
    }
    appendBox(items, cluster, script, from, index + 1, input.measure)

    const next = clusters[index + 1]
    if (next && canBreakAfter(cluster) && canBreakBefore(next)) {
      breaks.push({
        itemIndex: items.length - 1,
        sourceTo: index + 1,
        penalty: script === 'cjk' ? -50 : -100,
        flagged: script === 'cjk',
      })
    }

    index += 1
  }

  return { items, breaks }
}
