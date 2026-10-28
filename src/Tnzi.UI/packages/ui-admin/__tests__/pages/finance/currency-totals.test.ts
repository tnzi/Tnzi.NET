import { describe, it, expect } from 'vitest'
import { fmtCurrencyTotals, totalsByCurrency } from '../../../src/pages/finance/money'

// A queue or a pay run can hold several currencies (the EFT queue mixes NACHA
// USD and CPA-005 CAD payments by design). Summing across them and labelling
// the result with the first row's currency gives a figure that looks exact and
// means nothing; each currency gets its own total.

type Row = { amount?: number | null; currency?: string | null }
const amount = (r: Row) => r.amount
const currency = (r: Row) => r.currency

describe('totalsByCurrency', () => {
  it('totals each currency on its own, rounded to cents, in code order', () => {
    const rows: Row[] = [
      { amount: 1000, currency: 'USD' },
      { amount: 10.1, currency: 'CAD' },
      { amount: 20.2, currency: 'CAD' },
      { amount: 0.1, currency: 'USD' },
    ]
    expect(totalsByCurrency(rows, amount, currency)).toEqual([
      { currency: 'CAD', total: 30.3 },
      { currency: 'USD', total: 1000.1 },
    ])
  })

  it('treats a missing amount as nothing and a missing code as its own group', () => {
    const rows: Row[] = [{ amount: null, currency: 'USD' }, { amount: 5, currency: null }]
    expect(totalsByCurrency(rows, amount, currency)).toEqual([
      { currency: '', total: 5 },
      { currency: 'USD', total: 0 },
    ])
  })

  it('is empty for no rows', () => {
    expect(totalsByCurrency([], amount, currency)).toEqual([])
  })
})

describe('fmtCurrencyTotals', () => {
  it('formats one currency as a money figure', () => {
    expect(fmtCurrencyTotals([{ currency: 'USD', total: 1500 }])).toBe('$1,500.00')
  })

  it('★ lists several currencies by code instead of adding them up', () => {
    const text = fmtCurrencyTotals([
      { currency: 'CAD', total: 500 },
      { currency: 'USD', total: 1000 },
    ])
    expect(text).toBe('500.00 CAD · 1,000.00 USD')
  })

  it('shows zero for no totals', () => {
    expect(fmtCurrencyTotals([])).toBe('0.00')
  })
})
