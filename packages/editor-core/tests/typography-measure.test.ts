import { afterEach, describe, expect, it, vi } from 'vitest'
import { createEditor } from '../src/index'
import { extractProjectionSnapshots } from '../src/typography/projection-snapshot'
import { createTypographyMeasurementService } from '../src/typography/projection-measure'
import { buildTypographyInlineDom, typographyInlinePieces } from '../src/typography/projection-dom'
import { candidateGapSlots, type ParagraphSnapshot } from '@markleaf/typography'

export const sample: ParagraphSnapshot = {
  id: 'p', revision: 0, environmentKey: 'font0', text: 'AV', width: 100,
  graphemeOffsets: [0, 1, 2], language: 'en', direction: 'ltr', alignment: 'default',
  runs: [{ from: 0, to: 2, styleKey: 's', css: { 'font-size': '16px', 'font-kerning': 'normal', 'font-feature-settings': 'normal' } }],
}
afterEach(() => { vi.restoreAllMocks(); document.body.innerHTML = '' })
describe('batch typography measurement', () => {
  it('reusesIdenticalShapingAcrossOffsetsAndWithinOneBatch', () => {
    const reads = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 18 } as DOMRect)
    const service = createTypographyMeasurementService(document), batches: unknown[] = []
    service.onBatch = stats => batches.push(stats)
    const s = { ...sample, text: 'AV AV AV', runs: [{ ...sample.runs[0]!, to: 8 }] }
    const lines = [0, 3].map(from => ({ from, to: from + 2, contentTo: from + 2, key: `av:${from}` }))
    expect(service.measureBatch(s, lines, []).map(m => m.naturalWidth)).toEqual([18, 18])
    expect(service.measureBatch(s, [{ from: 6, to: 8, contentTo: 8, key: 'av:6' }], [])[0]?.naturalWidth).toBe(18)
    expect(reads).toHaveBeenCalledTimes(1)
    expect(batches).toEqual([{ requested: 2, cacheHits: 1, domMeasured: 1 }, { requested: 1, cacheHits: 1, domMeasured: 0 }])
    service.dispose()
  })
  it('reusesNaturalMeasurementsAfterResizingButInvalidatesChangedShaping', () => {
    let reads = 0
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      if (this.parentElement?.hasAttribute('data-typography-measure')) reads++
      return { width: 80 } as DOMRect
    })
    const mount = document.createElement('div'); document.body.append(mount)
    const editor = createEditor(mount, '中 English', false), service = createTypographyMeasurementService(document)
    try {
      const p = editor.view.dom.querySelector('p')!
      p.style.fontSize = '16px'
      let width = 100
      p.getBoundingClientRect = () => ({ width } as DOMRect)
      const measure = (epoch = 0) => {
        const entry = extractProjectionSnapshots(editor.view, 0, epoch)[0]!
        if (!entry.eligible) throw Error('missing snapshot')
        const s = entry.snapshot
        service.measureBatch(s, [{ from: 0, to: s.text.length, contentTo: s.text.length, key: 'whole' }], candidateGapSlots(s, new Map([[1, 4]])))
      }
      measure(); expect(reads).toBe(1)
      width = 90; measure(); expect(reads).toBe(1)
      p.style.fontSize = '20px'; measure(); expect(reads).toBe(2)
      measure(1); expect(reads).toBe(3)
      editor.commands.setContent('中 Another'); measure(1); expect(reads).toBe(4)
    } finally { service.dispose(); editor.destroy() }
  })
  it('neverSharesShapeWidthsAcrossDifferentBoundarySpacing', () => {
    const reads = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return { width: 100 + [...this.querySelectorAll('span')].reduce((n, el) => n + (parseFloat(el.style.getPropertyValue('padding-inline-end')) || 0), 0) } as DOMRect
    })
    const service = createTypographyMeasurementService(document)
    const s = { ...sample, text: '中A中A', runs: [{ ...sample.runs[0]!, to: 4 }] }
    const slots = candidateGapSlots(s, new Map()).map(slot => slot.at === 3 ? { ...slot, natural: 3 } : slot)
    expect(service.measureBatch(s, [0, 2].map(from => ({ from, to: from + 2, contentTo: from + 2, key: `mixed:${from}` })), slots).map(m => m.naturalWidth)).toEqual([102, 103])
    expect(reads).toHaveBeenCalledTimes(2)
    service.dispose()
  })

  it('omitsMixedGapsAtBothCutEdgesFromTheMeasuredDom', () => {
    const text = 'English中English'
    const s = { ...sample, text, runs: [{ ...sample.runs[0]!, to: text.length }] }
    const slots = candidateGapSlots(s, new Map())
    const cut = { from: 7, to: 8, contentTo: 8, key: '7:8:8' }
    const measured = buildTypographyInlineDom(document, s, cut, slots, [])
    const projected = buildTypographyInlineDom(document, s, cut, [], [])
    const parent = document.createElement('span'); parent.append(measured)
    const expected = document.createElement('span'); expected.append(projected)
    expect(parent.innerHTML).toBe(expected.innerHTML)
    expect(parent.textContent).toBe('中')
    const whole = buildTypographyInlineDom(document, s, { from: 0, to: text.length, contentTo: text.length, key: 'whole' }, slots, [])
    const owned = [...whole.querySelectorAll('span')].find(n => n.textContent === '中') as HTMLElement
    expect(owned.style.getPropertyValue('padding-inline-start')).toBe('2px')
    expect(owned.style.getPropertyValue('padding-inline-end')).toBe('2px')
  })

  it('visitsBoundaryOwnersLinearlyWhenBuildingALongCjkLine', () => {
    const text = '中'.repeat(129), s = { ...sample, text, runs: [{ ...sample.runs[0]!, to: text.length }] }
    let ownerReads = 0
    const slots = candidateGapSlots(s, new Map()).map(slot => ({ ...slot, get owner() { ownerReads++; return slot.owner } }))
    const pieces = typographyInlinePieces(s, { from: 0, to: text.length, contentTo: text.length, key: 'long' }, slots, [])
    expect(pieces).toHaveLength(129)
    expect(pieces[0]!.ids).toEqual([])
    expect(pieces.at(-1)!.ids).toEqual(['cjk:128'])
    expect(ownerReads).toBeLessThan(128 * 12)
  })
  it('sharesInheritedRunStylesWithoutChangingPerBoundarySpacing', () => {
    const css = { 'font-size': '16px', 'font-kerning': 'normal', 'font-feature-settings': 'normal' }
    const s = { ...sample, text: '中文中文', runs: [{ from: 0, to: 4, styleKey: 's', css }] }
    const slots = [1, 2, 3].map(at => ({ id: `cjk:${at}`, at, kind: 'cjk' as const, owner: { from: at, to: at + 1 }, edge: 'start' as const, natural: 0, min: 0, max: 1.28, weight: 1, leftStyleKey: 's', rightStyleKey: 's' }))
    const dom = buildTypographyInlineDom(document, s, { from: 0, to: 4, contentTo: 4, key: 'one' }, slots, [])
    expect(dom.textContent).toBe('中文中文')
    expect([...dom.querySelectorAll('span')].filter(el => el.style.fontSize).map(el => el.style.fontSize)).toEqual(['16px'])
    expect([...dom.querySelectorAll('span')].filter(el => el.style.getPropertyValue('padding-inline-start') === '0px')).toHaveLength(3)
  })
  it('reportsCacheHitsAndDoesNotReuseAnotherEnvironment', () => {
    let reads = 0
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => ({ width: ++reads * 10 } as DOMRect))
    const service = createTypographyMeasurementService(document), batches: unknown[] = []
    service.onBatch = stats => batches.push(stats)
    const lines = [{ from: 0, to: 2, contentTo: 2, key: 'av' }]
    expect(service.measureBatch(sample, lines, [])[0]?.naturalWidth).toBe(10)
    expect(service.measureBatch(sample, lines, [])[0]?.naturalWidth).toBe(10)
    expect(service.measureBatch({ ...sample, environmentKey: 'font1' }, lines, [])[0]?.naturalWidth).toBe(20)
    expect(batches).toEqual([{ requested: 1, cacheHits: 0, domMeasured: 1 }, { requested: 1, cacheHits: 1, domMeasured: 0 }, { requested: 1, cacheHits: 0, domMeasured: 1 }])
    service.dispose()
  })
  it('cleansMeasurementChildrenWhenLayoutThrows', () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(() => { throw Error('layout') })
    const service = createTypographyMeasurementService(document)
    expect(() => service.measureBatch(sample, [{ key: 'av', from: 0, to: 2, contentTo: 2 }], [])).toThrow('layout')
    expect(document.querySelector('[data-typography-measure]')?.children.length).toBe(0)
    service.dispose()
  })
  it('measuresSameStructureAndStyles', () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return { width: this.textContent === 'AV' ? 18 : 10 } as DOMRect
    })
    const service = createTypographyMeasurementService(document)
    const result = service.measureBatch(sample, [{ from: 0, to: 2, contentTo: 2, key: 'av' }], [])
    expect(result).toEqual([{ key: 'av', naturalWidth: 18 }])
    const dom = buildTypographyInlineDom(document, sample, { from: 0, to: 2, contentTo: 2, key: 'av' }, [], [])
    expect(dom.childNodes).toHaveLength(1)
    const span = dom.firstChild as HTMLElement
    expect(span.textContent).toBe('AV')
    expect(span.style.fontKerning).toBe('normal')
    expect(span.style.fontFeatureSettings).toBe('normal')
    service.dispose()
  })
  it('batchesWritesBeforeReadsAndDisposes', () => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
      return { width: this.parentElement!.children.length * 10 } as DOMRect
    })
    const service = createTypographyMeasurementService(document)
    const lines = [{ from: 0, to: 1, contentTo: 1, key: 'a' }, { from: 1, to: 2, contentTo: 2, key: 'v' }]
    expect(service.measureBatch(sample, lines, []).map(x => x.naturalWidth)).toEqual([20, 20])
    service.dispose()
    expect(document.querySelector('[data-typography-measure]')).toBeNull()
    expect(() => service.measureBatch(sample, lines, [])).toThrow('disposed')
  })
})
