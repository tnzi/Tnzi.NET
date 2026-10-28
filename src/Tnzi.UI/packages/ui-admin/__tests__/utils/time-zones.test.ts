import { afterEach, describe, it, expect, vi } from 'vitest'
import { browserTimeZone, convertTimeOfDay, timeZoneIds } from '../../src/utils/time-zones'

describe('timeZoneIds', () => {
  afterEach(() => vi.restoreAllMocks())

  it('lists the runtime zones plus UTC, sorted and without duplicates', () => {
    const ids = timeZoneIds()
    expect(ids).toContain('UTC')
    expect(ids).toContain('America/Toronto')
    expect(new Set(ids).size).toBe(ids.length)
    expect([...ids].sort((a, b) => a.localeCompare(b))).toEqual(ids)
  })

  it('always includes the ids the form must display, even ones the runtime does not list', () => {
    expect(timeZoneIds(['Asia/Calcutta', null, undefined, ''])).toContain('Asia/Calcutta')
  })

  it('degrades to the extras when the runtime cannot enumerate zones', () => {
    vi.spyOn(Intl, 'supportedValuesOf').mockImplementation(() => {
      throw new RangeError('unsupported')
    })
    expect(timeZoneIds(['Europe/London'])).toEqual(['Europe/London', 'UTC'])
  })
})

describe('browserTimeZone', () => {
  afterEach(() => vi.restoreAllMocks())

  it('returns the runtime zone', () => {
    expect(browserTimeZone()).toBe(Intl.DateTimeFormat().resolvedOptions().timeZone)
  })

  it('falls back to UTC when the runtime cannot tell', () => {
    vi.spyOn(Intl, 'DateTimeFormat').mockImplementation(() => {
      throw new Error('no Intl')
    })
    expect(browserTimeZone()).toBe('UTC')
  })
})

describe('convertTimeOfDay', () => {
  const july = new Date('2026-07-15T12:00:00Z')
  const january = new Date('2026-01-15T12:00:00Z')

  it('keeps the instant: 03:00 UTC is 23:00 in Toronto in summer and 22:00 in winter', () => {
    expect(convertTimeOfDay('03:00:00', 'UTC', 'America/Toronto', july)).toBe('23:00:00')
    expect(convertTimeOfDay('03:00:00', 'UTC', 'America/Toronto', january)).toBe('22:00:00')
  })

  it('converts between two named zones', () => {
    expect(convertTimeOfDay('22:00:00', 'Asia/Tokyo', 'America/Toronto', july)).toBe('09:00:00')
  })

  it('handles quarter-hour offsets', () => {
    expect(convertTimeOfDay('00:00:00', 'UTC', 'Asia/Kathmandu', july)).toBe('05:45:00')
  })

  it('returns the time untouched for the same zone and null for an unknown one', () => {
    expect(convertTimeOfDay('22:10:30', 'Europe/London', 'Europe/London', july)).toBe('22:10:30')
    expect(convertTimeOfDay('22:00:00', 'UTC', 'Mars/Olympus', july)).toBeNull()
    expect(convertTimeOfDay('not a time', 'UTC', 'Asia/Tokyo', july)).toBeNull()
  })
})
