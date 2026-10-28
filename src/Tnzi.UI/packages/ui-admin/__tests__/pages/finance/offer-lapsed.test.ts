import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { isLapsed, type OfferRow } from '../../../src/pages/finance/offer-config'
import { FinanceOfferStatus } from '../../../src/services/bridges/finance-bridge'

// An estimate's expiry date (a purchase order's expected date) is a calendar
// day the document is good through. The check compared the instant the date
// string parses to with "now", so the document read as lapsed from midnight
// of its last valid day (UTC midnight for a date-only value, which in the
// Americas is the evening before).

function offer(date: string, status = FinanceOfferStatus.Sent): OfferRow {
  return { id: 'e1', status, expiryDate: date } as OfferRow
}

describe('isLapsed', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 9, 10, 21, 0))
  })
  afterEach(() => {
    vi.useRealTimers()
  })

  it('★ is not lapsed on its last valid day, in either wire form', () => {
    expect(isLapsed(offer('2026-10-10'), 'expiryDate')).toBe(false)
    expect(isLapsed(offer('2026-10-10T00:00:00'), 'expiryDate')).toBe(false)
  })

  it('★ is not lapsed the evening before its last valid day', () => {
    vi.setSystemTime(new Date(2026, 9, 9, 21, 0))
    expect(isLapsed(offer('2026-10-10'), 'expiryDate')).toBe(false)
  })

  it('is lapsed once its last valid day has passed', () => {
    expect(isLapsed(offer('2026-10-09'), 'expiryDate')).toBe(true)
    expect(isLapsed(offer('2026-10-09T00:00:00'), 'expiryDate')).toBe(true)
  })

  it('only documents still worth chasing can lapse', () => {
    expect(isLapsed(offer('2026-10-01', FinanceOfferStatus.Draft), 'expiryDate')).toBe(false)
    expect(isLapsed({ id: 'e1', status: FinanceOfferStatus.Sent } as OfferRow, 'expiryDate')).toBe(false)
  })
})
