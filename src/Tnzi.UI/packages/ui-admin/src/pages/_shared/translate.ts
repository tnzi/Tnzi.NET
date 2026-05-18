import { en } from '../../locales/en'
import { zhCn } from '../../locales/zh-cn'
import { useAdminAppStore } from '../../stores/useAdminAppStore'

/**
 * Last-segment capitalised fallback for i18n keys that haven't been
 * translated yet (e.g. `admin.crud.searchPlaceholder` → "Search Placeholder",
 * `loginLogs` → "Login Logs"). Mirrors the fallback used by
 * `useAdminRouteStore.resolveI18nKey`, `TAdminAutoBreadcrumb`, and
 * `TAdminTabs.renderTitle` so every surface shows the same humanised
 * label when a key is missing.
 */
function humanise(key: string): string {
  if (!key) return ''
  const parts = key.split('.')
  let last = parts[parts.length - 1] ?? key
  if (last === 'title' && parts.length > 1) {
    last = parts[parts.length - 2] ?? key
  }
  // camelCase → "Space Separated"
  const spaced = last.replace(/([a-z])([A-Z])/g, '$1 $2')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

function lookup(messages: Record<string, unknown>, path: string): string | undefined {
  let node: unknown = messages
  for (const part of path.split('.')) {
    if (typeof node === 'object' && node !== null && part in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[part]
    } else {
      return undefined
    }
  }
  return typeof node === 'string' ? node : undefined
}

/**
 * Resolves an i18n key.
 *
 * - **Absolute keys** (`admin.…`, `tnzi.admin.…`) are looked up directly,
 *   the page namespace is ignored. This is what fixed the
 *   `admin.crud.searchPlaceholder` → `admin.modules.identity.roles.admin.crud.searchPlaceholder`
 *   double-prefix bug: TCrudPage forwards its `admin.crud.*` literals
 *   straight in, expecting them to resolve at the locale root.
 * - **Page-scoped keys** (no `admin.` prefix) are prepended with
 *   `admin.modules.{pageNs}.` so each page can use short keys.
 * - **Misses** fall back to a humanised last segment so the user never
 *   sees raw dotted strings.
 */
export function translatePageKey(pageNs: string, key: string): string {
  if (!key) return key
  const locale = useAdminAppStore().locale
  const messages = (locale === 'zh-cn' ? zhCn : en) as Record<string, unknown>

  // Strip optional `tnzi.` prefix — bundled locales are rooted at `admin.*`.
  const normalised = key.startsWith('tnzi.') ? key.slice(5) : key

  // Absolute key — resolve directly without prefixing the page namespace.
  if (normalised.startsWith('admin.')) {
    const hit = lookup(messages, normalised)
    return hit ?? humanise(key)
  }

  // Page-scoped key — prepend the namespace.
  const full = `admin.modules.${pageNs}.${normalised}`
  const hit = lookup(messages, full)
  if (hit !== undefined) return hit

  // Shared dictionary fallback for `columns.xxx` / `form.xxx` keys — lets a
  // single global label cover every page that surfaces the same field name
  // (column.key). Page-scoped entries always take precedence above; this
  // only kicks in when the page hasn't defined a per-key override.
  if (normalised.startsWith('columns.') || normalised.startsWith('form.')) {
    const shared = lookup(messages, `admin.shared.${normalised}`)
    if (shared !== undefined) return shared
  }

  return humanise(key)
}

/**
 * Mustache-style `{name}` placeholder substitution against a translated string.
 * Centralised here so every page uses identical null-handling semantics —
 * missing params resolve to an empty string (not the literal `{name}` token)
 * to avoid leaking template syntax into user-facing copy.
 *
 * Examples:
 *   interpolate('Hello, {name}', { name: 'Alice' }) → 'Hello, Alice'
 *   interpolate('Picked {n} of {total}', { n: 3, total: 10 }) → 'Picked 3 of 10'
 *   interpolate('Hi {name}', {})                  → 'Hi '
 */
export function interpolate(template: string, params?: Record<string, unknown>): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (_, k: string) => {
    const v = params[k]
    return v == null ? '' : String(v)
  })
}

/**
 * Bound translate factory — returns a `t(key, params?)` function pinned to
 * a page namespace and aware of the `{name}` interpolation convention.
 *
 * Usage in page setup:
 *   const t = makePageTranslator('identity.organizations')
 *   t('createSuccess')                       // → 'Organization created'
 *   t('list.titleFor', { userId: 'u-123' })  // → 'Sessions for user u-123'
 *
 * Equivalent to the previous per-page inline `t(key, params)` helpers; saves
 * each consumer from re-implementing the regex substitution + null handling.
 */
export function makePageTranslator(pageNs: string): (key: string, params?: Record<string, unknown>) => string {
  return (key: string, params?: Record<string, unknown>) =>
    interpolate(translatePageKey(pageNs, key), params)
}
