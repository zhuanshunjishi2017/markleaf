import type { AllocationResult, GapSlot, ParagraphSnapshot } from './layout-model'
import { graphemeOffsets } from './opportunities'

/** Conservative experimental rule set; changing it invalidates measured plans. */
export const typographySpacingRules = Object.freeze({
  version: 7,
  wordSpace: Object.freeze({ minRatio: .8, preferredMaxRatio: 1.4, maxReadableEm: 1, weight: 1 }),
  cjk: Object.freeze({ naturalEm: 0, minEm: 0, preferredMaxEm: .08, maxEm: .16, weight: 1 }),
  mixed: Object.freeze({ naturalEm: .125, minEm: .08, preferredMaxEm: .2, maxEm: .6, weight: .5 }),
})

export function* prepareGapSlots(snapshot: ParagraphSnapshot, spaceWidths: ReadonlyMap<number, number>): Generator<void, readonly GapSlot[]> {
  const offsets = graphemeOffsets(snapshot.text), result: GapSlot[] = []
  const runAt = (at: number) => snapshot.runs.find(r => r.from <= at && r.to > at)
  const han = (text: string) => /^\p{Script=Han}\p{M}*$/u.test(text)
  const latin = (text: string) => /^[\p{Script=Latin}\p{M}\p{N}]+$/u.test(text)
  for (let i = 0; i < offsets.length - 1; i++) {
    yield
    const from = offsets[i]!, to = offsets[i + 1]!, text = snapshot.text.slice(from, to)
    const leftRun = runAt(Math.max(0, from - 1)), rightRun = runAt(from)
    if (text === ' ' && snapshot.text[from - 1] !== ' ' && snapshot.text[to] !== ' ') {
      const w = spaceWidths.get(from)
      const rule = typographySpacingRules.wordSpace
      // Ordinary word spaces are elastic glue. The available measure is a
      // finite physical ceiling; a fixed stretch ratio must not reject a whole
      // English paragraph. Preferred spacing and cost keep loose lines rare.
      if (w !== undefined && w > 0 && Number.isFinite(w)) result.push({ id: `word-space:${from}`, at: from, kind: 'word-space', owner: { from, to }, edge: 'space', natural: w, min: rule.minRatio * w, preferredMax: rule.preferredMaxRatio * w, max: Math.max(rule.preferredMaxRatio * w, snapshot.width), weight: rule.weight, leftStyleKey: leftRun?.styleKey ?? '', rightStyleKey: rightRun?.styleKey ?? '' })
    }
    if (i === 0) continue
    const before = snapshot.text.slice(offsets[i - 1]!, from)
    const size = Math.min(parseFloat(leftRun?.css['font-size'] ?? '') || 16, parseFloat(rightRun?.css['font-size'] ?? '') || 16)
    const kind = han(before) && han(text) ? 'cjk' : han(before) && latin(text) || latin(before) && han(text) ? 'mixed' : undefined
    if (!kind) continue
    const rule = typographySpacingRules[kind]
    const suppressAutoGap = kind === 'mixed' && snapshot.cjkAutoSpacing === false
    const owner = han(text) ? { from, to } : { from: offsets[i - 1]!, to: from }
    result.push({ id: `${kind}:${from}`, at: from, kind, owner, edge: han(text) ? 'start' : 'end',
      natural: suppressAutoGap ? 0 : rule.naturalEm * size, min: suppressAutoGap ? 0 : rule.minEm * size,
      max: rule.maxEm * size, weight: rule.weight,
      preferredMax: rule.preferredMaxEm * size,
      leftStyleKey: leftRun?.styleKey ?? '', rightStyleKey: rightRun?.styleKey ?? '' })
  }
  return result
}

export function candidateGapSlots(snapshot: ParagraphSnapshot, spaceWidths: ReadonlyMap<number, number>): readonly GapSlot[] {
  const task = prepareGapSlots(snapshot, spaceWidths)
  let next = task.next()
  while (!next.done) next = task.next()
  return next.value
}

export function buildGapSlots(snapshot: ParagraphSnapshot, supportedEdges: ReadonlySet<string>, spaceWidths: ReadonlyMap<number, number>): readonly GapSlot[] {
  return candidateGapSlots(snapshot, spaceWidths).filter(s => supportedEdges.has(s.id))
}

/** Bounded weighted allocation for one tier. Zero-capacity resources never donate space. */
function distribute(slots: readonly GapSlot[], capacities: readonly number[], required: number, scales = capacities) {
  const amounts = slots.map(() => 0)
  let active = capacities.map((c, i) => c > 0 ? i : -1).filter(i => i >= 0)
  let remaining = required
  while (active.length && remaining > 1e-10) {
    const weights = active.map(i => slots[i]!.weight * scales[i]! ** 2)
    const sum = weights.reduce((a, b) => a + b, 0)
    const saturated = active.filter((i, n) => remaining * weights[n]! / sum >= capacities[i]!)
    if (!saturated.length) {
      active.forEach((i, n) => { amounts[i] = remaining * weights[n]! / sum })
      remaining = 0; break
    }
    for (const i of saturated) { amounts[i] = capacities[i]!; remaining -= capacities[i]! }
    active = active.filter(i => !saturated.includes(i))
  }
  const cost = amounts.reduce((n, a, i) => n + (scales[i]! > 0 ? a ** 2 / (slots[i]!.weight * scales[i]! ** 2) : 0), 0)
  return { amounts, cost }
}

/** Use preferred spacing first, then elastic spaces / bounded CJK and mixed gaps.
 * Charge word-space looseness relative to its natural width, not the measure. */
export function allocateGaps(slots: readonly GapSlot[], delta: number): AllocationResult {
  if (!Number.isFinite(delta) || new Set(slots.map(s => s.id)).size !== slots.length || slots.some(s =>
    ![s.natural, s.min, s.max, s.weight].every(Number.isFinite) || s.min < 0 || s.min > s.natural || s.natural > s.max || s.weight <= 0
    || s.preferredMax !== undefined && (!Number.isFinite(s.preferredMax) || s.preferredMax < s.natural || s.preferredMax > s.max))) {
    return { feasible: false, reason: 'invalid-input' }
  }
  const sign = delta < 0 ? -1 : 1
  const capacities = slots.map(s => sign > 0 ? s.max - s.natural : s.natural - s.min)
  if (Math.abs(delta) > capacities.reduce((a, b) => a + b, 0) + 1e-9) return { feasible: false, reason: 'capacity-exceeded' }
  const preferred = slots.map((s, i) => sign > 0 ? (s.preferredMax ?? s.max) - s.natural : capacities[i]!)
  const normalTotal = preferred.reduce((a, b) => a + b, 0)
  const normal = distribute(slots, preferred, Math.min(Math.abs(delta), normalTotal))
  const extraCapacities = capacities.map((c, i) => c - preferred[i]!)
  const extraScales = slots.map((s, i) => s.kind === 'word-space' && s.natural > 0 ? s.natural : extraCapacities[i]!)
  // Allocation and cost must use the same scale. The physical space ceiling
  // is not a flexibility weight: it would starve dozens of CJK/mixed gaps.
  const extra = Math.abs(delta) > normalTotal + 1e-9
    ? distribute(slots, extraCapacities, Math.abs(delta) - normalTotal, extraScales) : undefined
  const gaps = slots.map((s, i) => {
    const width = Math.max(s.min, Math.min(s.max, s.natural + sign * (normal.amounts[i]! + (extra?.amounts[i] ?? 0))))
    return { slotId: s.id, width, adjustment: width - s.natural }
  })
  const cost = normal.cost + (extra ? 100 + 16 * extra.cost : 0)
  return { feasible: true, gaps, cost, residual: delta - gaps.reduce((n, g) => n + g.adjustment, 0) }
}
