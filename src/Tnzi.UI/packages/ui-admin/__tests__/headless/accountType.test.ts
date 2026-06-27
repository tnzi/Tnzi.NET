import { describe, it, expect } from 'vitest'
import { detectAccountType } from '../../src/headless/accountType'

describe('detectAccountType', () => {
  it('detects email when the value contains @', () => {
    expect(detectAccountType('user@example.com')).toBe('email')
    expect(detectAccountType('a+tag@tnzi.cc')).toBe('email')
  })

  it('detects phone otherwise', () => {
    expect(detectAccountType('+8613800138000')).toBe('phone')
    expect(detectAccountType('13800138000')).toBe('phone')
    expect(detectAccountType('555-1234')).toBe('phone')
  })
})
