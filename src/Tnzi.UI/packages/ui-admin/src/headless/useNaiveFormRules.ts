/**
 * The login stack's form rules, as Naive UI's `NForm` takes them.
 *
 * `@tnzi/ui` builds its rules (`useFormRules`, `useLoginAccountField`) as the
 * library-neutral `IFormRule` from `@tnzi/core/types`, so the shared layer
 * depends on no component library. This package renders forms with Naive UI,
 * so translating them is its job, and it goes through the one converter
 * (`convertFormRule`): a required rule refuses a blank value whatever else it
 * checks, and a validator's message becomes the form item's error.
 *
 * @example
 * ```ts
 * const { rules: r } = useNaiveFormRules(translate)
 * const formRules: FormRules = { userName: r.userName, password: r.password() }
 * ```
 */

import { computed, type ComputedRef } from 'vue'
import type { FormItemRule } from 'naive-ui'
import type { IFormRule } from '@tnzi/core/types'
import { useFormRules, useLoginAccountField, type CodeChannels, type FormRules, type Translate } from '@tnzi/ui'
import { convertFormRule } from '../utils/naive-helpers'

/** One member of `FormRules` with every `IFormRule` in it turned into a `FormItemRule`. */
type AsNaive<T> = T extends IFormRule[]
  ? FormItemRule[]
  : T extends IFormRule
    ? FormItemRule
    : T extends (...args: infer A) => infer R
      ? (...args: A) => AsNaive<R>
      : never

/** `FormRules` from `@tnzi/ui`, member for member, in Naive UI's rule type. */
export type NaiveFormRules = { [K in keyof FormRules]: AsNaive<FormRules[K]> }

function toNaive(rules: IFormRule | IFormRule[]): FormItemRule | FormItemRule[] {
  return Array.isArray(rules) ? rules.map(convertFormRule) : convertFormRule(rules)
}

/**
 * `useFormRules(translate)` with each rule converted for `NForm`. Call it where
 * `useFormRules` would be called (in setup): the fixed rule sets read their
 * messages, and `password()` the application's password policy, at that point.
 */
export function useNaiveFormRules(translate?: Translate): { rules: NaiveFormRules } {
  const { rules } = useFormRules(translate)
  const converted = Object.fromEntries(
    Object.entries(rules).map(([name, member]) => [
      name,
      typeof member === 'function'
        ? (...args: unknown[]) => toNaive((member as (...a: unknown[]) => IFormRule | IFormRule[])(...args))
        : toNaive(member as IFormRule[]),
    ]),
  )
  return { rules: converted as NaiveFormRules }
}

export interface NaiveLoginAccountField {
  rule: ComputedRef<FormItemRule[]>
  label: ComputedRef<string>
  placeholder: ComputedRef<string>
}

/**
 * `useLoginAccountField` (email / phone account field of the code-login,
 * register and password-recovery modules) with its rule converted for `NForm`.
 *
 * Each rule also carries `key: 'account'`, so a caller can validate the account
 * alone with naive's rule predicate, `validate(undefined, (rule) => rule.key ===
 * 'account')`: naive puts no key or path on the rules it merges.
 */
export function useNaiveLoginAccountField(
  translate: Translate,
  channels: () => CodeChannels,
): NaiveLoginAccountField {
  const { rule, label, placeholder } = useLoginAccountField(translate, channels)
  return {
    rule: computed(() => rule.value.map((r) => ({ ...convertFormRule(r), key: 'account' }))),
    label,
    placeholder,
  }
}
