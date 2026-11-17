import { describe, it, expect } from 'vitest'
import type { IFormRule } from '@tnzi/core/types'
import { useLoginAccountField } from '../../../src/headless/auth/useLoginAccountField'

const translate = (_k: string, fb?: string) => fb ?? _k

// A rule applies either via a regex `pattern` (phone/email) or a `validator`
// (emailOrPhone), which takes the value alone and answers true or a message.
function applyRule(rule: IFormRule, value: string): true | string {
  if (rule.validator) {
    const out = rule.validator(value)
    return out === true ? true : String(out)
  }
  if (rule.pattern) return rule.pattern.test(value) ? true : 'pattern fail'
  return true
}

const FAILS = expect.any(String)

describe('useLoginAccountField', () => {
  it('sms-only channel → phone rule + "Phone" label + phone placeholder', () => {
    const { rule, label, placeholder } = useLoginAccountField(translate, () => ({ sms: true, email: false }))
    expect(label.value).toBe('Phone')
    expect(placeholder.value).toBe('Enter phone number')
    expect(applyRule(rule.value[1]!, 'user@example.com')).toEqual(FAILS)
    expect(applyRule(rule.value[1]!, '+8613800138000')).toBe(true)
  })

  it('email-only channel → email rule + "Email" label + email placeholder', () => {
    const { rule, label, placeholder } = useLoginAccountField(translate, () => ({ sms: false, email: true }))
    expect(label.value).toBe('Email')
    expect(placeholder.value).toBe('Enter email')
    expect(applyRule(rule.value[1]!, '+8613800138000')).toEqual(FAILS)
    expect(applyRule(rule.value[1]!, 'user@example.com')).toBe(true)
  })

  it('both channels → emailOrPhone rule + "Phone / Email" label', () => {
    const { rule, label } = useLoginAccountField(translate, () => ({ sms: true, email: true }))
    expect(label.value).toBe('Phone / Email')
    expect(applyRule(rule.value[1]!, 'user@example.com')).toBe(true)
    expect(applyRule(rule.value[1]!, '+8613800138000')).toBe(true)
    expect(applyRule(rule.value[1]!, 'not-an-account!')).toEqual(FAILS)
  })

  it('leads with the required rule, whatever the channels', () => {
    for (const channels of [{ sms: true, email: false }, { sms: false, email: true }, { sms: true, email: true }]) {
      const { rule } = useLoginAccountField(translate, () => channels)
      expect(rule.value[0]).toMatchObject({ required: true })
    }
  })
})
