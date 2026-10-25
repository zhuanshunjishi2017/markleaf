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
const protectedBoundary = (s: string) => s === '\u00a0' || s === '\u2060' || s === '\u2011'

export type BreakOpportunity = { at: number; kind: 'allowed' | 'forced'; preferenceCost: number }
export function* prepareBreakOpportunities(text: string): Generator<void, readonly BreakOpportunity[]> {
  const gs = splitGraphemes(text)
  const offsets = [0]
  for (const g of gs) { offsets.push(offsets.at(-1)! + g.length); yield }
  const tokenInteriors = new Set<number>(), hyphenEnds = new Set<number>()
  // Treat a URL scheme / Windows drive and the rest of its resource token as
  // one unit, including consecutive separators such as :// or :\\.
  for (const match of text.matchAll(/\b(?:[a-z][a-z0-9+.-]*:\/\/|www\.)\S+|[A-Za-z]:[\\/]\S+/giu)) {
    for (let p = match.index + 1; p < match.index + match[0].length; p++) { tokenInteriors.add(p); yield }
  }
  for (const match of text.matchAll(/[\p{Script=Latin}\p{M}\p{N}]+(?:['’_.:/-][\p{Script=Latin}\p{M}\p{N}]+)*/gu)) {
    const compound = /^[\p{Script=Latin}\p{M}]+(?:-[\p{Script=Latin}\p{M}]+)+$/u.test(match[0])
    for (let p = match.index + 1; p < match.index + match[0].length; p++) {
      if (compound && text[p - 1] === '-' && !tokenInteriors.has(p)) hyphenEnds.add(p)
      else tokenInteriors.add(p)
      yield
    }
  }
  const out: Array<{ at: number; kind: 'allowed' | 'forced'; preferenceCost: number }> = []
  // Straight quotes have no distinct opening/closing code points. A quote
  // following a word (including an inch mark) belongs to that word's end;
  // after whitespace or an opening bracket it starts the quoted content.
  const opensStraightQuote = (i: number) => gs[i] === '"'
    && (i === 0 || gs[i - 1] === ' ' || opening.has(gs[i - 1]!))
  for (let i = 1; i < gs.length; i++) {
    yield
    if (tokenInteriors.has(offsets[i]!)) continue
    const left = gs[i - 1]!, right = gs[i]!
    if (protectedBoundary(left) || protectedBoundary(right) || right === ' ') continue
    // Spaces cannot evade punctuation restrictions on either side of the run.
    let previous = i - 1
    while (previous >= 0 && gs[previous] === ' ') previous--
    if (opening.has(gs[previous] ?? '') || opensStraightQuote(previous)
      || closing.has(right) || right === '"' && !opensStraightQuote(i)) continue
    if (word(left) && word(right)) continue
    if ((left === '…' || left === '—') && right === left) {
      let start = i - 1
      while (start > 0 && gs[start - 1] === left) start--
      if ((i - start) % 2 === 1) continue
    }
    out.push({ at: offsets[i]!, kind: 'allowed', preferenceCost: hyphenEnds.has(offsets[i]!) ? 150 : 0 })
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
