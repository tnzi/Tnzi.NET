/**
 * `useFormRules()` — i18n-reactive form validation rules for Naive UI's
 * `NForm`. Returns a frozen set of `FormItemRule[]` keyed by common field
 * names; consumers pick the ones they need.
 *
 * @example
 * ```vue
 * const { rules } = useFormRules(t)
 * const formRules = { email: rules.email, password: rules.password() }
 * ```
 *
 * All rules accept a `translate` function. When omitted, returns
 * English fallback messages.
 */

import type { FormItemRule } from 'naive-ui'

export type Translate = (key: string, fallback?: string) => string

function fb(t: Translate | undefined, key: string, fallback: string): string {
  if (!t) return fallback
  const result = t(key, fallback)
  return result === key && fallback ? fallback : result
}

export interface FormRules {
  /** Required string — non-empty after trim. */
  required(message?: string): FormItemRule
  /** Required + length range. */
  text(opts?: { min?: number; max?: number; message?: string }): FormItemRule[]
  /** Username — 3-32 chars, letters/digits/dash/underscore. */
  userName: FormItemRule[]
  /** Email — RFC 5322 light. */
  email: FormItemRule[]
  /** International phone number — digits + optional `+` and dashes. */
  phone: FormItemRule[]
  /** Strong password — 8-32 chars, at least one letter + one digit. */
  password(opts?: { min?: number; max?: number }): FormItemRule[]
  /** Match another field value (e.g. confirm-password). */
  matches(other: () => string, message?: string): FormItemRule
  /** Valid URL (http/https only). */
  url: FormItemRule[]
  /** Valid JSON string. */
  json: FormItemRule[]
  /** Integer in range. */
  integer(opts?: { min?: number; max?: number }): FormItemRule[]
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
      const min = opts.min ?? 8
      const max = opts.max ?? 32
      return [
        {
          required: true,
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
            if (!/[A-Za-z]/.test(v) || !/\d/.test(v)) {
              return new Error(
                fb(t, 'admin.rules.passwordMix', 'Password must contain letters and digits'),
              )
            }
            return true
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
