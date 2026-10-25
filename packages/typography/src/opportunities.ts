import { splitGraphemes } from './script'

export function graphemeOffsets(text: string): readonly number[] {
  const offsets = [0]
  for (const cluster of splitGraphemes(text)) offsets.push(offsets.at(-1)! + cluster.length)
  return offsets
}

/** Deliberately a supported subset, not a claim of general Unicode layout support. */
export function supportsTypographyText(text: string): boolean {
  return splitGraphemes(text).every(g =>
    /^[\p{Script=Han}\p{Script=Latin}\p{M}\p{N}\p{P}\p{S} \u00a0\u2060\u200d\ufe0f]+$/u.test(g),
  )
}

const opening = new Set(Array.from('（〔［｛《〈「『【〖〘〚“‘([{'))
const closing = new Set(Array.from('）〕］｝》〉」』】〗〙〛”’)]}，。、；：？！,.;:!?'))
const word = (s: string) => /^[\p{Script=Latin}\p{M}\p{N}]+$/u.test(s)
const protectedSpace = (s: string) => s === '\u00a0' || s === '\u2060'

export type BreakOpportunity = { at: number; kind: 'allowed' | 'forced'; preferenceCost: number }
export function* prepareBreakOpportunities(text: string): Generator<void, readonly BreakOpportunity[]> {
  const gs = splitGraphemes(text)
  const offsets = [0]
  for (const g of gs) { offsets.push(offsets.at(-1)! + g.length); yield }
  const tokenInteriors = new Set<number>()
  for (const match of text.matchAll(/[\p{Script=Latin}\p{M}\p{N}]+(?:['’_.:/-][\p{Script=Latin}\p{M}\p{N}]+)*/gu)) {
    for (let p = match.index + 1; p < match.index + match[0].length; p++) { tokenInteriors.add(p); yield }
  }
  const out: Array<{ at: number; kind: 'allowed' | 'forced'; preferenceCost: number }> = []
  for (let i = 1; i < gs.length; i++) {
    yield
    if (tokenInteriors.has(offsets[i]!)) continue
    const left = gs[i - 1]!, right = gs[i]!
    if (protectedSpace(left) || protectedSpace(right) || right === ' ') continue
    // Spaces cannot evade punctuation restrictions on either side of the run.
    let previous = i - 1
    while (previous >= 0 && gs[previous] === ' ') previous--
    if (opening.has(gs[previous] ?? '') || closing.has(right)) continue
    if (word(left) && word(right)) continue
    if ((left === '…' || left === '—') && right === left) {
      let start = i - 1
      while (start > 0 && gs[start - 1] === left) start--
      if ((i - start) % 2 === 1) continue
    }
    out.push({ at: offsets[i]!, kind: 'allowed', preferenceCost: 0 })
  }
  if (text.length) out.push({ at: text.length, kind: 'forced', preferenceCost: 0 })
  return out
}

export function collectBreakOpportunities(text: string): readonly BreakOpportunity[] {
  const task = prepareBreakOpportunities(text)
  let next = task.next()
  while (!next.done) next = task.next()
  return next.value
}
