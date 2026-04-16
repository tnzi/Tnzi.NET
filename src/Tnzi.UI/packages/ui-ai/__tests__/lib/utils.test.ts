import { describe, it, expect } from 'vitest'
import { formatCompactNumber } from '../../src/lib/utils'

describe('lib/utils', () => {
  describe('formatCompactNumber', () => {
    it('formats millions with M suffix', () => {
      expect(formatCompactNumber(1_000_000)).toBe('1.0M')
      expect(formatCompactNumber(2_500_000)).toBe('2.5M')
      expect(formatCompactNumber(9_999_999)).toBe('10.0M')
    })

    it('formats thousands with K suffix', () => {
      expect(formatCompactNumber(1_000)).toBe('1.0K')
      expect(formatCompactNumber(1_500)).toBe('1.5K')
      expect(formatCompactNumber(999_999)).toBe('1000.0K')
    })

    it('formats small numbers with locale', () => {
      expect(formatCompactNumber(0)).toBe((0).toLocaleString())
      expect(formatCompactNumber(1)).toBe((1).toLocaleString())
      expect(formatCompactNumber(999)).toBe((999).toLocaleString())
    })
  })
})
