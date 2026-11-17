/**
 * `useFormRules()` - form validation rules with messages from an optional
 * translator. Returns a set of `IFormRule[]` (`@tnzi/core/types`) keyed by
 * common field names; consumers pick the ones they need. The rules belong to
 * no component library: a package that renders forms translates them into its
 * own (`@tnzi/ui-admin`'s `useNaiveFormRules` for Naive UI's `NForm`). Messages
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
 * Each validator takes the field's value alone and answers `true`, or the
 * message to fail with.
 *
 * All rules accept a `translate` function. When omitted, returns
 * English fallback messages.
 *
 * Every required rule counts a whitespace-only string as empty, as
 * `IFormRule.required` does everywhere: the backend's [Required] refuses
 * such a string, so passing it here only moves the error to a 400.
 */

import { hasInjectionContext, inject, type InjectionKey } from 'vue'
import type { IFormRule } from '@tnzi/core/types'

export type Translate = (key: string, fallback?: string) => string

function fb(t: Translate | undefined, key: string, fallback: string): string {
  if (!t) return fallback
  const result = t(key, fallback)
  return result === key && fallback ? fallback : result
}

export interface FormRules {
  /** Required string - non-empty after trim. */
  required(message?: string): IFormRule
  /** Required + length range. */
  text(opts?: { min?: number; max?: number; message?: string }): IFormRule[]
  /** Username - 3-32 chars, letters/digits/dash/underscore. */
  userName: IFormRule[]
  /** Email - RFC 5322 light. */
  email: IFormRule[]
  /** International phone number - digits + optional `+` and dashes. */
  phone: IFormRule[]
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
  password(opts?: PasswordRuleOptions): IFormRule[]
  /**
   * Account identifier - username, email, OR phone. Length-only (no strict
   * format) so a login form accepts whatever the backend allows. Used by the
   * password-login account field.
   */
  account(opts?: { min?: number; max?: number; message?: string }): IFormRule[]
  /**
   * Email or phone - accepts either format in a single field (the consumer
   * auto-detects which `type` to send). Used by code-login / register /
   * password-recovery.
   */
  emailOrPhone(message?: string): IFormRule[]
  /** Match another field value (e.g. confirm-password). */
  matches(other: () => string, message?: string): IFormRule
  /** Valid URL (http/https only). */
  url: IFormRule[]
  /** Valid JSON string. */
  json: IFormRule[]
  /** Integer in range. */
  integer(opts?: { min?: number; max?: number }): IFormRule[]
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
  /** The class named on its own, for the requirements stated up front. */
  readonly nameKey: string
  readonly nameFallback: string
}> = [
  {
    flag: 'requireUppercase', pattern: /[A-Z]/,
    key: 'admin.rules.passwordUppercase', fallback: 'Password must contain an uppercase letter (A-Z)',
    nameKey: 'admin.rules.passwordClass.uppercase', nameFallback: 'an uppercase letter (A-Z)',
  },
  {
    flag: 'requireLowercase', pattern: /[a-z]/,
    key: 'admin.rules.passwordLowercase', fallback: 'Password must contain a lowercase letter (a-z)',
    nameKey: 'admin.rules.passwordClass.lowercase', nameFallback: 'a lowercase letter (a-z)',
  },
  {
    flag: 'requireDigit', pattern: /[0-9]/,
    key: 'admin.rules.passwordDigit', fallback: 'Password must contain a digit (0-9)',
    nameKey: 'admin.rules.passwordClass.digit', nameFallback: 'a digit (0-9)',
  },
  {
    flag: 'requireNonAlphanumeric', pattern: /[^\p{L}\p{Nd}]/u,
    key: 'admin.rules.passwordSymbol', fallback: 'Password must contain a character that is not a letter or digit',
    nameKey: 'admin.rules.passwordClass.symbol', nameFallback: 'a character that is not a letter or digit',
  },
]

/** What `password()` and `usePasswordRequirements()` take besides the translator. */
export interface PasswordRuleOptions {
  min?: number
  max?: number
  complexity?: boolean
  policy?: Partial<PasswordPolicy>
}

/** The checks a `password()` rule runs: the length range and the classes it requires. */
interface ResolvedPasswordRule {
  readonly min: number
  readonly max: number
  readonly classes: typeof PASSWORD_CLASSES
}

function resolvePasswordRule(appPolicy: Partial<PasswordPolicy> | null, opts: PasswordRuleOptions): ResolvedPasswordRule {
  const policy: PasswordPolicy = { ...DEFAULT_PASSWORD_POLICY, ...appPolicy, ...opts.policy }
  const complexity = opts.complexity ?? true
  return {
    min: opts.min ?? policy.minLength,
    max: opts.max ?? DEFAULT_PASSWORD_MAX,
    classes: complexity ? PASSWORD_CLASSES.filter((c) => policy[c.flag]) : [],
  }
}

/** The provided policy, when this runs where the application can be asked (setup or `app.runWithContext`). */
function providedPasswordPolicy(): Partial<PasswordPolicy> | null {
  return hasInjectionContext() ? inject(PASSWORD_POLICY_KEY, null) : null
}

/** A field's value as the text checks read it: nothing typed reads as ''. */
function asText(value: unknown): string {
  return value === undefined || value === null ? '' : String(value)
}

/**
 * Whether the required rule next to a check already refuses this value. The
 * checks pass such a value, so an empty field shows one message (the required
 * one) instead of that and a length error for the same condition.
 */
function isBlank(value: unknown): boolean {
  return asText(value).trim() === ''
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
        message: message ?? fb(t, 'admin.rules.required', 'This field is required'),
      }
    },

    text(opts = {}) {
      const min = opts.min ?? 1
      const max = opts.max ?? 255
      return [
        {
          required: true,
          trigger: ['blur', 'input'],
          message: opts.message ?? fb(t, 'admin.rules.required', 'This field is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(value: unknown) {
            if (isBlank(value)) return true
            const len = asText(value).trim().length
            if (len < min || len > max) {
              return fb(t, 'admin.rules.lengthRange', `Length must be ${min}-${max}`)
                .replace('{min}', String(min))
                .replace('{max}', String(max))
            }
            return true
          },
        },
      ]
    },

    userName: [
      {
        required: true,
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
      const { min, max, classes } = resolvePasswordRule(appPasswordPolicy, opts)
      return [
        {
          required: true,
          trigger: ['blur', 'input'],
          message: fb(t, 'admin.rules.required', 'Password is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(value: unknown) {
            if (isBlank(value)) return true
            const v = asText(value)
            if (v.length < min) {
              return fb(t, 'admin.rules.passwordTooShort', 'Password must be at least {min} characters')
                .replace('{min}', String(min))
            }
            if (v.length > max) {
              return fb(t, 'admin.rules.passwordTooLong', 'Password must be no more than {max} characters')
                .replace('{max}', String(max))
            }
            const missing = classes.find((c) => !c.pattern.test(v))
            if (missing) return fb(t, missing.key, missing.fallback)
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
          trigger: ['blur', 'input'],
          message: opts.message ?? fb(t, 'admin.rules.required', 'Account is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(value: unknown) {
            if (isBlank(value)) return true
            const len = asText(value).trim().length
            if (len < min || len > max) {
              return fb(t, 'admin.rules.lengthRange', `Length must be ${min}-${max}`)
                .replace('{min}', String(min))
                .replace('{max}', String(max))
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
          trigger: ['blur', 'input'],
          message: fb(t, 'admin.rules.required', 'This field is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(value: unknown) {
            const v = asText(value).trim()
            if (emailRegex().test(v) || phoneRegex().test(v)) return true
            return message ?? fb(t, 'admin.rules.emailOrPhone', 'Enter a valid email or phone number')
          },
        },
      ]
    },

    matches(other, message) {
      return {
        trigger: ['blur', 'input'],
        validator(value: unknown) {
          if (value !== other()) {
            return message ?? fb(t, 'admin.rules.matches', 'Values do not match')
          }
          return true
        },
      }
    },

    url: [
      {
        required: true,
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
        message: fb(t, 'admin.rules.required', 'This field is required'),
      },
      {
        trigger: ['blur'],
        validator(value: unknown) {
          if (!value) return true
          try {
            JSON.parse(asText(value))
            return true
          } catch {
            return fb(t, 'admin.rules.json', 'Invalid JSON')
          }
        },
      },
    ],

    integer(opts = {}) {
      const result: IFormRule[] = [
        {
          required: true,
          trigger: ['blur', 'input'],
          message: fb(t, 'admin.rules.required', 'This field is required'),
        },
        {
          trigger: ['blur', 'input'],
          validator(value: unknown) {
            const n = typeof value === 'number' ? value : Number(value)
            if (!Number.isInteger(n)) {
              return fb(t, 'admin.rules.integer', 'Must be an integer')
            }
            if (opts.min !== undefined && n < opts.min) {
              return fb(t, 'admin.rules.min', `Must be ≥ ${opts.min}`).replace('{min}', String(opts.min))
            }
            if (opts.max !== undefined && n > opts.max) {
              return fb(t, 'admin.rules.max', `Must be ≤ ${opts.max}`).replace('{max}', String(opts.max))
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

/**
 * The requirements a `password()` rule checks, as one sentence to show before
 * the user types (a field hint, a banner): "Use at least 6 characters,
 * including a lowercase letter (a-z) and a digit (0-9)." It reads the same
 * policy the rule does, so call it where `useFormRules` is called (in setup)
 * and pass the options given to `password()`.
 *
 * @example
 * ```ts
 * const passwordRequirements = usePasswordRequirements(translate)
 * const hint = computed(() => passwordRequirements())
 * ```
 */
export function usePasswordRequirements(translate?: Translate): (opts?: PasswordRuleOptions) => string {
  const t = translate
  const appPasswordPolicy = providedPasswordPolicy()
  return (opts = {}) => {
    const { min, classes } = resolvePasswordRule(appPasswordPolicy, opts)
    if (classes.length === 0) {
      return fb(t, 'admin.rules.passwordRequirements.length', 'Use at least {min} characters.')
        .replace('{min}', String(min))
    }
    const names = classes.map((c) => fb(t, c.nameKey, c.nameFallback))
    const last = names[names.length - 1]!
    const listed = names.length === 1
      ? last
      : fb(t, 'admin.rules.passwordRequirements.listLast', '{rest} and {last}')
          .replace('{rest}', names.slice(0, -1).join(fb(t, 'admin.rules.passwordRequirements.listSeparator', ', ')))
          .replace('{last}', last)
    return fb(t, 'admin.rules.passwordRequirements.lengthAndClasses', 'Use at least {min} characters, including {classes}.')
      .replace('{min}', String(min))
      .replace('{classes}', listed)
  }
}
