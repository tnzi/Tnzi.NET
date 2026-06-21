import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { formatChatTime } from '../../../src/components/chat/time'

// Build "now" at LOCAL midday so no fixture sits near a day boundary, making
// the today/yesterday/earlier classification identical in every timezone.
// `new Date(y, mIndex, d, h, m)` constructs a LOCAL time; `.toISOString()`
// gives the matching UTC instant, which formatChatTime parses back to the
// same local day.
const NOW = new Date(2024, 2, 15, 12, 0, 0)          // local 2024-03-15 12:00
const TODAY_ISO = new Date(2024, 2, 15, 9, 15, 0).toISOString()     // same local day
const YESTERDAY_ISO = new Date(2024, 2, 14, 18, 0, 0).toISOString() // local yesterday
const EARLIER_ISO = new Date(2024, 2, 10, 8, 0, 0).toISOString()    // local earlier

describe('formatChatTime', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(NOW)
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('returns empty string for null', () => {
    expect(formatChatTime(null)).toBe('')
  })

  it('returns empty string for undefined', () => {
    expect(formatChatTime(undefined)).toBe('')
  })

  it('returns empty string for empty string', () => {
    expect(formatChatTime('')).toBe('')
  })

  it('returns empty string for an unparseable date', () => {
    expect(formatChatTime('not-a-date')).toBe('')
  })

  it('returns exact HH:mm for a message sent today', () => {
    expect(formatChatTime(TODAY_ISO)).toBe('09:15')
  })

  it('returns "Yesterday" for yesterday', () => {
    expect(formatChatTime(YESTERDAY_ISO)).toBe('Yesterday')
  })

  it('returns exact MM/DD for messages older than yesterday', () => {
    expect(formatChatTime(EARLIER_ISO)).toBe('03/10')
  })
})
