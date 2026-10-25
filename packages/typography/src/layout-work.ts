export const typographyWorkLimits = Object.freeze({ frameMs: 4, activeMs: 128, edgeChecks: 65536,
  uniqueMeasurements: 256, initialBatch: 4, minBatch: 1, maxBatch: 16, quietMs: 150 })
export type LayoutBudgetReason = 'active-time' | 'edge-checks' | 'measurements'
export type LayoutWorkStats = Readonly<{ activeMs: number; edgeChecks: number; uniqueMeasurements: number }>
export type LayoutWork = {
  run<T>(work: () => T): T
  visitEdge(): boolean
  reserveMeasurements(keys: readonly string[]): boolean
  exhausted(): LayoutBudgetReason | undefined
  stats(): LayoutWorkStats
}
export type LayoutCheckpoint = { shouldYield(): boolean; isCancelled(): boolean; work: LayoutWork }

export function createLayoutWork(clock: () => number): LayoutWork {
  let activeMs = 0, edgeChecks = 0, started: number | undefined, reason: LayoutBudgetReason | undefined
  const keys = new Set<string>()
  const elapsed = () => activeMs + (started === undefined ? 0 : Math.max(0, clock() - started))
  return {
    run<T>(work: () => T): T {
      if (started !== undefined) throw new Error('Nested layout work segment')
      started = clock()
      try { return work() } finally { activeMs = elapsed(); started = undefined }
    },
    visitEdge() {
      if (edgeChecks >= typographyWorkLimits.edgeChecks) { reason = 'edge-checks'; return false }
      edgeChecks++; return true
    },
    reserveMeasurements(requests) {
      const fresh = [...new Set(requests)].filter(k => !keys.has(k))
      if (keys.size + fresh.length > typographyWorkLimits.uniqueMeasurements) { reason = 'measurements'; return false }
      fresh.forEach(k => keys.add(k)); return true
    },
    exhausted: () => reason ?? (elapsed() >= typographyWorkLimits.activeMs ? 'active-time' : undefined),
    stats: () => Object.freeze({ activeMs: elapsed(), edgeChecks, uniqueMeasurements: keys.size }),
  }
}
