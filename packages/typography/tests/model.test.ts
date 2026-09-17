import { describe, expect, it } from 'vitest'
import type { LayoutPlan } from '../src/model'

describe('typography package', () => {
  it('exposes the public model types through a plan-shaped value', () => {
    const plan: LayoutPlan = {
      lines: [],
      fallback: false,
    }

    expect(plan).toEqual({ lines: [], fallback: false })
  })
})
