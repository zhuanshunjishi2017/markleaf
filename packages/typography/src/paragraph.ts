import type { BreakCandidate, LayoutItem, ScriptClass, TextMetrics } from './model'
import { canBreakAfter, canBreakBefore } from './punctuation'
import { classifyCharacter, splitGraphemes } from './script'

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
  metrics: TextMetrics,
): void {
  items.push({
    kind: 'box',
    text,
    script,
    from,
    to,
    width: metrics.width,
    stretch: metrics.stretch,
    shrink: metrics.shrink,
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
  const wordBoundaries = new Set<number>([0, clusters.reduce((offset, cluster) => offset + cluster.length, 0)])
  const wordSegmenter = new Intl.Segmenter(undefined, { granularity: 'word' })
  for (const segment of wordSegmenter.segment(input.text)) {
    wordBoundaries.add(segment.index + segment.segment.length)
  }
  const nextContentIndex = (start: number): number => {
    let index = start
    while (index < clusters.length && classifyCharacter(clusters[index]!) === 'space') {
      index += 1
    }
    return index
  }
  const breakBeforeNextContent = (start: number): boolean => {
    const index = nextContentIndex(start)
    return index >= clusters.length || canBreakBefore(clusters[index]!)
  }
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
        // Do not let a whitespace break strand a line-start-forbidden
        // punctuation mark on the next line.
        if (breakBeforeNextContent(end)) {
          breaks.push({
            itemIndex: items.length - 2,
            sourceTo: end,
            penalty: 0,
            flagged: false,
          })
        }
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
        appendBox(items, text, script, from, end, metrics)
        return { items, breaks: [] }
      }
      appendBox(items, text, script, from, end, metrics)
      const next = clusters[end]
      const lastCluster = text.at(-1) ?? ''
      if (
        next &&
        classifyCharacter(next) !== 'space' &&
        canBreakAfter(lastCluster) &&
        breakBeforeNextContent(end)
      ) {
        breaks.push({
          itemIndex: items.length - 1,
          sourceTo: end,
          penalty: -100,
          flagged: false,
        })
      }
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
      appendBox(items, cluster, script, from, index + 1, metrics)
      return { items, breaks: [] }
    }
    appendBox(items, cluster, script, from, index + 1, metrics)

    const next = clusters[index + 1]
    const sourceTo = nextContentIndex(index + 1)
    if (next && canBreakAfter(cluster) && breakBeforeNextContent(index + 1) && wordBoundaries.has(index + 1)) {
      breaks.push({
        itemIndex: items.length - 1,
        sourceTo,
        penalty: script === 'cjk' ? -50 : -100,
        flagged: script === 'cjk',
      })
    }

    index += 1
  }

  return { items, breaks }
}
