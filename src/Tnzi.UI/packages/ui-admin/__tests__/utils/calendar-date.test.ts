import { describe, it, expect } from 'vitest'
import { toIsoDateValue, toLocalDayBounds, tsToIsoDate } from '../../src/utils/calendar-date'
import { tsToIsoDate as financeTsToIsoDate } from '../../src/utils/finance-format'

describe('tsToIsoDate', () => {
  it('reads the local calendar day of a picker timestamp', () => {
    expect(tsToIsoDate(new Date(2026, 0, 5).getTime())).toBe('2026-01-05')
    expect(tsToIsoDate(new Date(2026, 11, 31, 23, 59).getTime())).toBe('2026-12-31')
  })

  it('is the same function the finance helpers export', () => {
    expect(financeTsToIsoDate).toBe(tsToIsoDate)
  })
})

describe('toIsoDateValue', () => {
  it('turns a picked day into a calendar date', () => {
    expect(toIsoDateValue(new Date(1990, 4, 1).getTime())).toBe('1990-05-01')
  })

  it('sends a value the record arrived with back unchanged', () => {
    expect(toIsoDateValue('2027-06-30T17:00:00Z')).toBe('2027-06-30T17:00:00Z')
    expect(toIsoDateValue('2027-06-30')).toBe('2027-06-30')
  })

  it('sends a Date as the instant it holds', () => {
    const d = new Date(Date.UTC(2027, 5, 30, 17))
    expect(toIsoDateValue(d)).toBe('2027-06-30T17:00:00.000Z')
  })

  it('reads an empty field as no date', () => {
    expect(toIsoDateValue(null)).toBeNull()
    expect(toIsoDateValue(undefined)).toBeNull()
    expect(toIsoDateValue('')).toBeNull()
    expect(toIsoDateValue('  ')).toBeNull()
    expect(toIsoDateValue(Number.NaN)).toBeNull()
    expect(toIsoDateValue(new Date(Number.NaN))).toBeNull()
  })
})

describe('toLocalDayBounds', () => {
  const startOf = (y: number, m: number, d: number) => new Date(y, m - 1, d).toISOString()
  const endOf = (y: number, m: number, d: number) => new Date(y, m - 1, d, 23, 59, 59, 999).toISOString()

  it('covers the whole of a one-day range', () => {
    expect(toLocalDayBounds({ from: '2026-10-10', to: '2026-10-10' }, 'from', 'to')).toEqual({
      from: startOf(2026, 10, 10),
      to: endOf(2026, 10, 10),
    })
  })

  it('bounds an open-ended range on the side that is set', () => {
    expect(toLocalDayBounds({ to: '2026-10-10' }, 'from', 'to')).toEqual({ to: endOf(2026, 10, 10) })
  })

  it('accepts a picker timestamp as well as a calendar date', () => {
    expect(toLocalDayBounds({ to: new Date(2026, 9, 10).getTime() }, 'from', 'to')).toEqual({ to: endOf(2026, 10, 10) })
  })

  it('leaves an instant some caller already built, and every other filter, alone', () => {
    const filters = { from: '2026-10-10T04:00:00.000Z', status: 'Failed' }
    expect(toLocalDayBounds(filters, 'from', 'to')).toEqual(filters)
  })

  it('returns a new object and leaves the input as it was', () => {
    const filters = { from: '2026-10-10' }
    const out = toLocalDayBounds(filters, 'from', 'to')
    expect(out).not.toBe(filters)
    expect(filters).toEqual({ from: '2026-10-10' })
  })
})
