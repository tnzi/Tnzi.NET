/**
 * @tnzi/ui/utils/naive-helpers
 *
 * Shared Naive UI bridge utilities - extracted to eliminate duplication
 * across components, stores, and adapters.
 */

import type { MenuOption, FormItemRule } from 'naive-ui'
import type { IMenuItem, IFormRule, FormRuleResult } from '@tnzi/core'
import { isEmptyValue } from './placeholders'

// ============================================
// Menu conversion
// ============================================

/**
 * Convert core IMenuItem[] to Naive UI MenuOption[].
 * Recursively maps children and filters hidden items.
 */
export function convertToMenuOptions(items: IMenuItem[]): MenuOption[] {
  return items
    .filter((item) => !item.hidden)
    .map((item) => {
      const option: MenuOption = {
        key: item.key,
        label: item.label,
        disabled: item.disabled,
      }
      if (item.children && item.children.length > 0) {
        option.children = convertToMenuOptions(item.children)
      }
      return option
    })
}

// ============================================
// Form rule conversion
// ============================================

/**
 * A message async-validator would have produced: the form's template for it
 * (an NForm `validate-messages` may replace the defaults) filled in the same
 * way, so a rule without its own `message` reads as it did before the check
 * moved into a validator.
 */
function formMessage(template: unknown, fallback: string, args: unknown[]): string {
  if (typeof template === 'function') return String(template(...args))
  const text = typeof template === 'string' ? template : fallback
  let next = 0
  return text.replace(/%s/g, () => String(args[next++] ?? ''))
}

/** What a rule's own check answered, as the error naive shows; undefined when it passed. */
function validationFailure(result: FormRuleResult, message: string | undefined): Error | undefined {
  if (result === true) return undefined
  if (typeof result === 'string') return new Error(result)
  return new Error(message || 'Validation failed')
}

/**
 * Convert a single core IFormRule to a Naive UI FormItemRule.
 *
 * A required rule counts a whitespace-only string as missing, as the backend's
 * [Required] does (passing it here only moves the error to a 400). The value
 * itself is never trimmed.
 *
 * How that is wired follows from which checks async-validator runs for a given
 * rule shape: a `validator` replaces all of its built-in checks, a `pattern`
 * (without one) selects the pattern check, which reads `required` but neither
 * `whitespace` nor `min` / `max`, and only a rule left on the string check
 * honours `whitespace: true`. So:
 *   - a required rule with only a length range (no pattern, no validator) gets
 *     `whitespace: true`;
 *   - every other required rule gets a validator that rejects a missing value
 *     (null / undefined / blank text / empty list) first, then runs what the
 *     built-in checks did for that shape: the pattern, or the rule's own
 *     validator. Without it a `required` next to a `validator` was never
 *     checked at all.
 */
export function convertFormRule(rule: IFormRule): FormItemRule {
  const naiveRule: FormItemRule = {}

  if (rule.required !== undefined) naiveRule.required = rule.required
  if (rule.message !== undefined) naiveRule.message = rule.message
  if (rule.trigger !== undefined) naiveRule.trigger = rule.trigger
  if (rule.min !== undefined) naiveRule.min = rule.min
  if (rule.max !== undefined) naiveRule.max = rule.max
  if (rule.pattern !== undefined) naiveRule.pattern = rule.pattern

  const customValidator = rule.validator
  const lengthOnly = (rule.min !== undefined || rule.max !== undefined) && rule.pattern === undefined
  const checksMissing = rule.required === true && (customValidator !== undefined || !lengthOnly)
  if (rule.required === true && !checksMissing) naiveRule.whitespace = true
  if (customValidator === undefined && !checksMissing) return naiveRule

  // Synchronous unless the rule's own validator is asynchronous: async-validator
  // reports errors in the order its checks settle, so a promise here would put
  // this rule's message after those of the synchronous rules declared after it.
  // Failures are returned, not thrown: naive treats a thrown error as a broken
  // validator and skips the validation.
  naiveRule.validator = (ruleItem, value, _callback, _source, options) => {
    const { fullField, field } = ruleItem as { fullField?: string; field?: string }
    const path = fullField ?? field ?? ''
    if (checksMissing && isEmptyValue(value)) {
      return new Error(rule.message || formMessage(options?.messages?.required, '%s is required', [path]))
    }
    if (customValidator === undefined) {
      // The pattern check this validator stands in for. A length range next
      // to a pattern was never applied, so it is not applied here either.
      const pattern = rule.pattern
      if (pattern !== undefined) {
        pattern.lastIndex = 0
        if (!pattern.test(String(value))) {
          return new Error(
            rule.message
              || formMessage(options?.messages?.pattern?.mismatch, '%s value %s does not match pattern %s', [path, value, pattern]),
          )
        }
      }
      return true
    }
    let result: ReturnType<typeof customValidator>
    try {
      result = customValidator(value)
    } catch (error) {
      // Shown as the field's error, as a rejected promise was before.
      return error instanceof Error ? error : new Error(String(error))
    }
    if (result instanceof Promise) {
      return result.then((settled) => {
        const failure = validationFailure(settled, rule.message)
        if (failure) throw failure
      })
    }
    return validationFailure(result, rule.message) ?? true
  }

  return naiveRule
}

/**
 * Convert a record of core IFormRule[] to Naive UI FormRules format.
 */
export function convertFormRules(rules: Record<string, IFormRule[]>): Record<string, FormItemRule[]> {
  const converted: Record<string, FormItemRule[]> = {}
  for (const [field, fieldRules] of Object.entries(rules)) {
    converted[field] = fieldRules.map(convertFormRule)
  }
  return converted
}
