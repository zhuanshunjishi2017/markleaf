import type { JSONContent } from '@tiptap/core'
import { afterEach, expect, it } from 'vitest'
import { createEditor, getMarkdown } from '../src/index'
import { applyTableLayoutDOM } from '../src/table-layout-extension'
import { resizeTableBoundary, type TableLayout } from '../src/table-layout'

const editors: ReturnType<typeof createEditor>[] = []
const layout: TableLayout = { version: 1, shape: [2, 2], width: 300, columns: [100, 200], rows: {} }
function setup(zoom = 1) {
  const host = document.createElement('div'); document.body.append(host)
  const editor = createEditor(host, `<!-- markleaf:table-layout ${JSON.stringify(layout)} -->\n| A | B |\n| --- | --- |\n| first | second |`); editors.push(editor)
  const table = host.querySelector('table')!
  Object.defineProperty(table, 'offsetWidth', { value: 300, configurable: true })
  table.getBoundingClientRect = () => ({ left: 0, right: 300 * zoom, width: 300 * zoom, top: 0, height: 60 * zoom, bottom: 60 * zoom, x: 0, y: 0, toJSON() {} })
  Array.from(table.rows).forEach((row, ri) => Array.from(row.cells).forEach((cell, ci) => {
    cell.getBoundingClientRect = () => ({ left: (ci ? 100 : 0) * zoom, right: (ci ? 300 : 100) * zoom, width: (ci ? 200 : 100) * zoom, top: ri * 30 * zoom, bottom: (ri + 1) * 30 * zoom, height: 30 * zoom, x: 0, y: 0, toJSON() {} })
  }))
  const positions: number[] = []; editor.state.doc.descendants((n, p) => { if (n.type.name === 'tableCell') positions.push(p) })
  editor.commands.setTextSelection(positions[0]! + 2)
  return { editor, table, host }
}
function pointer(target: EventTarget, name: string, x: number) {
  const event = new MouseEvent(name, { bubbles: true, cancelable: true, button: 0, clientX: x })
  Object.defineProperty(event, 'pointerId', { value: 1 }); target.dispatchEvent(event)
}
afterEach(() => { editors.splice(0).forEach(e => e.destroy()); document.body.innerHTML = '' })
it.each([.8, 1, 1.25, 2])('resizes a logical column at zoom %s with one-step undo', zoom => {
  const { editor, table } = setup(zoom)
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100 * zoom)
  pointer(document, 'pointerup', 120 * zoom)
  expect(editor.state.doc.firstChild!.attrs.tableLayout.columns).toEqual([120, 180])
  editor.commands.undo()
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
})
it('changes only the row whose cell text is fully selected', () => {
  const { editor, table } = setup()
  const pos = editor.state.selection.from
  editor.commands.setTextSelection({ from: pos, to: pos + 5 })
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100)
  pointer(document, 'pointerup', 120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(resizeTableBoundary(layout, 0, 20, 1))
})
it('cancels escape/pointercancel without touching markdown or history', () => {
  for (const cancel of ['escape', 'pointercancel', 'lostpointercapture']) {
    const { editor, table } = setup(); const initial = getMarkdown(editor); const undoBefore = editor.can().undo()
    pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100)
    if (cancel === 'escape') document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    else if (cancel === 'pointercancel') pointer(document, 'pointercancel', 120)
    else pointer(editor.view.dom, 'lostpointercapture', 120)
    pointer(document, 'pointerup', 120)
    expect(getMarkdown(editor)).toBe(initial)
    expect(editor.can().undo()).toBe(undoBefore)
  }
})
it('does not resize read-only tables or the left outer edge', () => {
  const { editor, table } = setup()
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 0); pointer(document, 'pointerup', 20)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
  editor.setEditable(false)
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
})
it('does not turn presentation attributes into repeated document transactions', () => {
  const { editor, table } = setup()
  const before = getMarkdown(editor)
  let count = 0
  editor.on('transaction', () => { count++ })
  const observer = (editor.view as unknown as { domObserver: { flush(): void } }).domObserver
  for (let i = 0; i < 5; i++) observer.flush()
  expect(count).toBe(0)
  expect(getMarkdown(editor)).toBe(before)
  expect(table.getAttribute('aria-colcount')).toBe('2')
})
it('keeps repeated projection passes mutation-free', () => {
  const { host } = setup()
  const observer = new MutationObserver(() => {})
  observer.observe(host, { attributes: true, childList: true, subtree: true })
  applyTableLayoutDOM(host)
  expect(observer.takeRecords()).toHaveLength(0)
  observer.disconnect()
})
it('keeps the narrowest default or local cell at the visual minimum', () => {
  const { editor, table, host } = setup()
  const narrow = { ...layout, columns: [40, 260], rows: { '1': [60, 240] } }
  editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs, tableLayout: narrow }))
  applyTableLayoutDOM(host)
  expect(table.style.minWidth).toContain('300px')
  const local = { ...narrow, columns: [100, 200], rows: { '1': [40, 260] } }
  editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs, tableLayout: local }))
  expect(table.style.minWidth).toContain('300px')
})

it('previews without document changes and cancels an already painted drag', async () => {
  const { editor, table, host } = setup()
  const initial = getMarkdown(editor)
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100)
  pointer(document, 'pointermove', 130)
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
  expect(JSON.parse(table.getAttribute('data-markleaf-table-layout')!).columns).toEqual([130, 170])
  expect(getMarkdown(editor)).toBe(initial)
  expect(host.querySelectorAll('.markleaf-table-resize-guide')).toHaveLength(2)
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  expect(JSON.parse(table.getAttribute('data-markleaf-table-layout')!).columns).toEqual([100, 200])
  expect(host.querySelectorAll('.markleaf-table-resize-guide')).toHaveLength(0)
  expect(getMarkdown(editor)).toBe(initial)
})

it.each([false, true])('recognizes complete forward/reverse text selection before dragging (%s)', reverse => {
  const { editor, table } = setup()
  const from = editor.state.selection.from
  editor.commands.setTextSelection({ from: reverse ? from + 5 : from, to: reverse ? from : from + 5 })
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(resizeTableBoundary(layout, 0, 20, 1))
})
it('keeps partial and cross-cell text selections in whole-column mode', () => {
  for (const crossCell of [false, true]) {
    const { editor, table } = setup()
    const from = editor.state.selection.from
    editor.commands.setTextSelection({ from, to: crossCell ? from + 13 : from + 4 })
    pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
    expect(editor.state.doc.firstChild!.attrs.tableLayout.columns).toEqual([120, 180])
    expect(editor.state.doc.firstChild!.attrs.tableLayout.rows).toEqual({})
  }
})
it('uses the full native text range even before the model selection has caught up', () => {
  const { editor, table } = setup()
  const text = table.rows[1]!.cells[0]!.querySelector('p')!.firstChild!
  const range = document.createRange(); range.setStart(text, 0); range.setEnd(text, 5)
  document.getSelection()!.removeAllRanges(); document.getSelection()!.addRange(range)
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(resizeTableBoundary(layout, 0, 20, 1))
})
it('does not mistake a partial native range for a stale full model selection', () => {
  const { editor, table } = setup()
  const from = editor.state.selection.from
  editor.commands.setTextSelection({ from, to: from + 5 })
  const text = table.rows[1]!.cells[0]!.querySelector('p')!.firstChild!
  const range = document.createRange(); range.setStart(text, 0); range.setEnd(text, 4)
  document.getSelection()!.removeAllRanges(); document.getSelection()!.addRange(range)
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout.columns).toEqual([120, 180])
})

it('recognizes all content across formatted paragraphs in one cell', () => {
  const { editor, table } = setup()
  const doc = editor.getJSON() as JSONContent
  doc.content![0]!.content![1]!.content![0]!.content = [
    { type: 'paragraph', content: [{ type: 'text', text: 'first', marks: [{ type: 'bold' }] }] },
    { type: 'paragraph', content: [{ type: 'text', text: 'second' }] },
  ]
  editor.commands.setContent(doc)
  let from = 0, to = 0
  editor.state.doc.descendants((node, pos) => { if (node.type.name === 'tableCell' && from === 0) { from = pos + 2; to = pos + node.nodeSize - 2 } })
  editor.commands.setTextSelection({ from, to })
  pointer(table.rows[1]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(resizeTableBoundary(layout, 0, 20, 1))
})
it('does not apply local widths to a different row or an empty-cell caret', () => {
  for (const empty of [false, true]) {
    const { editor, table } = setup()
    const from = editor.state.selection.from
    if (empty) editor.commands.deleteRange({ from, to: from + 5 })
    else editor.commands.setTextSelection({ from, to: from + 5 })
    pointer(table.rows[empty ? 1 : 0]!.cells[0]!, 'pointerdown', 100); pointer(document, 'pointerup', 120)
    expect(editor.state.doc.firstChild!.attrs.tableLayout.columns).toEqual([120, 180])
    expect(editor.state.doc.firstChild!.attrs.tableLayout.rows).toEqual({})
  }
})

it.each([.8, 1, 1.25, 2])('resizes total table width at its right outer edge at zoom %s', zoom => {
  const { editor, table } = setup(zoom)
  pointer(table.rows[1]!.cells[1]!, 'pointerdown', 300 * zoom)
  pointer(document, 'pointerup', 330 * zoom)
  const changed = editor.state.doc.firstChild!.attrs.tableLayout
  expect(changed.width).toBe(330)
  expect(changed.columns).toEqual([100, 230])
  editor.commands.undo()
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(layout)
})
it('outer-edge resizing changes every row even when one cell is fully selected', () => {
  const { editor, table } = setup()
  const initial = { ...layout, rows: { '1': [150, 150] } }
  editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs, tableLayout: initial }))
  const from = editor.state.selection.from
  editor.commands.setTextSelection({ from, to: from + 5 })
  pointer(table.rows[1]!.cells[1]!, 'pointerdown', 300); pointer(document, 'pointerup', 330)
  const changed = editor.state.doc.firstChild!.attrs.tableLayout
  expect(changed.width).toBe(330)
  expect(changed.columns).toEqual([100, 230])
  expect(changed.rows['1']).toEqual([150, 180])
})
it('starts outer-edge resizing from the visible width in a compressed container', () => {
  const { editor, table } = setup()
  const rect = { left: 0, right: 200, width: 200, top: 0, bottom: 60, height: 60, x: 0, y: 0, toJSON() {} }
  Object.defineProperty(table, 'offsetWidth', { value: 200 })
  table.getBoundingClientRect = () => rect
  const cell = table.rows[1]!.cells[1]!
  cell.getBoundingClientRect = () => ({ ...rect, left: 200 / 3, width: 400 / 3 })
  pointer(cell, 'pointerdown', 200); pointer(document, 'pointerup', 180)
  const changed = editor.state.doc.firstChild!.attrs.tableLayout
  expect(changed.width).toBe(180)
  expect(changed.columns[0]).toBeCloseTo(200 / 3, 5)
  expect(changed.columns[1]).toBeCloseTo(180 - 200 / 3, 5)
})
it('cancels an outer-edge preview without changing content or persisted widths', async () => {
  const { editor, table } = setup()
  const initial = getMarkdown(editor)
  pointer(table.rows[1]!.cells[1]!, 'pointerdown', 300); pointer(document, 'pointermove', 330)
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
  expect(JSON.parse(table.getAttribute('data-markleaf-table-layout')!).width).toBe(330)
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  expect(getMarkdown(editor)).toBe(initial)
  expect(JSON.parse(table.getAttribute('data-markleaf-table-layout')!).width).toBe(300)
})
it('hits the right border when the browser targets the table instead of its cell', () => {
  const { editor, table } = setup()
  pointer(table, 'pointerdown', 300); pointer(document, 'pointerup', 320)
  expect(editor.state.doc.firstChild!.attrs.tableLayout.width).toBe(320)
})
it('does not create a document edit when the outer edge is already at its limit', () => {
  const { editor, table } = setup()
  const narrow = { ...layout, columns: [260, 40] }
  editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs, tableLayout: narrow }))
  let edits = 0
  editor.on('transaction', ({ transaction }) => { if (transaction.docChanged) edits++ })
  pointer(table.rows[1]!.cells[1]!, 'pointerdown', 300); pointer(document, 'pointerup', 270)
  expect(edits).toBe(0)
  expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(narrow)
})

function fittingFixture(intrinsic = [[40, 40, 40], [260, 140, 200], [100, 100, 200]]) {
  const host = document.createElement('div'); document.body.append(host)
  const initial: TableLayout = { version: 1, shape: [3, 3, 3], width: 600, columns: [200, 200, 200], rows: { '2': [210, 190, 200] } }
  const editor = createEditor(host, `<!-- markleaf:table-layout ${JSON.stringify(initial)} -->\n| A | B | C |\n| --- | --- | --- |\n| first | second | third |\n| local | other | third |`); editors.push(editor)
  const table = host.querySelector('table')!
  Object.defineProperty(table, 'offsetWidth', { value: 600 })
  table.getBoundingClientRect = () => ({ left: 0, right: 600, width: 600, top: 0, bottom: 90, height: 90, x: 0, y: 0, toJSON() {} })
  Array.from(table.rows).forEach((row, ri) => Array.from(row.cells).forEach((cell, ci) => {
    const widths = initial.rows[String(ri)] ?? initial.columns
    const left = widths.slice(0, ci).reduce((a, b) => a + b, 0)
    cell.getBoundingClientRect = () => ({ left, right: left + widths[ci]!, width: widths[ci]!, top: ri * 30, bottom: (ri + 1) * 30, height: 30, x: left, y: ri * 30, toJSON() {} })
  }))
  const descriptor = Object.getOwnPropertyDescriptor(HTMLTableCellElement.prototype, 'offsetWidth')
  Object.defineProperty(HTMLTableCellElement.prototype, 'offsetWidth', { configurable: true, get() {
    const cell = this as HTMLTableCellElement
    return intrinsic[(cell.parentElement as HTMLTableRowElement).rowIndex]?.[cell.cellIndex] ?? 40
  } })
  const restore = () => {
    if (descriptor) Object.defineProperty(HTMLTableCellElement.prototype, 'offsetWidth', descriptor)
    else delete (HTMLTableCellElement.prototype as { offsetWidth?: number }).offsetWidth
  }
  return { editor, table, initial, restore }
}
function doubleClick(target: EventTarget, x: number, y = 0) {
  return target.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true, button: 0, detail: 2, clientX: x, clientY: y }))
}
it('double-click fits the adjacent pair, preserves other columns and undoes as one action', () => {
  const { editor, table, initial, restore } = fittingFixture()
  try {
    doubleClick(table.rows[1]!.cells[0]!, 200)
    const changed = editor.state.doc.firstChild!.attrs.tableLayout
    expect(changed.columns).toEqual([260, 140, 200])
    expect(changed.rows['2']).toBeUndefined()
    expect(changed.width).toBe(600)
    editor.commands.undo()
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(initial)
  } finally { restore() }
})
it('double-click fits only the selected cell row using that row content', () => {
  const { editor, table, initial, restore } = fittingFixture([[40, 40, 40], [260, 140, 200], [80, 320, 200]])
  try {
    const positions: number[] = []; editor.state.doc.descendants((node, pos) => { if (node.type.name === 'tableCell') positions.push(pos) })
    editor.commands.setTextSelection({ from: positions[3]! + 2, to: positions[3]! + 7 })
    doubleClick(table.rows[2]!.cells[0]!, 210)
    const changed = editor.state.doc.firstChild!.attrs.tableLayout
    expect(changed.columns).toEqual(initial.columns)
    expect(changed.rows['2']).toEqual([80, 320, 200])
  } finally { restore() }
})
it('does not hijack a text double-click, an outer border or a read-only table', () => {
  const { editor, table, initial, restore } = fittingFixture()
  try {
    expect(doubleClick(table.rows[1]!.cells[0]!, 100)).toBe(true)
    doubleClick(table.rows[1]!.cells[2]!, 600)
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(initial)
    editor.setEditable(false)
    doubleClick(table.rows[1]!.cells[0]!, 200)
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(initial)
  } finally { restore() }
})
it('keeps the first click selection through double-click jitter and later native selection changes', () => {
  const { editor, table, initial, restore } = fittingFixture([[40, 40, 40], [260, 140, 200], [80, 320, 200]])
  try {
    const positions: number[] = []; editor.state.doc.descendants((node, pos) => { if (node.type.name === 'tableCell') positions.push(pos) })
    editor.commands.setTextSelection({ from: positions[3]! + 2, to: positions[3]! + 7 })
    const cell = table.rows[2]!.cells[0]!
    pointer(cell, 'pointerdown', 210); pointer(document, 'pointerup', 211)
    pointer(editor.view.dom, 'lostpointercapture', 211)
    editor.commands.setTextSelection(3)
    pointer(cell, 'pointerdown', 210); pointer(document, 'pointerup', 210)
    doubleClick(cell, 210)
    const changed = editor.state.doc.firstChild!.attrs.tableLayout
    expect(changed.columns).toEqual(initial.columns)
    expect(changed.rows['2']).toEqual([80, 320, 200])
    editor.commands.undo()
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(initial)
  } finally { restore() }
})

it('fits a boundary when pointer capture retargets the double-click to the editor root', () => {
  const { editor, table, restore } = fittingFixture()
  try {
    const cell = table.rows[1]!.cells[0]!
    pointer(cell, 'pointerdown', 200); pointer(document, 'pointerup', 200)
    pointer(cell, 'pointerdown', 200); pointer(document, 'pointerup', 200)
    doubleClick(editor.view.dom, 200, 35)
    expect(editor.state.doc.firstChild!.attrs.tableLayout.columns).toEqual([260, 140, 200])
  } finally { restore() }
})

it('auto-fit does not push an already narrow independent row to the hard minimum', () => {
  const { editor, table, initial, restore } = fittingFixture([[80, 80, 80], [120, 360, 200], [120, 280, 200]])
  try {
    const skewed = { ...initial, rows: { '2': [80, 320, 200] } }
    editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs, tableLayout: skewed }))
    doubleClick(table.rows[1]!.cells[0]!, 200)
    const changed = editor.state.doc.firstChild!.attrs.tableLayout
    // Both rows have a 400px pair budget. Natural 120:360 content becomes
    // 104:296 above the 40px minimum, rather than applying -96 to the 80px row.
    expect(changed.columns).toEqual([104, 296, 200])
    expect(changed.rows['2']).toBeUndefined()
    editor.commands.undo()
    expect(editor.state.doc.firstChild!.attrs.tableLayout).toEqual(skewed)
  } finally { restore() }
})
it('auto-fit preserves another independent boundary and each row pair budget', () => {
  const { editor, table, initial, restore } = fittingFixture([[80, 80, 80], [120, 360, 200], [120, 280, 200]])
  try {
    editor.view.dispatch(editor.state.tr.setNodeMarkup(0, undefined, { ...editor.state.doc.firstChild!.attrs,
      tableLayout: { ...initial, rows: { '2': [80, 220, 300] } } }))
    doubleClick(table.rows[1]!.cells[0]!, 200)
    const changed = editor.state.doc.firstChild!.attrs.tableLayout
    expect(changed.columns).toEqual([104, 296, 200])
    expect(changed.rows['2']).toEqual([84, 216, 300])
    expect(changed.width).toBe(600)
  } finally { restore() }
})
