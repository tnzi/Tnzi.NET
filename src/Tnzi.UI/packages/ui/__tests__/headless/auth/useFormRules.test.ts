import { describe, it, expect } from 'vitest'
import { useFormRules } from '../../../src/headless/auth/useFormRules'

function runValidator(rule: { validator?: unknown }, value: unknown): true | Error {
  if (typeof rule.validator !== 'function') return true
  const fn = rule.validator as (
    r: unknown,
    v: unknown,
  ) => true | Error | Promise<unknown>
  const out = fn({}, value)
  if (out instanceof Error) return out
  return true
}

describe('useFormRules', () => {
  const { rules } = useFormRules()

  it('required accepts non-empty strings', () => {
    expect(runValidator(rules.required(), 'value')).toBe(true)
  })

  it('required rejects empty / whitespace / null', () => {
    expect(runValidator(rules.required(), '')).toBeInstanceOf(Error)
    expect(runValidator(rules.required(), '   ')).toBeInstanceOf(Error)
    expect(runValidator(rules.required(), null)).toBeInstanceOf(Error)
  })

  it('text rule chain has required + length', () => {
    const chain = rules.text({ min: 2, max: 5 })
    expect(chain.length).toBe(2)
    expect(runValidator(chain[1]!, 'a')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'abc')).toBe(true)
    expect(runValidator(chain[1]!, 'abcdef')).toBeInstanceOf(Error)
  })

  it('userName regex accepts valid usernames', () => {
    const regex = rules.userName[1] as { pattern: RegExp }
    expect(regex.pattern.test('admin')).toBe(true)
    expect(regex.pattern.test('admin_42')).toBe(true)
    expect(regex.pattern.test('a-b-c')).toBe(true)
  })

  it('userName regex rejects invalid usernames', () => {
    const regex = rules.userName[1] as { pattern: RegExp }
    expect(regex.pattern.test('ab')).toBe(false) // too short
    expect(regex.pattern.test('with space')).toBe(false)
    expect(regex.pattern.test('hi!')).toBe(false)
  })

  it('email regex accepts common addresses', () => {
    const regex = rules.email[1] as { pattern: RegExp }
    expect(regex.pattern.test('user@example.com')).toBe(true)
    expect(regex.pattern.test('admin+tag@tnzi.cc')).toBe(true)
  })

  it('email regex rejects malformed addresses', () => {
    const regex = rules.email[1] as { pattern: RegExp }
    expect(regex.pattern.test('plain')).toBe(false)
    expect(regex.pattern.test('no@dots')).toBe(false)
  })

  it('phone regex accepts international formats', () => {
    const regex = rules.phone[1] as { pattern: RegExp }
    expect(regex.pattern.test('+1 (555) 123-4567')).toBe(true)
    expect(regex.pattern.test('+8613800138000')).toBe(true)
    expect(regex.pattern.test('555-1234')).toBe(true)
  })

  it('password rule requires letters + digits', () => {
    const chain = rules.password()
    expect(runValidator(chain[1]!, 'onlylett')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, '12345678')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'Admin123')).toBe(true)
  })

  it('password rule enforces length range', () => {
    const chain = rules.password({ min: 6, max: 12 })
    expect(runValidator(chain[1]!, 'abc12')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'abc12345678901234')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'abc123')).toBe(true)
  })

  it('matches rule passes when values are equal', () => {
    const rule = rules.matches(() => 'secret')
    expect(runValidator(rule, 'secret')).toBe(true)
    expect(runValidator(rule, 'wrong')).toBeInstanceOf(Error)
  })

  it('account rule accepts username, email, or phone (length only)', () => {
    const chain = rules.account()
    expect(runValidator(chain[1]!, 'admin')).toBe(true)
    expect(runValidator(chain[1]!, 'user@example.com')).toBe(true)
    expect(runValidator(chain[1]!, '+8613800138000')).toBe(true)
  })

  it('account rule enforces max length', () => {
    const chain = rules.account({ min: 1, max: 5 })
    expect(runValidator(chain[1]!, 'abcdef')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'abc')).toBe(true)
  })

  it('emailOrPhone accepts either an email or a phone, rejects neither', () => {
    const chain = rules.emailOrPhone()
    expect(runValidator(chain[1]!, 'user@example.com')).toBe(true)
    expect(runValidator(chain[1]!, '+8613800138000')).toBe(true)
    expect(runValidator(chain[1]!, 'not-an-account!')).toBeInstanceOf(Error)
  })

  it('url regex requires http/https', () => {
    const regex = rules.url[1] as { pattern: RegExp }
    expect(regex.pattern.test('https://tnzi.cc')).toBe(true)
    expect(regex.pattern.test('http://localhost:5175')).toBe(true)
    expect(regex.pattern.test('tnzi.cc')).toBe(false)
    expect(regex.pattern.test('ftp://x')).toBe(false)
  })

  it('json validator accepts valid JSON', () => {
    expect(runValidator(rules.json[0]!, '{"a":1}')).toBe(true)
    expect(runValidator(rules.json[0]!, '[1,2,3]')).toBe(true)
    expect(runValidator(rules.json[0]!, '')).toBe(true) // empty is allowed
  })

  it('json validator rejects malformed JSON', () => {
    expect(runValidator(rules.json[0]!, '{a:1}')).toBeInstanceOf(Error)
    expect(runValidator(rules.json[0]!, 'not json')).toBeInstanceOf(Error)
  })

  it('integer enforces range', () => {
    const chain = rules.integer({ min: 1, max: 100 })
    expect(runValidator(chain[1]!, 0)).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 101)).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 50)).toBe(true)
  })

  it('integer rejects non-integers', () => {
    const chain = rules.integer()
    expect(runValidator(chain[1]!, 1.5)).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'abc')).toBeInstanceOf(Error)
  })

  it('translate prop substitutes messages', () => {
    const { rules: localized } = useFormRules((_k, fb) => `localized:${fb}`)
    const rule = localized.required() as { validator?: (r: unknown, v: unknown) => Error | true }
    const result = rule.validator!({}, '')
    expect((result as Error).message).toContain('localized:')
  })
})
