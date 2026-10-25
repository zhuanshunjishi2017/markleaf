import { describe, expect, it } from 'vitest'
import type { MeasuredLine } from '@markleaf/typography'
import { correctTypographyLine } from '../src/typography/projection-correction'
const line: MeasuredLine = { from: 0, to: 1, contentTo: 1, key: 'l', targetWidth: 100, naturalWidth: 99,
  cost: 0, breakReason: 'optimal', slots: [{ id: 'a', at: 1, kind: 'cjk', owner: { from: 0, to: 1 }, edge: 'end',
    natural: 0, min: 0, max: 2, weight: 1, leftStyleKey: 's', rightStyleKey: 's' }],
  gaps: [{ slotId: 'a', width: 1, adjustment: 1 }] }
describe('bounded final correction', () => {
  it('acceptsHalfPixelAndCorrectsOnlySmallRemainingBudget', () => {
    expect(correctTypographyLine(line, 99.5)).toEqual({ status: 'accepted' })
    const result = correctTypographyLine(line, 99.25)
    expect(result).toMatchObject({ status: 'corrected', gaps: [{ width: 1.75, adjustment: 1.75 }] })
    expect(correctTypographyLine(line, 98.99)).toEqual({ status: 'replan' })
    expect(correctTypographyLine({ ...line, slots: [{ ...line.slots[0]!, max: 1.2 }] }, 99.25)).toEqual({ status: 'replan' })
  })
  it('neverSpendsCapacityTwiceOrStretchesLastLine', () => {
    expect(correctTypographyLine({ ...line, gaps: [{ slotId: 'a', width: 1.75, adjustment: 1.75 }] }, 99.25)).toEqual({ status: 'replan' })
    expect(correctTypographyLine({ ...line, breakReason: 'paragraph-end' }, 50)).toEqual({ status: 'accepted' })
    expect(correctTypographyLine(line, Number.NaN)).toEqual({ status: 'replan' })
  })
})
