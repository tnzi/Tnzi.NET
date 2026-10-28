import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'

/**
 * The period lives in module state seeded from localStorage, so each case
 * re-imports the module after arranging storage and the clock.
 *
 * "This month" and "Year to date" are rules, not ranges. Restoring the dates a
 * relative preset resolved to on the day it was picked made the picker read
 * "Year to date" in October while every report ran on a September range.
 */
const STORAGE_KEY = 'tnzi-admin-finance-period'

type FinancePeriodModule = typeof import('../../src/headless/useFinancePeriod')

async function load(): Promise<FinancePeriodModule> {
  vi.resetModules()
  return import('../../src/headless/useFinancePeriod')
}

function store(value: Record<string, unknown>): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(value))
}

function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state })
  document.dispatchEvent(new Event('visibilitychange'))
}

describe('useFinancePeriod - restoring a stored selection', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.useFakeTimers({ toFake: ['Date'] })
  })

  afterEach(() => {
    vi.useRealTimers()
    localStorage.clear()
  })

  it('★ re-resolves "Year to date" against today instead of the day it was picked', async () => {
    store({ from: '2026-01-01', to: '2026-09-15', preset: 'year-to-date', comparison: 'none' })
    vi.setSystemTime(new Date(2026, 9, 11, 9, 0)) // Oct 11
    const { useFinancePeriod } = await load()
    const { period, preset, asOf, rollovers } = useFinancePeriod()
    expect(preset.value).toBe('year-to-date')
    expect(period.value).toEqual({ from: '2026-01-01', to: '2026-10-11' })
    expect(asOf.value).toBe('2026-10-11')
    // Restoring is not a rollover: nothing ran on the stale range yet.
    expect(rollovers.value).toBe(0)
  })

  it('★ re-resolves "This month" into the current month', async () => {
    store({ from: '2026-09-01', to: '2026-09-30', preset: 'this-month', comparison: 'previous-period' })
    vi.setSystemTime(new Date(2026, 9, 11))
    const { useFinancePeriod } = await load()
    const { period, comparisonPeriod } = useFinancePeriod()
    expect(period.value).toEqual({ from: '2026-10-01', to: '2026-10-31' })
    // The comparison follows the live range, not the stored one.
    expect(comparisonPeriod.value).toEqual({ from: '2026-08-31', to: '2026-09-30' })
  })

  it('keeps a custom range exactly as stored', async () => {
    store({ from: '2025-03-01', to: '2025-03-31', preset: 'custom', comparison: 'none' })
    vi.setSystemTime(new Date(2026, 9, 11))
    const { useFinancePeriod } = await load()
    expect(useFinancePeriod().period.value).toEqual({ from: '2025-03-01', to: '2025-03-31' })
  })

  it('treats an unknown stored preset as a custom range and an unknown comparison as none', async () => {
    store({ from: '2025-03-01', to: '2025-03-31', preset: 'fortnight', comparison: 'vs-mars' })
    vi.setSystemTime(new Date(2026, 9, 11))
    const { useFinancePeriod } = await load()
    const { period, preset, comparison } = useFinancePeriod()
    expect(preset.value).toBe('custom')
    expect(comparison.value).toBe('none')
    expect(period.value).toEqual({ from: '2025-03-01', to: '2025-03-31' })
  })

  it('★ a finance surface mounted on a later day picks up the new range', async () => {
    vi.setSystemTime(new Date(2026, 8, 30, 23, 0)) // Sep 30
    const { useFinancePeriod } = await load()
    useFinancePeriod().setPreset('this-month')
    expect(useFinancePeriod().period.value).toEqual({ from: '2026-09-01', to: '2026-09-30' })

    vi.setSystemTime(new Date(2026, 9, 1, 8, 0)) // Oct 1, same tab
    expect(useFinancePeriod().period.value).toEqual({ from: '2026-10-01', to: '2026-10-31' })
  })

  it('★ a tab left open across midnight moves the range when it is shown again, and says so', async () => {
    vi.setSystemTime(new Date(2026, 8, 30, 23, 0))
    const { useFinancePeriod } = await load()
    const state = useFinancePeriod()
    state.setPreset('year-to-date')
    const before = state.rollovers.value

    vi.setSystemTime(new Date(2026, 9, 1, 8, 0))
    setVisibility('hidden')
    expect(state.period.value.to).toBe('2026-09-30')
    setVisibility('visible')
    expect(state.period.value).toEqual({ from: '2026-01-01', to: '2026-10-01' })
    expect(state.rollovers.value).toBe(before + 1)

    // Shown again the same day: nothing moved, nothing announced.
    setVisibility('visible')
    expect(state.rollovers.value).toBe(before + 1)
  })

  it('never moves a custom range on its own', async () => {
    vi.setSystemTime(new Date(2026, 8, 30))
    const { useFinancePeriod } = await load()
    const state = useFinancePeriod()
    state.setRange({ from: '2026-09-01', to: '2026-09-15' })
    vi.setSystemTime(new Date(2026, 9, 5))
    setVisibility('visible')
    expect(useFinancePeriod().period.value).toEqual({ from: '2026-09-01', to: '2026-09-15' })
  })
})
