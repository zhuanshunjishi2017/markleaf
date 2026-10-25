/** Geometry contracts. All offsets are paragraph-local UTF-16, never grapheme indices. */
export type SourceRange = Readonly<{ from: number; to: number }>
export type StyleRun = SourceRange & Readonly<{ styleKey: string; css: Readonly<Record<string, string>> }>
export type ParagraphSnapshot = Readonly<{
  id: string; revision: number; environmentKey: string; text: string; width: number
  graphemeOffsets: readonly number[]; runs: readonly StyleRun[]
  language: string; direction: 'ltr'; alignment: 'default' | 'justify'
  /** False removes automatic base gaps; justification may still use boundaries. */
  cjkAutoSpacing?: boolean
}>
export type DiagnosticReason = 'unsupported-block' | 'unsupported-text' | 'author-alignment'
  | 'font-pending' | 'no-legal-break' | 'capacity-exceeded' | 'measurement-mismatch' | 'budget-exceeded' | 'stale'
export type GapSlot = Readonly<{
  id: string; at: number; kind: 'word-space' | 'cjk' | 'mixed'
  owner: SourceRange; edge: 'start' | 'end' | 'space'
  natural: number; min: number; max: number; weight: number
  /** Positive expansion beyond this bound is reserved for otherwise unfillable lines. */
  preferredMax?: number
  leftStyleKey: string; rightStyleKey: string
}>
export type GapAllocation = Readonly<{ slotId: string; width: number; adjustment: number }>
export type AllocationResult =
  | { feasible: true; gaps: readonly GapAllocation[]; residual: number; cost: number }
  | { feasible: false; reason: 'capacity-exceeded' | 'invalid-input' }
export type LineRequest = SourceRange & Readonly<{ contentTo: number; key: string }>
export type LineMeasurement = Readonly<{ key: string; naturalWidth: number }>
export type MeasuredLine = LineRequest & Readonly<{
  targetWidth: number; naturalWidth: number; gaps: readonly GapAllocation[]
  slots: readonly GapSlot[]; cost: number; breakReason: 'optimal' | 'forced' | 'paragraph-end'
}>
export type MeasuredPlan = Readonly<{
  snapshotId: string; revision: number; environmentKey: string
  lines: readonly MeasuredLine[]; verified: boolean
}>
