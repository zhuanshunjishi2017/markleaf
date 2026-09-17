export type LineAdjustment =
  | { type: 'natural'; amount: 0 }
  | { type: 'stretch'; amount: number }
  | { type: 'shrink'; amount: number }
  | { type: 'overflow'; amount: number }

export function lineAdjustment(input: {
  availableWidth: number
  naturalWidth: number
  stretchWidth: number
  shrinkWidth: number
}): LineAdjustment {
  const delta = input.availableWidth - input.naturalWidth
  if (Math.abs(delta) < 0.0001) return { type: 'natural', amount: 0 }
  if (delta > 0) {
    if (input.stretchWidth <= 0 || delta > input.stretchWidth) {
      return { type: 'overflow', amount: delta }
    }
    return { type: 'stretch', amount: delta }
  }
  const shortage = -delta
  if (input.shrinkWidth <= 0 || shortage > input.shrinkWidth) {
    return { type: 'overflow', amount: shortage }
  }
  return { type: 'shrink', amount: shortage }
}

export function badness(input: {
  availableWidth: number
  naturalWidth: number
  stretchWidth: number
  shrinkWidth: number
}): number {
  const adjustment = lineAdjustment(input)
  if (adjustment.type === 'natural') return 0
  if (adjustment.type === 'overflow') return 10_000
  const available = adjustment.type === 'stretch'
    ? input.stretchWidth
    : input.shrinkWidth
  if (available <= 0) return 10_000
  return Math.min(10_000, Math.round(100 * (adjustment.amount / available) ** 3))
}

export function demerits(input: {
  badness: number
  penalty: number
  flagged: boolean
}): number {
  const badnessCost = (input.badness + 100) ** 2
  const penaltyCost = input.penalty < 0 ? (-input.penalty) ** 2 : 0
  const flagCost = input.flagged ? 300 : 0
  return badnessCost + penaltyCost + flagCost
}
