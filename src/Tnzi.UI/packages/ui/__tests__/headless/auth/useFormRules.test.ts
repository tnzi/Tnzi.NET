import { describe, it, expect } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount } from '@vue/test-utils'
import type { IFormRule } from '@tnzi/core/types'
import {
  useFormRules,
  usePasswordRequirements,
  PASSWORD_POLICY_KEY,
  DEFAULT_PASSWORD_POLICY,
} from '../../../src/headless/auth/useFormRules'

/**
 * Run a rule's own check the way every form library's adapter does: with the
 * field's value alone. `true` passes; a string is the message it fails with.
 */
function runValidator(rule: IFormRule, value: unknown): true | string {
  if (!rule.validator) return true
  const out = rule.validator(value)
  if (out instanceof Promise) throw new Error('the login stack validators are synchronous')
  if (out === true) return true
  return typeof out === 'string' ? out : (rule.message ?? 'failed')
}

const FAILS = expect.any(String)

describe('useFormRules', () => {
  const { rules } = useFormRules()

  it('required is the required flag with its message, and no check of its own', () => {
    expect(rules.required()).toEqual({ required: true, trigger: ['blur', 'input'], message: 'This field is required' })
    expect(rules.required('Pick one').message).toBe('Pick one')
  })

  it('text rule chain has required + length', () => {
    const chain = rules.text({ min: 2, max: 5 })
    expect(chain.length).toBe(2)
    expect(runValidator(chain[1]!, 'a')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'abc')).toBe(true)
    expect(runValidator(chain[1]!, 'abcdef')).toEqual(FAILS)
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

  // The server's default policy requires a digit AND a lowercase letter. The
  // rule used to ask for "letters and digits", so `ABCDEF12` passed the form
  // and was refused by the server.
  it('password rule follows the server default policy: a lowercase letter and a digit', () => {
    const chain = rules.password()
    expect(runValidator(chain[1]!, 'onlylett')).toEqual(FAILS)
    expect(runValidator(chain[1]!, '12345678')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'ABCDEF12')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'Admin123')).toBe(true)
    expect(runValidator(chain[1]!, 'admin123')).toBe(true)
  })

  it('password rule says which character class is missing', () => {
    const chain = rules.password()
    expect((runValidator(chain[1]!, 'ABCDEF12') as string)).toMatch(/lowercase/)
    expect((runValidator(chain[1]!, 'abcdefgh') as string)).toMatch(/digit/)
  })

  it('password rule applies the classes as the server register path does (ASCII letters)', () => {
    const chain = rules.password()
    // `é` is a lowercase letter, but not one ASP.NET Identity's validator counts.
    expect(runValidator(chain[1]!, 'ÉCOLE12é')).toEqual(FAILS)
  })

  it('password rule takes a deployment policy', () => {
    const upper = rules.password({ policy: { requireUppercase: true } })
    expect(runValidator(upper[1]!, 'admin123')).toEqual(FAILS)
    expect(runValidator(upper[1]!, 'Admin123')).toBe(true)

    const symbol = rules.password({ policy: { requireNonAlphanumeric: true } })
    expect(runValidator(symbol[1]!, 'Admin123')).toEqual(FAILS)
    // A letter in any script is a letter, not the "other" character the policy asks for.
    expect(runValidator(symbol[1]!, 'Admin12é')).toEqual(FAILS)
    expect(runValidator(symbol[1]!, 'Admin12!')).toBe(true)

    const relaxed = rules.password({ policy: { requireDigit: false, requireLowercase: false, minLength: 10 } })
    expect(runValidator(relaxed[1]!, 'ABCDEFGHI')).toEqual(FAILS)
    expect(runValidator(relaxed[1]!, 'ABCDEFGHIJ')).toBe(true)
  })

  it('password rule `min` still overrides the policy length alone', () => {
    const chain = rules.password({ min: 8, policy: { minLength: 12 } })
    expect(runValidator(chain[1]!, 'abcdef12')).toBe(true)
  })

  it('password rule follows a policy the application provides', () => {
    let built: IFormRule[] = []
    const Probe = defineComponent({
      setup() {
        built = useFormRules().rules.password()
        return () => h('div')
      },
    })
    mount(Probe, { global: { provide: { [PASSWORD_POLICY_KEY as symbol]: { requireUppercase: true } } } })
    expect(runValidator(built[1]!, 'admin123')).toEqual(FAILS)
    expect(runValidator(built[1]!, 'Admin123')).toBe(true)
    expect(DEFAULT_PASSWORD_POLICY).toEqual({
      minLength: 6, requireDigit: true, requireLowercase: true, requireUppercase: false, requireNonAlphanumeric: false,
    })
  })

  // The defaults must never refuse what the backend's default policy accepts
  // (minimum 6, no maximum): a 40-character password-manager password was
  // rejected client-side by an 8-32 range on register / reset / forced change,
  // while the login form (and the server) took up to 128.
  it('password rule defaults to the backend default range of 6 to 128', () => {
    const chain = rules.password()
    expect(runValidator(chain[1]!, 'abc12')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'abc123')).toBe(true)
    expect(runValidator(chain[1]!, `${'a'.repeat(38)}12`)).toBe(true)
    expect(runValidator(chain[1]!, `${'a'.repeat(127)}1`)).toBe(true)
    expect(runValidator(chain[1]!, `${'a'.repeat(128)}1`)).toEqual(FAILS)
  })

  it('password rule names the minimum when too short and the maximum when too long', () => {
    const chain = rules.password({ min: 6, max: 12 })
    expect(runValidator(chain[1]!, 'abc12')).toBe('Password must be at least 6 characters')
    expect(runValidator(chain[1]!, 'abc12345678901234')).toBe('Password must be no more than 12 characters')
  })

  it('password rule length messages go through the translator with {min} / {max}', () => {
    const { rules: localized } = useFormRules((key, fallback) =>
      key === 'admin.rules.passwordTooShort' ? '至少 {min} 位' : key === 'admin.rules.passwordTooLong' ? '最多 {max} 位' : fallback ?? key)
    const chain = localized.password({ max: 8 })
    expect(runValidator(chain[1]!, 'a1')).toBe('至少 6 位')
    expect(runValidator(chain[1]!, 'abcdefgh1')).toBe('最多 8 位')
  })

  it('password rule enforces length range', () => {
    const chain = rules.password({ min: 6, max: 12 })
    expect(runValidator(chain[1]!, 'abc12')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'abc12345678901234')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'abc123')).toBe(true)
  })

  it('matches rule passes when values are equal', () => {
    const rule = rules.matches(() => 'secret')
    expect(runValidator(rule, 'secret')).toBe(true)
    expect(runValidator(rule, 'wrong')).toEqual(FAILS)
  })

  it('account rule accepts username, email, or phone (length only)', () => {
    const chain = rules.account()
    expect(runValidator(chain[1]!, 'admin')).toBe(true)
    expect(runValidator(chain[1]!, 'user@example.com')).toBe(true)
    expect(runValidator(chain[1]!, '+8613800138000')).toBe(true)
  })

  it('account rule enforces max length', () => {
    const chain = rules.account({ min: 1, max: 5 })
    expect(runValidator(chain[1]!, 'abcdef')).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'abc')).toBe(true)
  })

  it('emailOrPhone accepts either an email or a phone, rejects neither', () => {
    const chain = rules.emailOrPhone()
    expect(runValidator(chain[1]!, 'user@example.com')).toBe(true)
    expect(runValidator(chain[1]!, '+8613800138000')).toBe(true)
    expect(runValidator(chain[1]!, 'not-an-account!')).toEqual(FAILS)
  })

  it('url regex requires http/https', () => {
    const regex = rules.url[1] as { pattern: RegExp }
    expect(regex.pattern.test('https://tnzi.cc')).toBe(true)
    expect(regex.pattern.test('http://localhost:5175')).toBe(true)
    expect(regex.pattern.test('tnzi.cc')).toBe(false)
    expect(regex.pattern.test('ftp://x')).toBe(false)
  })

  it('json is required, then checked as JSON', () => {
    expect(rules.json[0]).toMatchObject({ required: true, message: 'This field is required' })
    expect(runValidator(rules.json[1]!, '{"a":1}')).toBe(true)
    expect(runValidator(rules.json[1]!, '[1,2,3]')).toBe(true)
    // An empty value is the required rule's to refuse, not the JSON check's.
    expect(runValidator(rules.json[1]!, '')).toBe(true)
  })

  it('json validator rejects malformed JSON', () => {
    expect(runValidator(rules.json[1]!, '{a:1}')).toEqual(FAILS)
    expect(runValidator(rules.json[1]!, 'not json')).toEqual(FAILS)
  })

  it('integer enforces range', () => {
    const chain = rules.integer({ min: 1, max: 100 })
    expect(runValidator(chain[1]!, 0)).toEqual(FAILS)
    expect(runValidator(chain[1]!, 101)).toEqual(FAILS)
    expect(runValidator(chain[1]!, 50)).toBe(true)
  })

  it('integer rejects non-integers', () => {
    const chain = rules.integer()
    expect(runValidator(chain[1]!, 1.5)).toEqual(FAILS)
    expect(runValidator(chain[1]!, 'abc')).toEqual(FAILS)
  })

  describe('required fields (a whitespace-only value counts as empty: the backend [Required] refuses it)', () => {
    // `IFormRule.required` counts blank text as missing wherever the rule is
    // applied, so a required field needs nothing beyond the flag and its
    // message. What the form libraries make of it is tested where the rules
    // are translated (`@tnzi/ui-admin`'s useNaiveFormRules runs them through a
    // real NForm).
    it.each([
      ['phone', () => rules.phone, 'Phone is required'],
      ['password (login, no complexity)', () => rules.password({ complexity: false }), 'Password is required'],
      ['password', () => rules.password(), 'Password is required'],
      ['userName', () => rules.userName, 'Username is required'],
      ['email', () => rules.email, 'Email is required'],
      ['url', () => rules.url, 'URL is required'],
      ['account', () => rules.account(), 'Account is required'],
      ['emailOrPhone', () => rules.emailOrPhone(), 'This field is required'],
      ['text', () => rules.text(), 'This field is required'],
      ['integer', () => rules.integer(), 'This field is required'],
      ['required', () => [rules.required()], 'This field is required'],
    ] as const)('%s leads with a required rule carrying its message', (_name, chain, message) => {
      expect(chain()[0]).toMatchObject({ required: true, message })
    })

    // An empty field is one condition and gets one message: the checks after
    // the required rule leave a blank value to it.
    it.each([
      ['password', () => rules.password()],
      ['password (login, no complexity)', () => rules.password({ complexity: false })],
      ['account', () => rules.account()],
      ['text', () => rules.text({ min: 2 })],
    ] as const)('%s leaves a blank value to the required rule', (_name, chain) => {
      for (const value of ['', '   ', undefined, null]) {
        for (const rule of chain().slice(1)) expect(runValidator(rule, value)).toBe(true)
      }
    })

    it.each([
      ['password (login, no complexity)', () => rules.password({ complexity: false }), ' secret '],
      ['account', () => rules.account(), ' admin '],
      ['text', () => rules.text(), ' x '],
    ] as const)('%s still accepts a real value with surrounding spaces', (_name, chain, value) => {
      for (const rule of chain()) expect(runValidator(rule, value)).toBe(true)
    })
  })

  describe('the rule shape every form library reads', () => {
    const all = (): Array<[string, IFormRule[]]> => [
      ['required', [rules.required()]],
      ['text', rules.text()],
      ['userName', rules.userName],
      ['email', rules.email],
      ['phone', rules.phone],
      ['password', rules.password()],
      ['account', rules.account()],
      ['emailOrPhone', rules.emailOrPhone()],
      ['matches', [rules.matches(() => 'x')]],
      ['url', rules.url],
      ['json', rules.json],
      ['integer', rules.integer()],
    ]

    it('checks while typing and again on leaving the field (an array of triggers)', () => {
      for (const [name, chain] of all()) {
        for (const rule of chain) {
          expect(Array.isArray(rule.trigger), name).toBe(true)
          expect(rule.trigger, name).toContain('blur')
        }
      }
    })

    it('hands each validator the value alone, and gets back true or a message', () => {
      for (const [name, chain] of all()) {
        for (const rule of chain.filter((r) => r.validator)) {
          // One declared parameter: no library rule object in front of the value.
          expect(rule.validator!.length, name).toBe(1)
          const out = rule.validator!('\u0000 not a valid anything \u0000')
          expect(out === true || typeof out === 'string', name).toBe(true)
        }
      }
    })

    it('carries nothing a single form library invented (no whitespace / key / asyncValidator)', () => {
      const allowed = new Set(['required', 'message', 'trigger', 'min', 'max', 'pattern', 'validator'])
      for (const [name, chain] of all()) {
        for (const rule of chain) {
          expect(Object.keys(rule).filter((k) => !allowed.has(k)), name).toEqual([])
        }
      }
    })
  })

  it('translate prop substitutes messages', () => {
    const { rules: localized } = useFormRules((_k, fb) => `localized:${fb}`)
    expect(localized.required().message).toContain('localized:')
    expect(runValidator(localized.emailOrPhone()[1]!, 'neither')).toContain('localized:')
  })
})

describe('usePasswordRequirements', () => {
  it('states the default policy: the minimum and the classes it requires', () => {
    expect(usePasswordRequirements()()).toBe('Use at least 6 characters, including a lowercase letter (a-z) and a digit (0-9).')
  })

  it('states the minimum alone when no class is required', () => {
    expect(usePasswordRequirements()({ complexity: false })).toBe('Use at least 6 characters.')
    expect(usePasswordRequirements()({ policy: { minLength: 10, requireDigit: false, requireLowercase: false } }))
      .toBe('Use at least 10 characters.')
  })

  it('lists every required class, in the order the rule checks them', () => {
    const all = { requireUppercase: true, requireNonAlphanumeric: true }
    expect(usePasswordRequirements()({ policy: all })).toBe(
      'Use at least 6 characters, including an uppercase letter (A-Z), a lowercase letter (a-z), a digit (0-9)'
        + ' and a character that is not a letter or digit.',
    )
  })

  it('reads the policy the application provides, as password() does', () => {
    let hint = ''
    const Probe = defineComponent({
      setup() {
        hint = usePasswordRequirements()()
        return () => h('div')
      },
    })
    mount(Probe, { global: { provide: { [PASSWORD_POLICY_KEY as symbol]: { minLength: 8, requireDigit: false } } } })
    expect(hint).toBe('Use at least 8 characters, including a lowercase letter (a-z).')
  })

  it('builds the sentence from translated parts', () => {
    const zh: Record<string, string> = {
      'admin.rules.passwordRequirements.lengthAndClasses': '密码至少 {min} 个字符，并包含{classes}。',
      'admin.rules.passwordRequirements.listSeparator': '、',
      'admin.rules.passwordRequirements.listLast': '{rest}和{last}',
      'admin.rules.passwordClass.uppercase': '大写字母',
      'admin.rules.passwordClass.lowercase': '小写字母',
      'admin.rules.passwordClass.digit': '数字',
    }
    const hint = usePasswordRequirements((key, fallback) => zh[key] ?? fallback ?? key)
    expect(hint({ policy: { requireUppercase: true } })).toBe('密码至少 6 个字符，并包含大写字母、小写字母和数字。')
  })
})
