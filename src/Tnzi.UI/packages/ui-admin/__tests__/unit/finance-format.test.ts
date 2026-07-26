import { describe, it, expect } from 'vitest'
import {
  formatAccountingDate,
  formatAccountingDateRange,
  formatAmount,
  formatMoney,
  formatPercent,
  isoDateToLocalTs,
  srMoney,
  tsToIsoDate,
  variance,
} from '../../src/utils/finance-format'

describe('formatMoney', () => {
  it('wraps negatives in parentheses, never a minus sign', () => {
    expect(formatMoney(-1234.5)).toBe('(1,234.50)')
    expect(formatMoney(1234.5)).toBe('1,234.50')
  })

  it('puts the currency symbol inside the parentheses', () => {
    // `($1,234.50)` is the accounting form; `$(1,234.50)` is not.
    expect(formatMoney(-1234.5, { currency: 'USD' })).toBe('($1,234.50)')
  })

  it('never invents a currency symbol when the document has no code', () => {
    expect(formatMoney(10)).toBe('10.00')
    expect(formatMoney(10, { currency: null })).toBe('10.00')
  })

  it('renders missing values as a dash, and zero only when asked', () => {
    expect(formatMoney(null)).toBe('-')
    expect(formatMoney(undefined)).toBe('-')
    expect(formatMoney(0)).toBe('0.00')
    expect(formatMoney(0, { zeroDash: true })).toBe('-')
  })

  it('normalizes -0 so a rounded-to-zero row never shows (0.00)', () => {
    expect(formatMoney(-0)).toBe('0.00')
  })

  /**
   * A rounding residual is not a negative. Testing the sign before rounding
   * sent `-0.004` down the parenthesised branch and printed `(0.00)`, which a
   * reader parses as a real credit.
   */
  it('decides the sign after rounding, not before', () => {
    expect(formatMoney(-0.004)).toBe('0.00')
    expect(formatMoney(-0.004, { zeroDash: true })).toBe('-')
    expect(formatMoney(-0.4, { decimals: 0 })).toBe('0')
    // Still a real negative once it survives the rounding.
    expect(formatMoney(-0.006)).toBe('(0.01)')
  })

  it('supports opting out of the accounting form for round-trippable output', () => {
    expect(formatMoney(-5, { accounting: false })).toBe('-5.00')
  })

  it('signs positives only when asked (variance columns)', () => {
    expect(formatMoney(5, { signed: true })).toBe('+5.00')
    expect(formatMoney(-5, { signed: true })).toBe('(5.00)')
  })

  it('honours a decimal override', () => {
    expect(formatMoney(1.23456, { decimals: 4 })).toBe('1.2346')
  })
})

describe('srMoney', () => {
  it('gives screen readers an explicit minus, not the visual parentheses', () => {
    // Parentheses and red text are both purely visual; the accessible name has
    // to carry the sign on its own (AODA / WCAG 2.0 AA).
    expect(srMoney(-1234.5)).toBe('-1,234.50')
    expect(srMoney(-1234.5, { currency: 'USD' })).toBe('-$1,234.50')
  })
})

describe('formatAmount / formatPercent', () => {
  it('drops the currency symbol for report bodies', () => {
    expect(formatAmount(-99)).toBe('(99.00)')
  })

  it('parenthesises negative percentages too', () => {
    expect(formatPercent(12.34)).toBe('12.3%')
    expect(formatPercent(-3)).toBe('(3.0%)')
    expect(formatPercent(null)).toBe('-')
  })
})

describe('formatAccountingDate', () => {
  it('is unambiguous and independent of the viewer locale', () => {
    expect(formatAccountingDate('2026-01-15')).toBe('Jan 15, 2026')
  })

  it('reads a backend UTC-midnight value without shifting the calendar day', () => {
    // `new Date(...)` + local getters would render Jan 14 west of UTC.
    expect(formatAccountingDate('2026-01-15T00:00:00Z')).toBe('Jan 15, 2026')
  })

  it('falls back for empty and unparseable values', () => {
    expect(formatAccountingDate(null)).toBe('-')
    expect(formatAccountingDate('')).toBe('-')
    expect(formatAccountingDate('nonsense', { fallback: 'n/a' })).toBe('n/a')
  })
})

describe('formatAccountingDateRange', () => {
  it('states the year once when both ends share it', () => {
    expect(formatAccountingDateRange('2026-01-01', '2026-03-31')).toBe('Jan 1 - Mar 31, 2026')
  })

  it('spells both ends out across a year boundary', () => {
    expect(formatAccountingDateRange('2025-12-01', '2026-01-31')).toBe('Dec 1, 2025 - Jan 31, 2026')
  })

  it('states the month once when both ends share it', () => {
    expect(formatAccountingDateRange('2026-03-01', '2026-03-31')).toBe('Mar 1 - 31, 2026')
  })
})

describe('date round-trip helpers', () => {
  it('round-trips a date-only value through a local-midnight timestamp', () => {
    expect(tsToIsoDate(isoDateToLocalTs('2026-07-04'))).toBe('2026-07-04')
  })
})

describe('variance', () => {
  it('reports the delta and the percentage change', () => {
    expect(variance(120, 100)).toEqual({ delta: 20, percent: 20 })
  })

  it('refuses to print an infinite percentage against a zero base', () => {
    expect(variance(50, 0)).toEqual({ delta: 50, percent: null })
  })

  it('measures the change against the magnitude of the base', () => {
    // Going from -100 to -50 is a 50% improvement, not -50%.
    expect(variance(-50, -100)).toEqual({ delta: 50, percent: 50 })
  })
})
