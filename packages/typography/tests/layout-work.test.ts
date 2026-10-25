import { describe, expect, it } from 'vitest'
import { createLayoutWork } from '../src/layout-work'

describe('layout work ledger', () => {
  it('countsOnlyActiveSegmentsIncludingThrows', () => {
    let now = 0
    const work = createLayoutWork(() => now)
    work.run(() => { now += 3 })
    now += 1000
    expect(() => work.run(() => { now += 2; throw Error('layout') })).toThrow('layout')
    expect(work.stats().activeMs).toBe(5)
    expect(work.exhausted()).toBeUndefined()
  })
  it('detectsActiveLimitInsideRun', () => {
    let now = 0
    const work = createLayoutWork(() => now)
    work.run(() => { now = 129; expect(work.exhausted()).toBe('active-time') })
    expect(work.stats().activeMs).toBe(129)
  })
  it('boundsAllEdgeChecksAcrossSlices', () => {
    const work = createLayoutWork(() => 0)
    for (let i = 0; i < 65536; i++) expect(work.visitEdge()).toBe(true)
    expect(work.visitEdge()).toBe(false)
    expect(work.exhausted()).toBe('edge-checks')
    expect(work.stats().edgeChecks).toBe(65536)
  })
  it('reservesUniqueRequestsAtomically', () => {
    const work = createLayoutWork(() => 0)
    expect(work.reserveMeasurements(Array.from({ length: 255 }, (_, i) => `line:${i}`))).toBe(true)
    expect(work.reserveMeasurements(['line:255', 'line:256'])).toBe(false)
    expect(work.stats().uniqueMeasurements).toBe(255)
    const other = createLayoutWork(() => 0)
    expect(other.reserveMeasurements(Array.from({ length: 256 }, (_, i) => `line:${i}`))).toBe(true)
    expect(other.reserveMeasurements(['line:0', 'line:255'])).toBe(true)
    expect(other.stats().uniqueMeasurements).toBe(256)
    expect(other.reserveMeasurements(['space:0'])).toBe(false)
    expect(other.exhausted()).toBe('measurements')
  })
})
