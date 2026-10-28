/**
 * The storage unit plan allowances, usage and the overage line share: 1 GB =
 * 2^30 bytes, the same unit `formatFileSize` and object storage bills use.
 */
import { describe, it, expect } from 'vitest'
import { STORAGE_BYTES_PER_GB, bytesToGb, formatStorageGb, gbToBytes } from '../../src/services/payment'

describe('storage units', () => {
  it('counts a GB as 2^30 bytes', () => {
    expect(STORAGE_BYTES_PER_GB).toBe(2 ** 30)
    expect(gbToBytes(250)).toBe(268435456000)
    expect(bytesToGb(268435456000)).toBe(250)
  })

  it('reads an empty or invalid allowance as no allowance, not as zero', () => {
    expect(gbToBytes(null)).toBeNull()
    expect(gbToBytes('')).toBeNull()
    expect(gbToBytes(-1)).toBeNull()
    expect(gbToBytes('abc')).toBeNull()
    expect(gbToBytes(0)).toBe(0)
    expect(bytesToGb(undefined)).toBeNull()
  })

  it('keeps whole bytes for a fractional GB', () => {
    expect(gbToBytes(0.5)).toBe(536870912)
    expect(gbToBytes('1.25')).toBe(1342177280)
  })

  it('formats in GB, so usage and allowance read in the same unit', () => {
    expect(formatStorageGb(250 * STORAGE_BYTES_PER_GB)).toBe('250 GB')
    expect(formatStorageGb(512 * 1024 * 1024)).toBe('0.5 GB')
    expect(formatStorageGb(null)).toBe('')
  })
})
