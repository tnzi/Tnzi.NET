import { describe, it, expect } from 'vitest'
import { ensureOk, unwrapResult } from '../../src/services/_mappers'

describe('_mappers ensureOk', () => {
  it('throws the envelope message on succeeded:false', () => {
    expect(() => ensureOk({ succeeded: false, success: false, message: 'Delete vetoed' })).toThrow(
      'Delete vetoed',
    )
  })

  it('throws the fallback message when the failure envelope has no message', () => {
    expect(() => ensureOk({ succeeded: false })).toThrow('Request failed')
    expect(() => ensureOk({ success: false }, 'Custom fallback')).toThrow('Custom fallback')
  })

  it('prefers succeeded over success when both are present', () => {
    expect(() => ensureOk({ succeeded: false, success: true, message: 'nope' })).toThrow('nope')
    expect(() => ensureOk({ succeeded: true, success: false })).not.toThrow()
  })

  it('tolerates a legitimately empty data payload on success (void endpoints)', () => {
    expect(() => ensureOk({ succeeded: true, success: true, data: null })).not.toThrow()
    expect(() => ensureOk({ succeeded: true, success: true })).not.toThrow()
  })

  it('passes through non-envelope values silently', () => {
    expect(() => ensureOk(undefined)).not.toThrow()
    expect(() => ensureOk(null)).not.toThrow()
    expect(() => ensureOk(42)).not.toThrow()
    expect(() => ensureOk({ some: 'object' })).not.toThrow()
  })

  it('stays consistent with unwrapResult duck-typing for success envelopes', () => {
    const envelope = { succeeded: true, success: true, data: { id: '1' } }
    expect(() => ensureOk(envelope)).not.toThrow()
    expect(unwrapResult(envelope)).toEqual({ id: '1' })
  })
})
