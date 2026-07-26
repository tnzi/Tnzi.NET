import { describe, it, expect } from 'vitest'
import { resolveComparison, resolvePreset } from '../../src/headless/useFinancePeriod'

describe('resolvePreset', () => {
  const today = new Date(2026, 6, 15) // Jul 15, 2026

  it('resolves calendar presets against a fixed today', () => {
    expect(resolvePreset('this-month', today)).toEqual({ from: '2026-07-01', to: '2026-07-31' })
    expect(resolvePreset('last-month', today)).toEqual({ from: '2026-06-01', to: '2026-06-30' })
    expect(resolvePreset('this-quarter', today)).toEqual({ from: '2026-07-01', to: '2026-09-30' })
    expect(resolvePreset('last-quarter', today)).toEqual({ from: '2026-04-01', to: '2026-06-30' })
    expect(resolvePreset('last-year', today)).toEqual({ from: '2025-01-01', to: '2025-12-31' })
  })

  it('ends year-to-date on today, not on Dec 31', () => {
    expect(resolvePreset('year-to-date', today)).toEqual({ from: '2026-01-01', to: '2026-07-15' })
  })

  it('leaves a custom range to the caller', () => {
    expect(resolvePreset('custom', today)).toBeNull()
  })
})

describe('resolveComparison', () => {
  const q3 = { from: '2026-07-01', to: '2026-09-30' }

  it('is off by default', () => {
    expect(resolveComparison(q3, 'none')).toBeNull()
  })

  it('shifts back by the range own length for previous-period', () => {
    // Q3 is 92 days; the preceding 92 days end the day before it starts.
    expect(resolveComparison(q3, 'previous-period')).toEqual({ from: '2026-03-31', to: '2026-06-30' })
  })

  it('shifts back exactly one calendar year for previous-year', () => {
    expect(resolveComparison(q3, 'previous-year')).toEqual({ from: '2025-07-01', to: '2025-09-30' })
  })

  it('handles a single-day range', () => {
    const day = { from: '2026-07-15', to: '2026-07-15' }
    expect(resolveComparison(day, 'previous-period')).toEqual({ from: '2026-07-14', to: '2026-07-14' })
  })
})
