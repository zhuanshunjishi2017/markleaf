import { Extension, type Editor, type JSONContent } from '@tiptap/core'
import type { Node as PMNode } from '@tiptap/pm/model'
import { Plugin, PluginKey, type EditorState } from '@tiptap/pm/state'
import type { EditorView, NodeView } from '@tiptap/pm/view'
import { Mapping } from '@tiptap/pm/transform'
import { closeHistory } from '@tiptap/pm/history'
import { CellSelection } from '@tiptap/pm/tables'
import { Table, TableRow } from '@tiptap/extension-table'
import { tableMarkdownTokenizer } from './markdown-block-tokenizers'
import { TABLE_CELL_MIN, TABLE_LAYOUT_ATTRIBUTE, TABLE_LAYOUT_PREFIX, parseTableLayout, makeTableLayout, fitTableWidths, resizeTableBoundary, resizeTableWidth, serializeTableLayout, validTableShape, type TableLayout } from './table-layout'

export function tableShape(node: PMNode | JSONContent): number[] {
  if ('forEach' in node) {
    const shape: number[] = []; (node as PMNode).forEach(row => shape.push(row.childCount)); return shape
  }
  return (node as JSONContent).content?.map(row => row.content?.length ?? 0) ?? []
}
export function isResizableTable(node: PMNode): boolean {
  if (!validTableShape(tableShape(node))) return false
  let valid = true
  node.forEach(row => row.forEach(cell => { if (cell.attrs.colspan !== 1 || cell.attrs.rowspan !== 1) valid = false }))
  return valid
}
export function selectedTable(state: EditorState): { node: PMNode; pos: number } | null {
  const { $from } = state.selection
  for (let depth = $from.depth; depth > 0; depth--) if ($from.node(depth).type.name === 'table') return { node: $from.node(depth), pos: $from.before(depth) }
  const node = state.doc.nodeAt(state.selection.from)
  return node?.type.name === 'table' ? { node, pos: state.selection.from } : null
}
export const tableLayoutAttributes = {
  tableLayout: {
    default: null,
    parseHTML: (el: HTMLElement) => { try { return JSON.parse(el.getAttribute(TABLE_LAYOUT_ATTRIBUTE) ?? 'null') } catch { return null } },
    renderHTML: (attrs: Record<string, unknown>) => attrs.tableLayout ? { [TABLE_LAYOUT_ATTRIBUTE]: serializeTableLayout(attrs.tableLayout as TableLayout) } : {},
  },
  tableLayoutRaw: { default: null, rendered: false },
}
const commentPattern = /^<!-- markleaf:table-layout ([^\r\n]*?) -->[ \t]*\r?\n(?:[ \t]*\r?\n)?/
export const layoutTableTokenizer: typeof tableMarkdownTokenizer = {
  ...tableMarkdownTokenizer,
  start(src) { return src.startsWith(TABLE_LAYOUT_PREFIX) ? 0 : (tableMarkdownTokenizer.start as (src: string) => number)(src) ?? -1 },
  tokenize(src, tokens, helpers) {
    const comment = src.startsWith(TABLE_LAYOUT_PREFIX) ? commentPattern.exec(src) : null
    if (!comment) return tableMarkdownTokenizer.tokenize(src, tokens, helpers)
    const following = src.slice(comment[0].length)
    if (((tableMarkdownTokenizer.start as (src: string) => number)(following) ?? -1) !== 0) return undefined
    // The native tokenizer delegates ordinary tables to marked, so parse this
    // isolated remainder through that same lexer. The prefix has been removed.
    const parsed = helpers.blockTokens(following)[0]
    if (parsed?.type !== 'table' || !parsed.raw) return undefined
    return { ...parsed, raw: comment[0] + parsed.raw, markleafLayout: comment[1] }
  },
}
export function parseLayoutTable(token: any, helpers: any): JSONContent {
  const node = (Table.config.parseMarkdown as any)(token, helpers) as JSONContent
  const raw = typeof token.markleafLayout === 'string' ? token.markleafLayout : null
  const layout = raw ? parseTableLayout(raw, tableShape(node)) : null
  node.attrs = { ...node.attrs, tableLayout: layout, tableLayoutRaw: layout ? null : raw }
  return node
}
export function tableLayoutMarkdownPrefix(node: any): string {
  const layout = parseTableLayout(node.attrs?.tableLayout, tableShape(node))
  const raw = layout ? serializeTableLayout(layout) : node.attrs?.tableLayoutRaw
  return typeof raw === 'string' && !raw.includes('-->') && !/[\r\n]/.test(raw) ? `${TABLE_LAYOUT_PREFIX}${raw} -->\n` : ''
}

const tableLayoutPreferences = new WeakMap<EditorView, boolean>()
export function isTableLayoutEnabled(view: EditorView): boolean { return tableLayoutPreferences.get(view) !== false }
export function setTableLayoutEnabled(editor: Editor, enabled: boolean): void {
  if (editor.isDestroyed || isTableLayoutEnabled(editor.view) === enabled) return
  tableLayoutPreferences.set(editor.view, enabled)
  editor.view.dispatch(editor.state.tr.setMeta('addToHistory', false))
}
export function clearAllTableLayouts(editor: Editor): boolean {
  if (!editor.isEditable) return false
  const tr = closeHistory(editor.state.tr)
  editor.state.doc.descendants((node, pos) => {
    if (node.type.name === 'table' && (node.attrs.tableLayout !== null || node.attrs.tableLayoutRaw !== null)) {
      tr.setNodeMarkup(pos, undefined, { ...node.attrs, tableLayout: null, tableLayoutRaw: null })
    }
  })
  if (!tr.docChanged) return false
  editor.view.dispatch(tr)
  editor.view.dispatch(closeHistory(editor.state.tr).setMeta('addToHistory', false))
  return true
}
function removeTableLayoutProjection(table: HTMLTableElement): void {
  projectedTables.delete(table)
  if (!table.classList.contains('markleaf-table-manual')) return
  table.classList.remove('markleaf-table-manual')
  for (const key of ['role', 'aria-colcount', 'aria-rowcount']) table.removeAttribute(key)
  for (const key of ['display', 'width', 'min-width', 'max-width']) table.style.removeProperty(key)
  for (const group of table.querySelectorAll<HTMLElement>(':scope > tbody, :scope > thead, :scope > tfoot')) group.style.removeProperty('display')
  for (const row of table.rows) {
    row.style.removeProperty('display'); row.style.removeProperty('grid-template-columns'); row.removeAttribute('role')
  }
}

const projectedTables = new WeakMap<HTMLTableElement, { raw: string | null; exporting: boolean; cells: HTMLTableCellElement[] }>()

/** Shared by the live view, HTML clipboard and HTML/PDF exports. No doc writes. */
export function applyTableLayoutDOM(root: ParentNode, exporting = false, enabled = true): void {
  for (const table of root.querySelectorAll<HTMLTableElement>(`table[${TABLE_LAYOUT_ATTRIBUTE}]`)) {
    if (!enabled) { removeTableLayoutProjection(table); continue }
    const rows = Array.from(table.rows)
    const cells = rows.flatMap(row => Array.from(row.cells))
    const raw = table.getAttribute(TABLE_LAYOUT_ATTRIBUTE)
    const previous = projectedTables.get(table)
    if (previous?.raw === raw && previous.exporting === exporting && table.classList.contains('markleaf-table-manual')
      && previous.cells.length === cells.length && previous.cells.every((cell, i) => cell === cells[i])) continue
    if (rows.some(row => Array.from(row.cells).some(cell => cell.colSpan !== 1 || cell.rowSpan !== 1))) continue
    const layout = parseTableLayout(table.getAttribute(TABLE_LAYOUT_ATTRIBUTE), rows.map(row => row.cells.length))
    if (!layout) continue
    projectedTables.set(table, { raw, exporting, cells })
    table.classList.add('markleaf-table-manual')
    table.setAttribute('role', 'table')
    table.setAttribute('aria-colcount', String(layout.columns.length))
    table.setAttribute('aria-rowcount', String(rows.length))
    table.style.display = 'block'
    table.style.width = exporting ? `min(100%, ${layout.width}px)` : `min(100%, calc(${layout.width}px * var(--ml-table-scale, 1)))`
    const narrowest = Math.min(...layout.columns, ...Object.values(layout.rows).flat())
    table.style.minWidth = exporting ? '0' : `calc(${layout.width * TABLE_CELL_MIN / narrowest}px * var(--ml-table-scale, 1))`
    table.style.maxWidth = 'none'
    for (const group of table.querySelectorAll('colgroup')) group.remove()
    for (const group of table.querySelectorAll<HTMLElement>(':scope > tbody, :scope > thead, :scope > tfoot')) group.style.display = 'block'
    rows.forEach((row, i) => {
      row.setAttribute('role', 'row')
      // Native cells retain their intrinsic roles. Mutating descendant cell
      // attributes would be parsed by their own PM view, bypassing the table view.
      if (exporting) Array.from(row.cells).forEach((cell, index) => { cell.setAttribute('role', cell.tagName === 'TH' ? 'columnheader' : 'cell'); cell.setAttribute('aria-colindex', String(index + 1)) })
      row.style.display = 'grid'
      row.style.gridTemplateColumns = (layout.rows[String(i)] ?? layout.columns).map(n => `minmax(0, ${n}fr)`).join(' ')
    })
  }
}

/** Row-owned projection attributes must be ignored by the nearest node view. */
export const LayoutTableRow = TableRow.extend({
  addNodeView() {
    return ({ node, view }) => {
      const dom = view.dom.ownerDocument.createElement('tr')
      return {
        dom, contentDOM: dom,
        update: next => next.type === node.type,
        ignoreMutation(mutation) {
          return mutation.type === 'attributes' && mutation.target === dom && ['style', 'role'].includes(mutation.attributeName ?? '')
            || mutation.type === 'childList' && mutation.target === dom && [...mutation.addedNodes, ...mutation.removedNodes].every(n => n instanceof Element && n.classList.contains('markleaf-table-resize-guide'))
            || mutation.target instanceof Element && mutation.target.classList.contains('markleaf-table-resize-guide')
        },
      }
    }
  },
})

const tableViews = new WeakMap<HTMLTableElement, { getPos: () => number | undefined }>()
export function createTableLayoutView(node: PMNode, view: EditorView, getPos: () => number | undefined): NodeView {
  const owner = view.dom.ownerDocument
  const dom = owner.createElement('div'); dom.className = 'markleaf-table-wrapper'
  const caption = owner.createElement('figcaption'); caption.className = 'markleaf-figcaption'; caption.contentEditable = 'false'
  const table = owner.createElement('table'); const tbody = owner.createElement('tbody'); table.append(tbody)
  dom.append(caption, table); tableViews.set(table, { getPos })
  let current = node
  const update = (next: PMNode) => {
    if (next.type !== current.type) return false
    current = next
    const title = typeof next.attrs.caption === 'string' ? next.attrs.caption : ''
    caption.textContent = title; caption.hidden = !title
    const layout = isResizableTable(next) ? parseTableLayout(next.attrs.tableLayout, tableShape(next)) : null
    if (layout) table.setAttribute(TABLE_LAYOUT_ATTRIBUTE, serializeTableLayout(layout))
    else {
      projectedTables.delete(table)
      table.removeAttribute(TABLE_LAYOUT_ATTRIBUTE); table.classList.remove('markleaf-table-manual'); table.removeAttribute('style')
      tbody.removeAttribute('style')
      for (const row of table.rows) row.removeAttribute('style')
    }
    return true
  }
  update(node)
  return { dom, contentDOM: tbody, update,
    ignoreMutation(mutation) {
      return mutation.type !== 'selection' && (mutation.target === dom || mutation.target === caption || caption.contains(mutation.target)
        || mutation.type === 'attributes' && (mutation.target === table || mutation.target === tbody || mutation.target.nodeName === 'TR')
        || mutation.type === 'attributes' && ['TD', 'TH'].includes(mutation.target.nodeName) && ['role', 'aria-colindex'].includes(mutation.attributeName ?? '')
        || mutation.type === 'childList' && mutation.target.nodeName === 'TR' && [...mutation.addedNodes, ...mutation.removedNodes].every(n => n instanceof Element && n.classList.contains('markleaf-table-resize-guide'))
        || mutation.target instanceof Element && mutation.target.classList.contains('markleaf-table-resize-guide'))
    },
    destroy() { tableViews.delete(table) },
  }
}

function displayScale(view: EditorView): number {
  const scale = Number.parseFloat(getComputedStyle(view.dom).getPropertyValue('--ml-table-scale'))
  return Number.isFinite(scale) && scale > 0 ? scale : 1
}
function baseLayout(view: EditorView, pos: number, node: PMNode): TableLayout {
  const stored = parseTableLayout(node.attrs.tableLayout, tableShape(node))
  if (stored) return stored
  const wrapper = view.nodeDOM(pos) as HTMLElement | null
  const table = wrapper?.querySelector('table')
  const cells = table?.rows[0]?.cells
  const scale = displayScale(view)
  const widths = cells ? Array.from(cells, c => (c.offsetWidth || c.getBoundingClientRect().width) / scale) : []
  const width = (table?.offsetWidth ?? 0) / scale || widths.reduce((a, b) => a + b, 0) || Math.min(820, tableShape(node)[0]! * 160)
  return makeTableLayout(tableShape(node), width, widths.length === node.firstChild?.childCount ? widths : undefined)
}
export function commitTableLayout(view: EditorView, pos: number, layout: TableLayout | null): boolean {
  const node = view.state.doc.nodeAt(pos)
  if (!view.editable || !node || !isResizableTable(node) || layout && !parseTableLayout(layout, tableShape(node))) return false
  const tr = closeHistory(view.state.tr).setNodeMarkup(pos, undefined, { ...node.attrs, tableLayout: layout, tableLayoutRaw: null })
  view.dispatch(tr)
  // Isolate each gesture/command from the next text or layout operation.
  view.dispatch(closeHistory(view.state.tr).setMeta('addToHistory', false))
  return true
}
/** Measure each cell independently so a selected row does not inherit other rows' maxima. */
function measureTableContents(view: EditorView, pos: number, node: PMNode): { cells: number[][]; available: number } {
  const wrapper = view.nodeDOM(pos) as HTMLElement
  const table = wrapper.querySelector('table')!
  const clone = table.cloneNode(true) as HTMLTableElement
  const measure = document.createElement('div'); measure.className = 'markleaf-document'
  const font = getComputedStyle(view.dom)
  const scale = displayScale(view)
  const baselineFontSize = `${(Number.parseFloat(font.fontSize) || 16) / scale}px`
  Object.assign(measure.style, { position: 'fixed', left: '-100000px', top: '0', visibility: 'hidden', pointerEvents: 'none', width: 'max-content', maxWidth: 'none', fontFamily: font.fontFamily, fontSize: baselineFontSize, lineHeight: Number.isFinite(Number.parseFloat(font.lineHeight)) ? `${Number.parseFloat(font.lineHeight) / scale}px` : font.lineHeight })
  // Measure in logical typography units: images, padding and borders already
  // use fixed pixels, so dividing the entire measured cell would shrink them.
  measure.style.setProperty('--ml-font-size', baselineFontSize)
  measure.style.setProperty('--ml-table-scale', '1')
  clone.removeAttribute(TABLE_LAYOUT_ATTRIBUTE); clone.removeAttribute('style'); clone.classList.remove('markleaf-table-manual')
  Object.assign(clone.style, { display: 'block', width: 'max-content', maxWidth: 'none', tableLayout: 'auto' })
  for (const el of clone.querySelectorAll<HTMLElement>('tbody,thead,tfoot,tr,th,td')) el.removeAttribute('style')
  for (const group of clone.querySelectorAll<HTMLElement>('tbody,thead,tfoot')) group.style.display = 'block'
  for (const row of clone.rows) row.style.display = 'flex'
  for (const cell of clone.querySelectorAll<HTMLElement>('th,td')) Object.assign(cell.style, { display: 'block', flex: '0 0 auto', whiteSpace: 'pre', width: 'max-content', maxWidth: 'none' })
  const originalImages = table.querySelectorAll('img')
  const imageNodes: PMNode[] = []
  node.descendants(node => { if (node.type.name === 'image') imageNodes.push(node) })
  clone.querySelectorAll('img').forEach((img, i) => {
    const imageScale = typeof imageNodes[i]?.attrs.widthPercent === 'number' ? scale : 1
    const w = (originalImages[i]?.offsetWidth ?? 0) / imageScale
    if (w) img.style.width = `${w}px`
    if (imageScale !== 1) {
      const frame = img.closest<HTMLElement>('.markleaf-image-frame')
      for (const el of [img, frame]) if (el) for (const property of ['width', 'height'] as const) {
        if (el === img && property === 'width') continue
        const value = Number.parseFloat(el.style[property])
        if (Number.isFinite(value)) el.style[property] = `${value / imageScale}px`
      }
    }
  })
  measure.append(clone); document.body.append(measure)
  try {
    const cells = Array.from(clone.rows, row => Array.from(row.cells, cell => Math.max(TABLE_CELL_MIN, cell.offsetWidth || TABLE_CELL_MIN)))
    const available = Math.max(TABLE_CELL_MIN * node.firstChild!.childCount, (wrapper.parentElement?.clientWidth ?? 0) / scale || 820)
    return { cells, available }
  } finally { measure.remove() }
}

export function tableLayoutCommand(editor: Editor, command: string): boolean {
  const target = selectedTable(editor.state)
  if (!target || !editor.isEditable || !isTableLayoutEnabled(editor.view) || !isResizableTable(target.node)) return false
  if (command === 'resetTableWidths') return commitTableLayout(editor.view, target.pos, null)
  const base = baseLayout(editor.view, target.pos, target.node)
  if (command === 'distributeTableColumns') return commitTableLayout(editor.view, target.pos, makeTableLayout(base.shape, base.width))
  if (command !== 'fitTableContents') return false
  const measurement = measureTableContents(editor.view, target.pos, target.node)
  const desired = base.columns.map((_value, i) => Math.max(TABLE_CELL_MIN, ...measurement.cells.map(row => row[i] ?? TABLE_CELL_MIN)))
  const width = Math.min(measurement.available, desired.reduce((a, b) => a + b, 0))
  return commitTableLayout(editor.view, target.pos, makeTableLayout(base.shape, width, desired))
}

function fitTableBoundary(view: EditorView, pos: number, node: PMNode, boundary: number, row?: number): void {
  const base = baseLayout(view, pos, node)
  const measurement = measureTableContents(view, pos, node)
  const content = row === undefined ? base.columns.map((_value, i) => Math.max(TABLE_CELL_MIN, ...measurement.cells.map(cells => cells[i] ?? TABLE_CELL_MIN)))
    : measurement.cells[row]!
  // Auto-fit allocates each affected pair directly. A drag's shared delta can
  // crush an offset row to the hard minimum even though its content can fit.
  const fitPair = (widths: number[]) => {
    const pair = fitTableWidths([content[boundary]!, content[boundary + 1]!], widths[boundary]! + widths[boundary + 1]!)
    return widths.map((width, i) => i === boundary ? pair[0]! : i === boundary + 1 ? pair[1]! : width)
  }
  const columns = row === undefined ? fitPair(base.columns) : [...base.columns]
  const rows = Object.fromEntries(Object.entries(base.rows).map(([key, widths]) => [key, row === undefined ? fitPair(widths) : [...widths]]))
  if (row !== undefined) rows[String(row)] = fitPair(base.rows[String(row)] ?? base.columns)
  for (const key of Object.keys(rows)) if (rows[key]!.every((width, i) => Math.abs(width - columns[i]!) < .0001)) delete rows[key]
  const result = { ...base, columns, rows }
  if (serializeTableLayout(result) !== serializeTableLayout(base)) commitTableLayout(view, pos, result)
}

/** Structural edits from ANY entry point use mapped old cell identities. */
function reconcileLayout(old: PMNode, next: PMNode, oldPos: number, nextPos: number, mapping: Mapping): TableLayout | null {
  const layout = parseTableLayout(old.attrs.tableLayout, tableShape(old))
  const shape = tableShape(next)
  if (!layout || !isResizableTable(next)) return null
  const destinations = new Map<number, { row: number; col: number }>()
  next.forEach((row, ro, ri) => row.forEach((_cell, co, ci) => destinations.set(nextPos + ro + co + 2, { row: ri, col: ci })))
  const rowSources = new Map<number, number>(), colSources = new Map<number, number>()
  old.forEach((row, ro, ri) => row.forEach((_cell, co, ci) => {
    const mapped = mapping.mapResult(oldPos + ro + co + 3, 1)
    if (mapped.deleted) return
    const dest = destinations.get(mapped.pos - 1)
    if (dest) { rowSources.set(dest.row, ri); colSources.set(dest.col, ci) }
  }))
  const width = Math.max(layout.width, shape[0]! * TABLE_CELL_MIN)
  const columns = fitTableWidths(Array.from({ length: shape[0]! }, (_, i) => layout.columns[colSources.get(i) ?? -1] ?? TABLE_CELL_MIN * 2), width)
  const result: TableLayout = { version: 1, shape, width, columns, rows: {} }
  for (const [row, oldRow] of rowSources) {
    const previous = layout.rows[String(oldRow)]
    if (previous) result.rows[String(row)] = fitTableWidths(columns.map((_v, i) => previous[colSources.get(i) ?? -1] ?? TABLE_CELL_MIN * 2), width)
  }
  return result
}

/** A full content range is a local resize intent; a caret is never one. */
function fullySelectedCell(view: EditorView): number | null {
  const selection = view.state.selection
  if (selection instanceof CellSelection) return selection.$anchorCell.pos === selection.$headCell.pos ? selection.$anchorCell.pos : null
  const coveredCell = (from: number, to: number): number | null => {
    if (from === to) return null
    const $from = view.state.doc.resolve(from)
    for (let depth = $from.depth; depth > 0; depth--) {
      const cell = $from.node(depth)
      if (!['tableCell', 'tableHeader'].includes(cell.type.name)) continue
      const pos = $from.before(depth), start = pos + 1
      if (to > pos + cell.nodeSize - 1) return null
      let first = Infinity, last = -1
      cell.descendants((node, offset) => {
        if (!node.isText && !node.isAtom) return
        first = Math.min(first, start + offset)
        last = Math.max(last, start + offset + node.nodeSize)
        return false
      })
      return from <= first && to >= last && last >= 0 ? pos : null
    }
    return null
  }
  // Read the native range before pointerdown collapses it. WebKit may not yet
  // have published its final text selection into ProseMirror at this point.
  const native = view.dom.ownerDocument.getSelection()
  if (native && !native.isCollapsed && native.anchorNode && native.focusNode
    && view.dom.contains(native.anchorNode) && view.dom.contains(native.focusNode)) {
    try {
      const anchor = view.posAtDOM(native.anchorNode, native.anchorOffset)
      const focus = view.posAtDOM(native.focusNode, native.focusOffset)
      return coveredCell(Math.min(anchor, focus), Math.max(anchor, focus))
    } catch { /* A detached native endpoint cannot describe the current document. */ }
  }
  return coveredCell(selection.from, selection.to)
}

const layoutKey = new PluginKey('markleaf-table-layout')
type Drag = { table: HTMLTableElement; pos: number; startX: number; layout: TableLayout; boundary: number; row?: number; pointer: number; nextX: number; frame: number; startRenderedWidth: number; maxWidth: number; previewed: boolean; hitRow: number }
export const TableLayoutInteraction = Extension.create({
  name: 'markleafTableLayout',
  addProseMirrorPlugins() {
    return [new Plugin({
      key: layoutKey,
      appendTransaction(transactions, before, after) {
        if (!transactions.some(t => t.docChanged) || transactions.some(t => t.getMeta(layoutKey))) return null
        const mapping = new Mapping(); transactions.forEach(tr => mapping.appendMapping(tr.mapping))
        const tr = after.tr
        before.doc.descendants((old, pos) => {
          if (old.type.name !== 'table' || !old.attrs.tableLayout) return
          const nextPos = mapping.map(pos, 1), next = after.doc.nodeAt(nextPos)
          if (!next || next.type.name !== 'table' || !next.attrs.tableLayout || !isResizableTable(next)) return
          if (JSON.stringify(tableShape(old)) === JSON.stringify(tableShape(next)) || parseTableLayout(next.attrs.tableLayout, tableShape(next))) return
          const layout = reconcileLayout(old, next, pos, nextPos, mapping)
          if (layout) tr.setNodeMarkup(nextPos, undefined, { ...next.attrs, tableLayout: layout, tableLayoutRaw: null })
        })
        return tr.docChanged ? tr.setMeta(layoutKey, true) : null
      },
      view(view) {
        let drag: Drag | null = null
        let boundaryClick: { table: HTMLTableElement; pos: number; boundary: number; row?: number; hitRow: number; time: number } | null = null
        const root = view.dom
        const owner = root.ownerDocument
        const window = owner.defaultView!
        const movedEnough = (d: Drag) => Math.abs(d.nextX - d.startX) > 3 * d.startRenderedWidth / d.layout.width
        const clearGuides = () => root.querySelectorAll('.markleaf-table-resize-guide').forEach(el => el.remove())
        const paint = (d: Drag, x: number) => {
          if (!d.startRenderedWidth) return d.layout
          d.previewed = true
          const delta = (x - d.startX) * d.layout.width / d.startRenderedWidth
          const outer = d.boundary === d.layout.columns.length - 1
          const layout = outer
            ? resizeTableWidth(d.layout, Math.min(delta, d.maxWidth - d.layout.width))
            : resizeTableBoundary(d.layout, d.boundary, delta, d.row)
          d.table.setAttribute(TABLE_LAYOUT_ATTRIBUTE, serializeTableLayout(layout)); applyTableLayoutDOM(root, false, isTableLayoutEnabled(view))
          clearGuides()
          Array.from(d.table.rows).forEach((row, ri) => {
            if (d.row !== undefined && ri !== d.row) return
            const guide = owner.createElement('span'); guide.className = 'markleaf-table-resize-guide'; guide.contentEditable = 'false'
            const widths = layout.rows[String(ri)] ?? layout.columns
            guide.style.left = outer ? 'calc(100% - 1px)' : `${100 * widths.slice(0, d.boundary + 1).reduce((a, b) => a + b, 0) / layout.width}%`
            row.append(guide)
          })
          return layout
        }
        const finish = (commit = false) => {
          const current = drag; if (!current) return
          if (current.frame) window.cancelAnimationFrame(current.frame)
          const moved = movedEnough(current)
          let result = commit && moved ? paint(current, current.nextX) : null
          if (result && serializeTableLayout(result) === serializeTableLayout(current.layout)) result = null
          drag = null; clearGuides(); root.classList.remove('markleaf-table-resizing'); root.style.removeProperty('cursor')
          try { root.releasePointerCapture(current.pointer) } catch { /* capture already released */ }
          if (commit && result) commitTableLayout(view, current.pos, result)
          else {
            const node = view.state.doc.nodeAt(current.pos)
            const saved = node && parseTableLayout(node.attrs.tableLayout, tableShape(node))
            if (saved) current.table.setAttribute(TABLE_LAYOUT_ATTRIBUTE, serializeTableLayout(saved))
            else {
              current.table.removeAttribute(TABLE_LAYOUT_ATTRIBUTE); current.table.classList.remove('markleaf-table-manual'); current.table.removeAttribute('style')
              for (const el of current.table.querySelectorAll<HTMLElement>('tbody,tr')) el.removeAttribute('style')
            }
            applyTableLayoutDOM(root, false, isTableLayoutEnabled(view))
          }
        }
        const hit = (event: MouseEvent, capturedCell?: HTMLTableCellElement) => {
          const element = event.target instanceof Element ? event.target : null
          let cell = capturedCell ?? element?.closest<HTMLTableCellElement>('td,th') ?? null
          const table = capturedCell?.closest('table') ?? element?.closest('table') ?? element?.closest('.markleaf-table-wrapper')?.querySelector('table')
          const entry = table && tableViews.get(table)
          const pos = entry?.getPos(), node = pos === undefined ? null : view.state.doc.nodeAt(pos)
          if (!table || pos === undefined || !node || !isResizableTable(node) || !view.editable || !isTableLayoutEnabled(view)) return null
          const zoom = table.getBoundingClientRect().width / (table.offsetWidth || table.getBoundingClientRect().width || 1)
          if (!cell || cell.closest('table') !== table) {
            if (Math.abs(event.clientX - table.getBoundingClientRect().right) > 5 * zoom * displayScale(view)) return null
            const row = Array.from(table.rows).find(row => {
              const bounds = row.getBoundingClientRect()
              return event.clientY >= bounds.top && event.clientY <= bounds.bottom
            })
            cell = row?.cells.item(row.cells.length - 1) ?? null
          }
          if (!cell) return null
          const rect = cell.getBoundingClientRect(), column = cell.cellIndex
          const left = column > 0 && Math.abs(event.clientX - rect.left) <= 5 * zoom * displayScale(view)
          const right = Math.abs(event.clientX - rect.right) <= 5 * zoom * displayScale(view)
          if (!left && !right) return null
          const row = (cell.parentElement as HTMLTableRowElement).rowIndex
          const boundary = left ? column - 1 : column
          return { table, pos, node, row, boundary }
        }
        const down = (event: PointerEvent) => {
          if (event.button !== 0) { boundaryClick = null; return }
          if (drag) return
          const target = hit(event); if (!target) { boundaryClick = null; return }
          if (boundaryClick && (boundaryClick.table !== target.table || boundaryClick.boundary !== target.boundary || Date.now() - boundaryClick.time > 1000)) boundaryClick = null
          event.preventDefault(); event.stopImmediatePropagation()
          const outer = target.boundary === target.node.firstChild!.childCount - 1
          const cellPos = outer ? null : fullySelectedCell(view)
          let local: number | undefined
          if (cellPos !== null) {
            const selected = view.nodeDOM(cellPos) as HTMLTableCellElement | null
            const selectedRow = selected?.parentElement as HTMLTableRowElement | null
            if (selectedRow?.rowIndex === target.row && selected?.closest('table') === target.table
              && (selected.cellIndex === target.boundary || selected.cellIndex === target.boundary + 1)) local = target.row
          }
          let layout = baseLayout(view, target.pos, target.node)
          const scale = displayScale(view)
          const rect = target.table.getBoundingClientRect()
          const parent = target.table.closest('.markleaf-table-wrapper')?.parentElement
          const style = parent && getComputedStyle(parent)
          const available = parent ? (parent.clientWidth - (Number.parseFloat(style!.paddingLeft) || 0) - (Number.parseFloat(style!.paddingRight) || 0)) / scale : 0
          if (outer) {
            // A saved table may currently be compressed by its container. Start
            // the edge gesture at the visible width, not an off-screen preference.
            const renderedWidth = (target.table.offsetWidth || rect.width) / scale
            const ratio = renderedWidth / layout.width
            const widths = (values: number[]) => fitTableWidths(values.map(n => n * ratio), renderedWidth)
            layout = { ...layout, width: Math.max(layout.columns.length * TABLE_CELL_MIN, renderedWidth), columns: widths(layout.columns),
              rows: Object.fromEntries(Object.entries(layout.rows).map(([row, values]) => [row, widths(values)])) }
          }
          drag = { ...target, startX: event.clientX, nextX: event.clientX, layout, row: local, pointer: event.pointerId, frame: 0,
            startRenderedWidth: rect.width, previewed: false, hitRow: target.row, maxWidth: available > 0 ? Math.max(available, layout.width) : 100_000 }
          root.classList.add('markleaf-table-resizing')
          try { root.setPointerCapture(event.pointerId) } catch { /* document listeners still complete the drag */ }
        }
        const move = (event: PointerEvent) => {
          if (drag && event.pointerId === drag.pointer) {
            event.preventDefault(); drag.nextX = event.clientX
            if (!drag.previewed && !movedEnough(drag)) return
            if (!drag.frame) drag.frame = window.requestAnimationFrame(() => { if (drag) { drag.frame = 0; paint(drag, drag.nextX) } })
          } else if (!drag) root.style.cursor = hit(event) ? 'col-resize' : ''
        }
        const up = (event: PointerEvent) => {
          if (!drag || event.pointerId !== drag.pointer) return
          drag.nextX = event.clientX; event.preventDefault()
          if (!drag.previewed && !movedEnough(drag) && drag.boundary < drag.layout.columns.length - 1) {
            // Keep the first click's intent if native word-selection runs during
            // the second click. A real drag is never part of a fit gesture.
            if (!boundaryClick) boundaryClick = { table: drag.table, pos: drag.pos, boundary: drag.boundary, row: drag.row, hitRow: drag.hitRow, time: Date.now() }
          } else boundaryClick = null
          finish(true)
        }
        const cancel = () => { boundaryClick = null; finish() }
        const lostCapture = () => { if (drag) cancel() }
        const doubleClick = (event: MouseEvent) => {
          if (event.button !== 0) return
          const intent = boundaryClick
          let target = hit(event)
          if (!target && intent && Date.now() - intent.time <= 1000) {
            // Capturing the pointer on the editor can make the browser send
            // click/dblclick to that root. Validate the recorded cell geometry
            // rather than requiring the eventual event target to remain a TD.
            const cell = intent.table.rows[intent.hitRow]?.cells[intent.boundary]
            if (cell && root.contains(cell)) {
              const rect = cell.getBoundingClientRect()
              const scale = intent.table.getBoundingClientRect().width / (intent.table.offsetWidth || 1) * displayScale(view)
              if (Math.abs(event.clientX - rect.right) <= 5 * scale && event.clientY >= rect.top && event.clientY <= rect.bottom) target = hit(event, cell)
            }
          }
          if (!target || target.boundary >= target.node.firstChild!.childCount - 1) return
          event.preventDefault(); event.stopImmediatePropagation()
          const preserved = intent && intent.table === target.table && intent.pos === target.pos && intent.boundary === target.boundary && (intent.row === undefined || intent.row === target.row) && Date.now() - intent.time <= 1000
          let local = preserved ? intent.row : undefined
          if (!preserved) {
            const cellPos = fullySelectedCell(view)
            const selected = cellPos === null ? null : view.nodeDOM(cellPos) as HTMLTableCellElement | null
            if (selected?.closest('table') === target.table && (selected.parentElement as HTMLTableRowElement).rowIndex === target.row
              && (selected.cellIndex === target.boundary || selected.cellIndex === target.boundary + 1)) local = target.row
          }
          boundaryClick = null; finish()
          fitTableBoundary(view, target.pos, target.node, target.boundary, local)
        }
        const key = (event: KeyboardEvent) => { boundaryClick = null; if (drag && event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); finish() } }
        const mouse = (event: MouseEvent) => { if (drag) { event.preventDefault(); event.stopImmediatePropagation() } }
        root.addEventListener('pointerdown', down, true); root.addEventListener('mousedown', mouse, true); root.addEventListener('dblclick', doubleClick, true)
        owner.addEventListener('pointermove', move, true); owner.addEventListener('pointerup', up, true); owner.addEventListener('pointercancel', cancel, true)
        owner.addEventListener('keydown', key, true); window.addEventListener('blur', cancel); root.addEventListener('lostpointercapture', lostCapture)
        applyTableLayoutDOM(root, false, isTableLayoutEnabled(view))
        return {
          update(updated, previous) { if (!updated.state.doc.eq(previous.doc) || !updated.editable || !isTableLayoutEnabled(updated)) { boundaryClick = null; if (drag) finish() }; applyTableLayoutDOM(root, false, isTableLayoutEnabled(view)) },
          destroy() {
            finish(); root.removeEventListener('pointerdown', down, true); root.removeEventListener('mousedown', mouse, true); root.removeEventListener('dblclick', doubleClick, true)
            owner.removeEventListener('pointermove', move, true); owner.removeEventListener('pointerup', up, true); owner.removeEventListener('pointercancel', cancel, true)
            owner.removeEventListener('keydown', key, true); window.removeEventListener('blur', cancel); root.removeEventListener('lostpointercapture', lostCapture)
          },
        }
      },
    })]
  },
})
