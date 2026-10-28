import { describe, it, expect } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { NForm, NFormItem, type FormInst, type FormItemRule } from 'naive-ui'
import { useFormRules, PASSWORD_POLICY_KEY, DEFAULT_PASSWORD_POLICY } from '../../../src/headless/auth/useFormRules'

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

  // The server's default policy requires a digit AND a lowercase letter. The
  // rule used to ask for "letters and digits", so `ABCDEF12` passed the form
  // and was refused by the server.
  it('password rule follows the server default policy: a lowercase letter and a digit', () => {
    const chain = rules.password()
    expect(runValidator(chain[1]!, 'onlylett')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, '12345678')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'ABCDEF12')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'Admin123')).toBe(true)
    expect(runValidator(chain[1]!, 'admin123')).toBe(true)
  })

  it('password rule says which character class is missing', () => {
    const chain = rules.password()
    expect((runValidator(chain[1]!, 'ABCDEF12') as Error).message).toMatch(/lowercase/)
    expect((runValidator(chain[1]!, 'abcdefgh') as Error).message).toMatch(/digit/)
  })

  it('password rule applies the classes as the server register path does (ASCII letters)', () => {
    const chain = rules.password()
    // `é` is a lowercase letter, but not one ASP.NET Identity's validator counts.
    expect(runValidator(chain[1]!, 'ÉCOLE12é')).toBeInstanceOf(Error)
  })

  it('password rule takes a deployment policy', () => {
    const upper = rules.password({ policy: { requireUppercase: true } })
    expect(runValidator(upper[1]!, 'admin123')).toBeInstanceOf(Error)
    expect(runValidator(upper[1]!, 'Admin123')).toBe(true)

    const symbol = rules.password({ policy: { requireNonAlphanumeric: true } })
    expect(runValidator(symbol[1]!, 'Admin123')).toBeInstanceOf(Error)
    // A letter in any script is a letter, not the "other" character the policy asks for.
    expect(runValidator(symbol[1]!, 'Admin12é')).toBeInstanceOf(Error)
    expect(runValidator(symbol[1]!, 'Admin12!')).toBe(true)

    const relaxed = rules.password({ policy: { requireDigit: false, requireLowercase: false, minLength: 10 } })
    expect(runValidator(relaxed[1]!, 'ABCDEFGHI')).toBeInstanceOf(Error)
    expect(runValidator(relaxed[1]!, 'ABCDEFGHIJ')).toBe(true)
  })

  it('password rule `min` still overrides the policy length alone', () => {
    const chain = rules.password({ min: 8, policy: { minLength: 12 } })
    expect(runValidator(chain[1]!, 'abcdef12')).toBe(true)
  })

  it('password rule follows a policy the application provides', () => {
    let built: FormItemRule[] = []
    const Probe = defineComponent({
      setup() {
        built = useFormRules().rules.password()
        return () => h('div')
      },
    })
    mount(Probe, { global: { provide: { [PASSWORD_POLICY_KEY as symbol]: { requireUppercase: true } } } })
    expect(runValidator(built[1]!, 'admin123')).toBeInstanceOf(Error)
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
    expect(runValidator(chain[1]!, 'abc12')).toBeInstanceOf(Error)
    expect(runValidator(chain[1]!, 'abc123')).toBe(true)
    expect(runValidator(chain[1]!, `${'a'.repeat(38)}12`)).toBe(true)
    expect(runValidator(chain[1]!, `${'a'.repeat(127)}1`)).toBe(true)
    expect(runValidator(chain[1]!, `${'a'.repeat(128)}1`)).toBeInstanceOf(Error)
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

  describe('whitespace-only input counts as empty (the backend [Required] refuses it)', () => {
    // Run the rule chain through a real NForm: `required` / `whitespace` are
    // read by the form's validator, not by any function on the rule.
    async function validationErrors(chain: FormItemRule | FormItemRule[], value: string): Promise<string[]> {
      const formRef = ref<FormInst | null>(null)
      const wrapper = mount(defineComponent({
        setup: () => () =>
          h(NForm, { ref: formRef, model: { v: value }, rules: { v: chain } }, () =>
            h(NFormItem, { path: 'v', label: 'V' }, () => h('span'))),
      }))
      try {
        await formRef.value!.validate()
        return []
      } catch (errors) {
        return (errors as Array<Array<{ message?: string }>>).flat().map((e) => e.message ?? '')
      } finally {
        wrapper.unmount()
      }
    }

    it.each([
      ['phone', () => rules.phone, '       ', 'Phone is required'],
      ['password (login, no complexity)', () => rules.password({ complexity: false }), '        ', 'Password is required'],
      ['password', () => rules.password(), '        ', 'Password is required'],
      ['userName', () => rules.userName, '     ', 'Username is required'],
      ['email', () => rules.email, '   ', 'Email is required'],
      ['url', () => rules.url, '   ', 'URL is required'],
      ['account', () => rules.account(), '   ', 'Account is required'],
      ['emailOrPhone', () => rules.emailOrPhone(), '        ', 'This field is required'],
      ['text', () => rules.text(), '   ', 'This field is required'],
      ['required', () => rules.required(), '   ', 'This field is required'],
    ] as const)('%s rejects a whitespace-only value with its required message', async (_name, chain, blank, message) => {
      expect((await validationErrors(chain(), blank))[0]).toBe(message)
    })

    it.each([
      ['phone', () => rules.phone, '+1 555 123 4567'],
      ['password (login, no complexity)', () => rules.password({ complexity: false }), ' secret '],
      ['account', () => rules.account(), ' admin '],
    ] as const)('%s still accepts a real value with surrounding spaces', async (_name, chain, value) => {
      expect(await validationErrors(chain(), value)).toEqual([])
    })
  })

  it('translate prop substitutes messages', () => {
    const { rules: localized } = useFormRules((_k, fb) => `localized:${fb}`)
    const rule = localized.required() as { validator?: (r: unknown, v: unknown) => Error | true }
    const result = rule.validator!({}, '')
    expect((result as Error).message).toContain('localized:')
  })
})
