import { describe, it, expect } from 'vitest'
import { useLoginAccountField } from '../../../src/headless/auth/useLoginAccountField'

const translate = (_k: string, fb?: string) => fb ?? _k

// A rule applies either via a regex `pattern` (phone/email) or a `validator`
// (emailOrPhone); normalise both to true | Error.
function applyRule(rule: { pattern?: RegExp; validator?: unknown }, value: string): true | Error {
  if (typeof rule.validator === 'function') {
    const out = (rule.validator as (r: unknown, v: unknown) => true | Error)({}, value)
    return out instanceof Error ? out : true
  }
  if (rule.pattern) return rule.pattern.test(value) ? true : new Error('pattern fail')
  return true
}

describe('useLoginAccountField', () => {
  it('sms-only channel → phone rule + "Phone" label + phone placeholder', () => {
    const { rule, label, placeholder } = useLoginAccountField(translate, () => ({ sms: true, email: false }))
    expect(label.value).toBe('Phone')
    expect(placeholder.value).toBe('Enter phone number')
    expect(applyRule(rule.value[1]!, 'user@example.com')).toBeInstanceOf(Error)
    expect(applyRule(rule.value[1]!, '+8613800138000')).toBe(true)
  })

  it('email-only channel → email rule + "Email" label + email placeholder', () => {
    const { rule, label, placeholder } = useLoginAccountField(translate, () => ({ sms: false, email: true }))
    expect(label.value).toBe('Email')
    expect(placeholder.value).toBe('Enter email')
    expect(applyRule(rule.value[1]!, '+8613800138000')).toBeInstanceOf(Error)
    expect(applyRule(rule.value[1]!, 'user@example.com')).toBe(true)
  })

  it('both channels → emailOrPhone rule + "Phone / Email" label', () => {
    const { rule, label } = useLoginAccountField(translate, () => ({ sms: true, email: true }))
    expect(label.value).toBe('Phone / Email')
    expect(applyRule(rule.value[1]!, 'user@example.com')).toBe(true)
    expect(applyRule(rule.value[1]!, '+8613800138000')).toBe(true)
    expect(applyRule(rule.value[1]!, 'not-an-account!')).toBeInstanceOf(Error)
  })

  it('tags every account rule with key="account" (so per-field validate matches)', () => {
    const { rule } = useLoginAccountField(translate, () => ({ sms: true, email: true }))
    expect(rule.value.length).toBeGreaterThan(0)
    expect(rule.value.every((ru) => (ru as { key?: string }).key === 'account')).toBe(true)
  })
})
