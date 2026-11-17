/**
 * `useNaiveFormRules` / `useNaiveLoginAccountField` against the rules the login
 * pages used before the login stack's rules became library-neutral.
 *
 * `LEGACY` below is a frozen copy of the `FormItemRule`s `@tnzi/ui`'s
 * `useFormRules` used to build itself (naive's `(rule, value)` validators
 * returning `Error`, `whitespace: true`), limited to the rules the built-in
 * login, register, password-reset, pending-action and invitation pages use.
 * Both sets run through a real `NForm`, on every trigger the pages validate on
 * and with values that hit each branch: the form must say exactly what it said
 * before, with the same required mark and the same triggers.
 *
 * Two deliberate departures are written into the reference: a blank value is
 * left to the required rule (one message for an empty field, not a length
 * error beside it), and the password length error names the bound that was
 * missed ("at least 6 characters", "no more than 128 characters").
 */
import { describe, it, expect } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { mount } from '@vue/test-utils'
import { NForm, NFormItem, type FormItemRule } from 'naive-ui'
import { useNaiveFormRules, useNaiveLoginAccountField } from '../../src/headless/useNaiveFormRules'

const translate = (_key: string, fallback?: string) => fallback ?? _key

// ---------------------------------------------------------------------------
// The rules as they were (frozen; do not "fix" them, they are the reference).
// ---------------------------------------------------------------------------

const EMAIL = /^[\w.+-]+@[a-z0-9-]+(\.[a-z0-9-]+)+$/i
const PHONE = /^\+?[\d ()-]{7,20}$/
const BOTH: FormItemRule['trigger'] = ['blur', 'input']

function legacyLength(min: number, max: number): FormItemRule {
  return {
    trigger: BOTH,
    validator(_rule: unknown, value: string) {
      const len = (value ?? '').trim().length
      if (len === 0) return true
      return len < min || len > max ? new Error(`Length must be ${min}-${max}`) : true
    },
  }
}

const LEGACY = {
  account: (): FormItemRule[] => [
    { required: true, whitespace: true, trigger: BOTH, message: 'Account is required' },
    legacyLength(1, 256),
  ],
  password: (opts: { min?: number; max?: number; complexity?: boolean } = {}): FormItemRule[] => {
    const min = opts.min ?? 6
    const max = opts.max ?? 128
    const classes = opts.complexity ?? true
      ? [
          { pattern: /[a-z]/, message: 'Password must contain a lowercase letter (a-z)' },
          { pattern: /[0-9]/, message: 'Password must contain a digit (0-9)' },
        ]
      : []
    return [
      { required: true, whitespace: true, trigger: BOTH, message: 'Password is required' },
      {
        trigger: BOTH,
        validator(_rule: unknown, value: string) {
          const v = value ?? ''
          if (v.trim() === '') return true
          if (v.length < min) return new Error(`Password must be at least ${min} characters`)
          if (v.length > max) return new Error(`Password must be no more than ${max} characters`)
          const missing = classes.find((c) => !c.pattern.test(v))
          return missing ? new Error(missing.message) : true
        },
      },
    ]
  },
  matches: (other: () => string, message: string): FormItemRule => ({
    trigger: BOTH,
    validator(_rule: unknown, value: string) {
      return value !== other() ? new Error(message) : true
    },
  }),
  phone: (): FormItemRule[] => [
    { required: true, whitespace: true, trigger: BOTH, message: 'Phone is required' },
    { pattern: PHONE, trigger: BOTH, message: 'Invalid phone number' },
  ],
  email: (): FormItemRule[] => [
    { required: true, whitespace: true, trigger: BOTH, message: 'Email is required' },
    { pattern: EMAIL, trigger: BOTH, message: 'Invalid email address' },
  ],
  emailOrPhone: (message: string): FormItemRule[] => [
    { required: true, whitespace: true, trigger: BOTH, message: 'This field is required' },
    {
      trigger: BOTH,
      validator(_rule: unknown, value: string) {
        const v = (value ?? '').trim()
        return EMAIL.test(v) || PHONE.test(v) ? true : new Error(message)
      },
    },
  ],
}

const legacyAccountField = (channels: { sms: boolean; email: boolean }): FormItemRule[] => {
  const base = channels.sms && !channels.email
    ? LEGACY.phone()
    : channels.email && !channels.sms
      ? LEGACY.email()
      : LEGACY.emailOrPhone('Enter a valid email or phone number')
  return base.map((rule) => ({ ...rule, key: 'account' }))
}

// ---------------------------------------------------------------------------
// Harness: one field, a real NForm, validated on one trigger or on submit.
// ---------------------------------------------------------------------------

type Trigger = 'blur' | 'input' | 'change' | 'submit'
const TRIGGERS: Trigger[] = ['submit', 'blur', 'input', 'change']

async function errorsFor(rules: FormItemRule[], value: unknown, trigger: Trigger): Promise<string[]> {
  const itemRef = ref<{ validate: (o: { trigger?: string }) => Promise<unknown> } | null>(null)
  const wrapper = mount(defineComponent({
    setup: () => () =>
      h(NForm, { model: { v: value }, rules: { v: rules } }, () =>
        h(NFormItem, { ref: itemRef, path: 'v', label: 'V' }, () => h('span'))),
  }))
  try {
    await itemRef.value!.validate(trigger === 'submit' ? {} : { trigger })
    return []
  } catch (errors) {
    return (errors as Array<Array<{ message?: string }>>).flat().map((e) => e.message ?? '')
  } finally {
    wrapper.unmount()
  }
}

/** The parts of a rule naive reads besides its checks: the required mark, the triggers, the key. */
function shape(rules: FormItemRule[]) {
  return rules.map((r) => ({ required: r.required === true, trigger: r.trigger, key: r.key }))
}

async function expectSameBehaviour(before: FormItemRule[], after: FormItemRule[], values: unknown[]) {
  expect(shape(after)).toEqual(shape(before))
  for (const value of values) {
    for (const trigger of TRIGGERS) {
      const expected = await errorsFor(before, value, trigger)
      expect(await errorsFor(after, value, trigger), `${JSON.stringify(value)} on ${trigger}`).toEqual(expected)
    }
  }
}

const BLANKS = ['', '   ', ' \t ']

describe('useNaiveFormRules gives the login pages the rules they had', () => {
  const { rules } = useNaiveFormRules(translate)

  it('account (password login: username, email or phone)', async () => {
    await expectSameBehaviour(LEGACY.account(), rules.account(), [...BLANKS, 'admin', ' admin ', 'a'.repeat(257)])
  })

  it('password for login (no complexity, 1-128)', async () => {
    const opts = { min: 1, max: 128, complexity: false }
    await expectSameBehaviour(LEGACY.password(opts), rules.password(opts), [...BLANKS, 'x', ' secret ', 'a'.repeat(129)])
  })

  it('password for register / reset / invitation (server default policy)', async () => {
    await expectSameBehaviour(LEGACY.password(), rules.password(), [...BLANKS, 'abc12', 'abcdefgh', 'ABCDEF12', '12345678', 'admin123'])
  })

  it('an empty or blank password field shows exactly one message, the required one', async () => {
    for (const value of BLANKS) {
      for (const trigger of TRIGGERS) {
        expect(await errorsFor(rules.password(), value, trigger), `${JSON.stringify(value)} on ${trigger}`)
          .toEqual(trigger === 'change' ? [] : ['Password is required'])
      }
    }
  })

  it('matches (confirm password)', async () => {
    const message = 'Passwords do not match'
    await expectSameBehaviour(
      [LEGACY.matches(() => 'secret1', message)],
      [rules.matches(() => 'secret1', message)],
      ['', 'secret1', 'secret2'],
    )
  })

  it.each([
    ['both channels', { sms: true, email: true }],
    ['SMS only', { sms: true, email: false }],
    ['email only', { sms: false, email: true }],
  ])('the code-login / register / reset account field, %s', async (_name, channels) => {
    const { rule } = useNaiveLoginAccountField(translate, () => channels)
    await expectSameBehaviour(legacyAccountField(channels), rule.value, [
      ...BLANKS,
      'user@example.com',
      ' user@example.com ',
      '+1 555 123 4567',
      'not-an-account!',
    ])
  })
})

describe('useNaiveFormRules', () => {
  it('keeps every member of FormRules, as a rule set or a factory', () => {
    const { rules } = useNaiveFormRules(translate)
    expect(Array.isArray(rules.email)).toBe(true)
    expect(typeof rules.password).toBe('function')
    expect(Object.keys(rules).sort()).toEqual(
      ['account', 'email', 'emailOrPhone', 'integer', 'json', 'matches', 'password', 'phone', 'required', 'text', 'url', 'userName'],
    )
  })

  it('passes the factories their arguments', async () => {
    const { rules } = useNaiveFormRules(translate)
    expect(await errorsFor(rules.account({ max: 3 }), 'abcd', 'submit')).toEqual(['Length must be 1-3'])
    expect(await errorsFor([rules.required('Pick one')], '  ', 'submit')).toEqual(['Pick one'])
  })

  it('takes the translator', async () => {
    const { rules } = useNaiveFormRules((_key, fallback) => `localized:${fallback}`)
    expect(await errorsFor(rules.email, '', 'submit')).toEqual(['localized:Email is required'])
  })

  it('tags the account field rules with key "account", so naive\'s rule predicate can pick them', () => {
    const { rule } = useNaiveLoginAccountField(translate, () => ({ sms: true, email: true }))
    expect(rule.value.length).toBeGreaterThan(0)
    expect(rule.value.every((r) => r.key === 'account')).toBe(true)
  })
})
