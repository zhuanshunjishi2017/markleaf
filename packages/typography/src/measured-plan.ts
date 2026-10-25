import type { DiagnosticReason, GapSlot, LineRequest, MeasuredPlan } from './layout-model'
import { createMeasuredPlanningTask, type MeasuredPlanningInput } from './measured-task'
import { createLayoutWork } from './layout-work'

export type PlanningResult = { status: 'planned'; plan: MeasuredPlan }
  | { status: 'needs-measurement'; requests: readonly LineRequest[] }
  | { status: 'fallback'; reason: DiagnosticReason }
export function slotsForLine(slots: readonly GapSlot[], line: LineRequest): readonly GapSlot[] {
  return slots.filter(s => s.owner.from >= line.from && s.owner.to <= line.contentTo
    && (s.kind === 'word-space' || s.at > line.from && s.at < line.contentTo))
}
export function typographyLineKey(from: number, to: number, contentTo: number): string { return `${from}:${to}:${contentTo}` }

/** Compatibility entry point. Production frame scheduling uses the task directly. */
export function planMeasuredParagraph(input: MeasuredPlanningInput & { shouldStop?: () => boolean }): PlanningResult {
  const task = createMeasuredPlanningTask(input), work = createLayoutWork(() => 0)
  try {
    for (;;) {
      if (input.shouldStop?.()) return { status: 'fallback', reason: 'budget-exceeded' }
      const result = task.step({ work, shouldYield: () => input.shouldStop?.() ?? false, isCancelled: () => false })
      if (result.status === 'yielded') continue
      if (result.status === 'cancelled') return { status: 'fallback', reason: 'stale' }
      return result
    }
  } finally { task.dispose() }
}
