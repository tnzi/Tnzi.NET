/**
 * `useFormRules()` - form validation rules for Naive UI's `NForm`, with
 * messages from an optional translator. Returns a set of `FormItemRule[]`
 * keyed by common field names; consumers pick the ones they need. Messages
 * of the rule factories (`password()`, `text()`, ...) are built when called,
 * those of the fixed arrays (`email`, `phone`, `userName`, `url`) when the
 * hook is called, so build the rules where the active locale is known.
 *
 * @example
 * ```vue
 * const { rules } = useFormRules(t)
 * const formRules = { email: rules.email, password: rules.password() }
 * ```
 *
 * All rules accept a `translate` function. When omitted, returns
 * English fallback messages.
 *
 * Every required rule counts a whitespace-only string as empty
 * (`whitespace: true`, or a trimming validator): the backend's [Required]
 * refuses such a string, so passing it here only moves the error to a 400.
 */

import { hasInjectionContext, inject, type InjectionKey } from 'vue'
import type { FormItemRule } from 'naive-ui'

export type Translate = (key: string, fallback?: string) => string

function fb(t: Translate | undefined, key: string, fallback: string): string {
  if (!t) return fallback
  const result = t(key, fallback)
  return result === key && fallback ? fallback : result
}

export interface FormRules {
  /** Required string - non-empty after trim. */
  required(message?: string): FormItemRule
  /** Required + length range. */
  text(opts?: { min?: number; max?: number; message?: string }): FormItemRule[]
  /** Username - 3-32 chars, letters/digits/dash/underscore. */
  userName: FormItemRule[]
  /** Email - RFC 5322 light. */
  email: FormItemRule[]
  /** International phone number - digits + optional `+` and dashes. */
  phone: FormItemRule[]
  /**
   * A new password, checked against the server's password policy
   * (`Identity:PasswordPolicy`): its minimum length and the character classes
   * it requires, up to the 128-character ceiling the login form and the user
   * DTOs use. The policy is the server's default unless the application
   * provides its own (`PASSWORD_POLICY_KEY`) or the call passes one; `min`
   * still overrides the length alone. The backend enforces the configured rule
   * either way: this check only spares the user a round trip, and it must not
   * accept a password the server would refuse.
   *
   * Pass `complexity: false` for LOGIN forms, where the password must only be
   * non-empty: the login form must not reject a valid existing password
   * client-side.
   */
  password(opts?: { min?: number; max?: number; complexity?: boolean; policy?: Partial<PasswordPolicy> }): FormItemRule[]
  /**
   * Account identifier - username, email, OR phone. Length-only (no strict
   * format) so a login form accepts whatever the backend allows. Used by the
   * password-login account field.
   */
  account(opts?: { min?: number; max?: number; message?: string }): FormItemRule[]
  /**
   * Email or phone - accepts either format in a single field (the consumer
   * auto-detects which `type` to send). Used by code-login / register /
   * password-recovery.
   */
  emailOrPhone(message?: string): FormItemRule[]
  /** Match another field value (e.g. confirm-password). */
  matches(other: () => string, message?: string): FormItemRule
  /** Valid URL (http/https only). */
  url: FormItemRule[]
  /** Valid JSON string. */
  json: FormItemRule[]
  /** Integer in range. */
  integer(opts?: { min?: number; max?: number }): FormItemRule[]
}

/**
 * The server's password policy, field for field (`Identity:PasswordPolicy`:
 * `MinLength`, `RequireDigit`, `RequireLowercase`, `RequireUppercase`,
 * `RequireNonAlphanumeric`).
 */
export interface PasswordPolicy {
  minLength: number
  requireDigit: boolean
  requireLowercase: boolean
  requireUppercase: boolean
  requireNonAlphanumeric: boolean
}

/**
 * The policy a server enforces when `Identity:PasswordPolicy` is not configured.
 * A form that checked less than this passed passwords the server then refused
 * (`ABCDEF12` has letters and digits and no lowercase letter).
 */
export const DEFAULT_PASSWORD_POLICY: Readonly<PasswordPolicy> = Object.freeze({
  minLength: 6,
  requireDigit: true,
  requireLowercase: true,
  requireUppercase: false,
  requireNonAlphanumeric: false,
})

/**
 * The deployment's password policy, for every `password()` rule built in the
 * application, the built-in register / reset / invitation pages included:
 * `app.provide(PASSWORD_POLICY_KEY, { requireUppercase: true })`, with the
 * values the server is configured with. Fields left out keep the server's
 * defaults. (`defineAdminApp({ passwordPolicy })` provides it for an admin app.)
 */
export const PASSWORD_POLICY_KEY: InjectionKey<Partial<PasswordPolicy>> = Symbol('tnzi-password-policy')

/** The ceiling the login form and the user DTOs use; the server sets no maximum. */
const DEFAULT_PASSWORD_MAX = 128

/**
 * The character classes the server tests for, in the order it reports them.
 * The classes are ASCII, as in ASP.NET Identity's validator, which is what the
 * server's register path runs: a password whose only lowercase letter is `é`
 * would pass a Unicode test here and be refused there. "Not a letter or digit"
 * counts every letter, in any script, as a letter, which is the stricter of the
 * server's two checks; so a password this accepts, both of them accept.
 */
const PASSWORD_CLASSES: ReadonlyArray<{
  readonly flag: keyof Omit<PasswordPolicy, 'minLength'>
  readonly pattern: RegExp
  readonly key: string
  readonly fallback: string
}> = [
  { flag: 'requireUppercase', pattern: /[A-Z]/, key: 'admin.rules.passwordUppercase', fallback: 'Password must contain an uppercase letter (A-Z)' },
  { flag: 'requireLowercase', pattern: /[a-z]/, key: 'admin.rules.passwordLowercase', fallback: 'Password must contain a lowercase letter (a-z)' },
  { flag: 'requireDigit', pattern: /[0-9]/, key: 'admin.rules.passwordDigit', fallback: 'Password must contain a digit (0-9)' },
  { flag: 'requireNonAlphanumeric', pattern: /[^\p{L}\p{Nd}]/u, key: 'admin.rules.passwordSymbol', fallback: 'Password must contain a character that is not a letter or digit' },
]

/** The provided policy, when this runs where the application can be asked (setup or `app.runWithContext`). */
function providedPasswordPolicy(): Partial<PasswordPolicy> | null {
  return hasInjectionContext() ? inject(PASSWORD_POLICY_KEY, null) : null
}

function emailRegex(): RegExp {
  return /^[\w.+-]+@[a-z0-9-]+(\.[a-z0-9-]+)+$/i
}
function phoneRegex(): RegExp {
  return /^\+?[\d ()-]{7,20}$/
}
function userNameRegex(): RegExp {
  return /^[a-zA-Z0-9_-]{3,32}$/
}
function urlRegex(): RegExp {
  return /^https?:\/\/[^\s]+$/i
}

export function useFormRules(translate?: Translate): { rules: FormRules } {
  const t = translate
  // Read once, where the hook is called: `inject` only works inside setup.
  const appPasswordPolicy = providedPasswordPolicy()

  const rules: FormRules = {
    required(message) {
      return {
        required: true,
        trigger: ['blur', 'input'],
        validator(_rule: unknown, value: unknown) {
          if (typeof value === 'string' && value.trim().length === 0) {
            return new Error(
              message ?? fb(t, 'admin.rules.required', 'This field is required'),
            )
          }
          if (value === undefined || value === null) {
            return new Error(
              message ?? fb(t, 'admin.rules.required', 'This field is required'),
            )
          }
          return true
        },
      }
    },

    text(opts = {}) {
      const min = opts.min ?? 1
      const max = opts.max ?? 255
      return [
        {
          required: true,
          whitespace: true,
          trigger: ['blur', 'input'],
          message: opts.message ?? fb(t, 'admin.rules.required', 'This field is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(_rule: unknown, value: string) {
            const len = (value ?? '').trim().length
            if (len < min || len > max) {
              return new Error(
                fb(t, 'admin.rules.lengthRange', `Length must be ${min}-${max}`)
                  .replace('{min}', String(min))
                  .replace('{max}', String(max)),
              )
            }
            return true
          },
        },
      ]
    },

    userName: [
      {
        required: true,
        whitespace: true,
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.required', 'Username is required'),
      },
      {
        pattern: userNameRegex(),
        trigger: ['blur', 'input'],
        message: fb(
          t,
          'admin.rules.userName',
          '3-32 chars, letters / digits / dash / underscore',
        ),
      },
    ],

    email: [
      {
        required: true,
        whitespace: true,
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.required', 'Email is required'),
      },
      {
        pattern: emailRegex(),
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.email', 'Invalid email address'),
      },
    ],

    phone: [
      {
        required: true,
        whitespace: true,
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.required', 'Phone is required'),
      },
      {
        pattern: phoneRegex(),
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.phone', 'Invalid phone number'),
      },
    ],

    password(opts = {}) {
      const policy: PasswordPolicy = { ...DEFAULT_PASSWORD_POLICY, ...appPasswordPolicy, ...opts.policy }
      const min = opts.min ?? policy.minLength
      const max = opts.max ?? DEFAULT_PASSWORD_MAX
      const complexity = opts.complexity ?? true
      const required = complexity ? PASSWORD_CLASSES.filter((c) => policy[c.flag]) : []
      return [
        {
          required: true,
          whitespace: true,
          trigger: ['blur', 'input'],
          message: fb(t, 'admin.rules.required', 'Password is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(_rule: unknown, value: string) {
            const v = value ?? ''
            if (v.length < min || v.length > max) {
              return new Error(
                fb(t, 'admin.rules.passwordLength', `Password must be ${min}-${max} chars`)
                  .replace('{min}', String(min))
                  .replace('{max}', String(max)),
              )
            }
            const missing = required.find((c) => !c.pattern.test(v))
            if (missing) return new Error(fb(t, missing.key, missing.fallback))
            return true
          },
        },
      ]
    },

    account(opts = {}) {
      const min = opts.min ?? 1
      const max = opts.max ?? 256
      return [
        {
          required: true,
          whitespace: true,
          trigger: ['blur', 'input'],
          message: opts.message ?? fb(t, 'admin.rules.required', 'Account is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(_rule: unknown, value: string) {
            const len = (value ?? '').trim().length
            if (len < min || len > max) {
              return new Error(
                fb(t, 'admin.rules.lengthRange', `Length must be ${min}-${max}`)
                  .replace('{min}', String(min))
                  .replace('{max}', String(max)),
              )
            }
            return true
          },
        },
      ]
    },

    emailOrPhone(message) {
      return [
        {
          required: true,
          whitespace: true,
          trigger: ['blur', 'input'],
          message: fb(t, 'admin.rules.required', 'This field is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(_rule: unknown, value: string) {
            const v = (value ?? '').trim()
            if (emailRegex().test(v) || phoneRegex().test(v)) return true
            return new Error(
              message ?? fb(t, 'admin.rules.emailOrPhone', 'Enter a valid email or phone number'),
            )
          },
        },
      ]
    },

    matches(other, message) {
      return {
        trigger: ['blur', 'input'],
        validator(_rule: unknown, value: string) {
          if (value !== other()) {
            return new Error(
              message ?? fb(t, 'admin.rules.matches', 'Values do not match'),
            )
          }
          return true
        },
      }
    },

    url: [
      {
        required: true,
        whitespace: true,
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.required', 'URL is required'),
      },
      {
        pattern: urlRegex(),
        trigger: ['blur', 'input'],
        message: fb(t, 'admin.rules.url', 'Must start with http:// or https://'),
      },
    ],

    json: [
      {
        required: true,
        trigger: ['blur'],
        validator(_rule: unknown, value: string) {
          if (!value) return true
          try {
            JSON.parse(value)
            return true
          } catch {
            return new Error(fb(t, 'admin.rules.json', 'Invalid JSON'))
          }
        },
      },
    ],

    integer(opts = {}) {
      const result: FormItemRule[] = [
        {
          required: true,
          trigger: ['blur', 'input'],
          message: fb(t, 'admin.rules.required', 'This field is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(_rule: unknown, value: unknown) {
            const n = typeof value === 'number' ? value : Number(value)
            if (!Number.isInteger(n)) {
              return new Error(fb(t, 'admin.rules.integer', 'Must be an integer'))
            }
            if (opts.min !== undefined && n < opts.min) {
              return new Error(
                fb(t, 'admin.rules.min', `Must be ≥ ${opts.min}`).replace(
                  '{min}',
                  String(opts.min),
                ),
              )
            }
            if (opts.max !== undefined && n > opts.max) {
              return new Error(
                fb(t, 'admin.rules.max', `Must be ≤ ${opts.max}`).replace(
                  '{max}',
                  String(opts.max),
                ),
              )
            }
            return true
          },
        },
      ]
      return result
    },
  }

  return { rules }
}
