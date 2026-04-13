/**
 * @tnzi/ui/utils/naive-helpers
 *
 * Shared Naive UI bridge utilities — extracted to eliminate duplication
 * across components, stores, and adapters.
 */

import type { MenuOption, FormItemRule } from 'naive-ui'
import type { IMenuItem, IFormRule } from '@tnzi/core'
import type { ThemeMode } from '@tnzi/core/types'

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
 * Convert a single core IFormRule to a Naive UI FormItemRule.
 */
export function convertFormRule(rule: IFormRule): FormItemRule {
  const naiveRule: FormItemRule = {}

  if (rule.required !== undefined) naiveRule.required = rule.required
  if (rule.message !== undefined) naiveRule.message = rule.message
  if (rule.trigger !== undefined) naiveRule.trigger = rule.trigger
  if (rule.min !== undefined) naiveRule.min = rule.min
  if (rule.max !== undefined) naiveRule.max = rule.max
  if (rule.pattern !== undefined) naiveRule.pattern = rule.pattern
  if (rule.validator !== undefined) {
    const customValidator = rule.validator
    naiveRule.validator = async (_rule: FormItemRule, value: unknown): Promise<void> => {
      const result = await customValidator(value)
      if (result === true) return
      if (result === false) {
        throw new Error(rule.message || 'Validation failed')
      }
      if (typeof result === 'string') {
        throw new Error(result)
      }
    }
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

// ============================================
// DOM helpers (theme & language)
// ============================================

/**
 * Apply theme mode to the document root element by toggling the 'dark' class.
 * Resolves 'system' mode using `prefers-color-scheme` media query.
 * Safe to call in SSR (no-ops when `document` is unavailable).
 */
export function applyThemeToDOM(theme: ThemeMode): void {
  if (typeof document === 'undefined') return

  const isDark =
    theme === 'dark' ||
    (theme === 'system' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches)

  document.documentElement.classList.toggle('dark', isDark)
}

/**
 * Apply language to the document root element's `lang` attribute.
 * Safe to call in SSR (no-ops when `document` is unavailable).
 */
export function applyLanguageToDOM(language: string): void {
  if (typeof document === 'undefined') return
  document.documentElement.lang = language
}
