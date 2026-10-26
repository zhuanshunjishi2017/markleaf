/** Widths are logical (unzoomed) CSS pixels. Logical rows/cells never change. */
export type TableLayout = {
  version: 1
  shape: number[]
  width: number
  columns: number[]
  rows: Record<string, number[]>
}
export const TABLE_CELL_MIN = 40
export const TABLE_LAYOUT_ATTRIBUTE = 'data-markleaf-table-layout'
export const TABLE_LAYOUT_PREFIX = '<!-- markleaf:table-layout '
const MAX_WIDTH = 100_000
export const MAX_TABLE_LAYOUT_BYTES = 65_536
export function validTableShape(shape: number[]): boolean {
  return shape.length > 0 && shape.length <= 10_000 && shape[0]! > 0 && shape[0]! <= 256 && shape.every(n => n === shape[0])
}
export function parseTableLayout(value: unknown, shape: number[]): TableLayout | null {
  if (!validTableShape(shape)) return null
  try {
    if (typeof value === 'string' && new TextEncoder().encode(value).byteLength > MAX_TABLE_LAYOUT_BYTES) return null
    const data = typeof value === 'string' ? JSON.parse(value) : value
    if (!data || data.version !== 1 || !Array.isArray(data.shape) || JSON.stringify(data.shape) !== JSON.stringify(shape)
      || !Number.isFinite(data.width) || data.width < TABLE_CELL_MIN * shape[0]! || data.width > MAX_WIDTH
      || !data.rows || typeof data.rows !== 'object' || Array.isArray(data.rows)) return null
    const widths = (a: unknown): a is number[] => Array.isArray(a) && a.length === shape[0]
      && a.every(v => typeof v === 'number' && Number.isFinite(v) && v >= TABLE_CELL_MIN && v <= MAX_WIDTH)
      && Math.abs(a.reduce((sum: number, n: number) => sum + n, 0) - data.width) < .02
    if (!widths(data.columns)) return null
    const rows: Record<string, number[]> = {}
    for (const [key, row] of Object.entries(data.rows)) {
      if (!/^(0|[1-9]\d*)$/.test(key) || Number(key) >= shape.length || !widths(row)) return null
      rows[key] = [...row]
    }
    const result: TableLayout = { version: 1, shape: [...shape], width: data.width, columns: [...data.columns], rows }
    return JSON.stringify(result).length <= MAX_TABLE_LAYOUT_BYTES ? result : null
  } catch { return null }
}
/** Allocate above the hard minimum without making short columns arbitrarily wide. */
export function fitTableWidths(desired: readonly number[], width: number): number[] {
  const total = Math.max(width, desired.length * TABLE_CELL_MIN)
  const result = desired.map(() => TABLE_CELL_MIN)
  const extra = desired.map(n => Math.max(0, n - TABLE_CELL_MIN))
  const sum = extra.reduce((a, b) => a + b, 0)
  const remaining = total - result.length * TABLE_CELL_MIN
  const widths = result.map((n, i) => Math.round((n + remaining * (sum > 0 ? extra[i]! / sum : 1 / result.length)) * 1e6) / 1e6)
  if (widths.length) widths[widths.length - 1] = Math.round((total - widths.slice(0, -1).reduce((a, b) => a + b, 0)) * 1e6) / 1e6
  return widths
}
export function makeTableLayout(shape: number[], width: number, desired?: number[]): TableLayout {
  const columns = fitTableWidths(desired ?? shape.slice(0, 1).flatMap(n => Array(n).fill(TABLE_CELL_MIN)), width)
  return { version: 1, shape: [...shape], width: columns.reduce((a, b) => a + b, 0), columns, rows: {} }
}
export function resizeTableBoundary(layout: TableLayout, boundary: number, delta: number, row?: number): TableLayout {
  if (!Number.isFinite(delta) || !Number.isInteger(boundary) || boundary < 0 || boundary >= layout.columns.length - 1
    || row !== undefined && (!Number.isInteger(row) || row < 0 || row >= layout.shape.length)) return layout
  const affected = row === undefined ? [layout.columns, ...Object.values(layout.rows)] : [layout.rows[String(row)] ?? layout.columns]
  const minimum = Math.max(...affected.map(a => TABLE_CELL_MIN - a[boundary]!))
  const maximum = Math.min(...affected.map(a => a[boundary + 1]! - TABLE_CELL_MIN))
  const movement = Math.max(minimum, Math.min(maximum, delta))
  const change = (a: number[]) => a.map((v, i) => v + (i === boundary ? movement : i === boundary + 1 ? -movement : 0))
  const rows = Object.fromEntries(Object.entries(layout.rows).map(([k, v]) => [k, [...v]]))
  const columns = row === undefined ? change(layout.columns) : [...layout.columns]
  if (row === undefined) for (const key of Object.keys(rows)) rows[key] = change(rows[key]!)
  else rows[String(row)] = change(layout.rows[String(row)] ?? layout.columns)
  for (const key of Object.keys(rows)) if (rows[key]!.every((v, i) => Math.abs(v - columns[i]!) < .0001)) delete rows[key]
  return { ...layout, columns, rows }
}
/** The right outer border changes the final column in every row. */
export function resizeTableWidth(layout: TableLayout, delta: number): TableLayout {
  if (!Number.isFinite(delta)) return layout
  const last = layout.columns.length - 1
  const affected = [layout.columns, ...Object.values(layout.rows)]
  const minimum = Math.max(...affected.map(widths => TABLE_CELL_MIN - widths[last]!))
  const movement = Math.max(minimum, Math.min(MAX_WIDTH - layout.width, delta))
  const change = (widths: number[]) => widths.map((value, i) => i === last ? value + movement : value)
  return { ...layout, width: layout.width + movement, columns: change(layout.columns),
    rows: Object.fromEntries(Object.entries(layout.rows).map(([row, widths]) => [row, change(widths)])) }
}

export function serializeTableLayout(layout: TableLayout): string {
  // Six decimals bound file size and make repeated save/load idempotent.
  const rounded = JSON.parse(JSON.stringify(layout, (_key, value) => typeof value === 'number' ? Math.round(value * 1e6) / 1e6 : value))
  return JSON.stringify(rounded)
}
