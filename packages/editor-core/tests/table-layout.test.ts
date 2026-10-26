import { expect, it } from 'vitest'
import { makeTableLayout, parseTableLayout, resizeTableBoundary, fitTableWidths } from '../src/table-layout'
it('changes all rows or one selected row and clamps against all affected cells', () => {
  const initial = { ...makeTableLayout([2, 2], 300, [100, 200]), rows: { '1': [150, 150] } }
  const local = resizeTableBoundary(initial, 0, 20, 1)
  expect(local.columns).toEqual(initial.columns)
  expect(local.rows['1']).toEqual([170, 130])
  const whole = resizeTableBoundary(initial, 0, 500)
  expect(whole.rows['1']).toEqual([260, 40])
  expect(whole.columns.reduce((a, b) => a + b)).toBe(300)
})
it('rejects unsafe, oversized, stale and nonfinite metadata', () => {
  const layout = makeTableLayout([2, 2], 300)
  expect(parseTableLayout(layout, [2, 2])).toEqual(layout)
  for (const invalid of [{ ...layout, width: Infinity }, { ...layout, version: 2 }, { ...layout, rows: { '__proto__': [150, 150], '20': [150, 150] } }, '{bad}']) expect(parseTableLayout(invalid, [2, 2])).toBeNull()
  expect(parseTableLayout(layout, [2, 2, 2])).toBeNull()
  expect(parseTableLayout(JSON.stringify({ ...layout, extra: '中'.repeat(30000) }), [2, 2])).toBeNull()
})
it('allocates widths above the minimum even when the container is too narrow', () => {
  expect(fitTableWidths([40, 100, 500], 80)).toEqual([40, 40, 40])
  expect(fitTableWidths([40, 100], 240)).toEqual([40, 200])
})
it('clamps the outer edge to the narrowest final cell across row overrides', async () => {
  const { resizeTableWidth } = await import('../src/table-layout')
  const initial = { ...makeTableLayout([2, 2], 300, [100, 200]), rows: { '1': [150, 150] } }
  const shrunk = resizeTableWidth(initial, -1000)
  expect(shrunk.width).toBe(190)
  expect(shrunk.columns).toEqual([100, 90])
  expect(shrunk.rows['1']).toEqual([150, 40])
  expect(parseTableLayout(shrunk, [2, 2])).toEqual(shrunk)
  const grown = resizeTableWidth(initial, 1000000)
  expect(grown.width).toBe(100000)
  expect(parseTableLayout(grown, [2, 2])).toEqual(grown)
})
