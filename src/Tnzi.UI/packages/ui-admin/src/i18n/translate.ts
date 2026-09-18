import { useAdminAppStore } from '../stores/useAdminAppStore'
import { getLocaleMessages, type AdminLocale } from './messages'

/**
 * Last-segment capitalised fallback for i18n keys that haven't been
 * translated yet (e.g. `admin.crud.searchPlaceholder` → "Search Placeholder",
 * `loginLogs` → "Login Logs"). Mirrors the fallback used by
 * `useAdminRouteStore.resolveI18nKey`, `TAdminAutoBreadcrumb`, and
 * `TAdminTabs.renderTitle` so every surface shows the same humanised
 * label when a key is missing.
 */
export function humanise(key: string): string {
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

/**
 * Walk a dotted path through a messages tree, returning the string leaf or
 * `undefined` when any segment is missing or the leaf is not a string.
 *
 * Exported because `useAdminRouteStore.resolveI18nKey` needs the identical
 * walk under a different signature (it resolves locale + overrides ONCE and
 * maps the result over a whole menu tree). It used to keep a byte-identical
 * private copy - the same hand-copy that let `AdminShellRoot` and
 * `ExceptionView` drift away from honouring consumer overrides.
 */
export function lookupMessage(
  messages: Record<string, unknown>,
  path: string,
): string | undefined {
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
 * The active locale and its consumer overrides.
 *
 * `useAdminAppStore()` requires an active Pinia. Test harnesses that mount
 * components without `createPinia()` make it throw, so a failure defaults to
 * English rather than forcing every mount to wire pinia. Production shells
 * always install pinia in main.ts.
 *
 * Single source of the "which dictionary wins" rule: consumer overrides for
 * the ACTIVE locale first, then the bundled pack. Every chrome surface goes
 * through it. The breadcrumb and the exception pages each used to carry their
 * own copy of the walk that read the bundled dictionary only, so an override
 * for a key the bundle already had (`admin.modules.dashboard.title`) was
 * honoured in the sidebar menu and ignored in the breadcrumb.
 *
 * There are three ENTRY POINTS, and that is deliberate - they differ only in
 * miss policy and in how the locale reaches them:
 *
 * | entry | miss policy | locale source |
 * |---|---|---|
 * | `translateChromeKey` | `fallback ?? key` | reads the store |
 * | `translatePageKey`   | `humanise(key)`   | reads the store |
 * | `useAdminRouteStore.resolveI18nKey` | `humanise(key)` | passed in, resolved once per menu tree |
 *
 * All three share `lookupMessage` and the overrides-first order. Adding a
 * fourth private walk is how this drifted the first time; extend these instead.
 */
function activeLocale(): { locale: AdminLocale; overrides?: Record<string, unknown> } {
  try {
    const store = useAdminAppStore()
    const locale = store.locale
    return {
      locale,
      overrides: store.messageOverrides?.[locale] as Record<string, unknown> | undefined,
    }
  } catch {
    return { locale: 'en' }
  }
}

/**
 * Resolve a chrome key: consumer overrides first, then the active locale's
 * bundled dictionary, then `fallback ?? key` on a miss.
 *
 * Same resolution ORDER as `translatePageKey`, different MISS POLICY. Chrome
 * translators (`TThemeDrawer`, the desktop tray, the login page) pass an
 * English literal as `fallback` and want it verbatim on a miss; page keys have
 * no such literal and humanise instead. Keep both - what must not diverge is
 * which dictionary wins, and that is now decided in one place.
 */
export function translateChromeKey(key: string, fallback?: string): string {
  if (!key) return key
  const { locale, overrides } = activeLocale()
  const messages = getLocaleMessages(locale) ?? {}
  // Strip optional `tnzi.` prefix - bundled locales are rooted at `admin.*`
  // (mirrors translatePageKey / resolveI18nKey).
  const normalised = key.startsWith('tnzi.') ? key.slice(5) : key
  const hit = (overrides && lookupMessage(overrides, normalised)) ?? lookupMessage(messages, normalised)
  return hit ?? fallback ?? key
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
  const { locale, overrides } = activeLocale()
  // Undefined until the locale chunk lands; every lookup below then misses and
  // falls through to `humanise`, and the reactive registry repaints once it
  // arrives. See i18n/messages.ts.
  const messages = getLocaleMessages(locale) ?? {}

  // Strip optional `tnzi.` prefix - bundled locales are rooted at `admin.*`.
  const normalised = key.startsWith('tnzi.') ? key.slice(5) : key

  // Resolution order for every lookup: consumer overrides (registered via
  // `useAdminAppStore.extendLocaleMessages`) win first, then the bundled
  // framework dictionary. Lets host apps surface their own
  // `admin.modules.{module}.…` and `admin.shared.…` keys to every page,
  // breadcrumb, and tab without forking the locale files.
  const find = (path: string): string | undefined =>
    (overrides && lookupMessage(overrides, path)) ?? lookupMessage(messages, path)

  // Absolute key - resolve directly without prefixing the page namespace.
  if (normalised.startsWith('admin.')) {
    return find(normalised) ?? humanise(key)
  }

  // Page-scoped key - prepend the namespace.
  const full = `admin.modules.${pageNs}.${normalised}`
  const hit = find(full)
  if (hit !== undefined) return hit

  // A key that already carries its own page namespace (`finance.recurring.actions.end`
  // handed to the `finance.recurring` translator) would be prefixed twice above and
  // miss. Resolve it once-prefixed instead of humanising: the slip is easy to make,
  // nothing errors, and the humanised result reads as plausible English ("End
  // Confirm") so only a non-English user or the confirm text gives it away.
  if (pageNs && normalised.startsWith(`${pageNs}.`)) {
    const repaired = find(`admin.modules.${normalised}`)
    if (repaired !== undefined) return repaired
  }

  // Shared dictionary fallback for `columns.xxx` / `form.xxx` keys - lets a
  // single global label cover every page that surfaces the same field name
  // (column.key). Page-scoped entries always take precedence above; this
  // only kicks in when the page hasn't defined a per-key override.
  if (normalised.startsWith('columns.') || normalised.startsWith('form.')) {
    const shared = find(`admin.shared.${normalised}`)
    if (shared !== undefined) return shared
  }

  return humanise(key)
}

/**
 * Mustache-style `{name}` placeholder substitution against a translated string.
 * Centralised here so every page uses identical null-handling semantics -
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
 * Bound translate factory - returns a `t(key, params?)` function pinned to
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

/**
 * Heuristic translator for fields that may hold either an i18n key or a
 * pre-translated literal string.
 *
 * The "is this an i18n key?" check is the same one TCrudPage uses for its
 * `title` prop - dotted lower-camel ASCII (`admin.modules.foo.bar`). When
 * the value matches, it's resolved through `translatePageKey('', value)`;
 * when it doesn't (e.g. "Hello world" or a raw display string), it passes
 * through unchanged.
 *
 * Use this in widgets / list renderers that accept user-supplied strings
 * that *might* be locale keys: KPI card titles, timeline item titles,
 * etc. The detection is intentionally narrow so legitimate English
 * sentences with periods (e.g. "Mr. Smith") aren't accidentally treated
 * as keys.
 */
const I18N_KEY_PATTERN = /^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$/

export function maybeTranslate(value: string | undefined | null): string {
  if (!value) return ''
  if (!I18N_KEY_PATTERN.test(value)) return value
  const hit = translatePageKey('', value)
  return hit || value
}

/**
 * Resolve a backend-provided i18n key, falling back to the backend display
 * string on a dictionary miss. `translatePageKey` never echoes the raw key -
 * it returns `humanise(key)` when the dictionary misses - so a miss is
 * detected by comparing against the humanised form. Used by the settings
 * center where every group/field ships both an `i18nKey` and an English
 * `label`/`displayName` fallback.
 */
export function resolveBackendLabel(i18nKey: string | null | undefined, fallback: string): string {
  if (!i18nKey) return fallback
  const resolved = translatePageKey('', i18nKey)
  return resolved && resolved !== humanise(i18nKey) ? resolved : fallback
}

/**
 * Decide whether `value` is an i18n key (translate it) or a human literal
 * (show verbatim). A "key" is a dotted lowerCamel path like `admin.crud.list`.
 * When `value` is empty, translate `fallback` instead. Mirrors the heuristic
 * formerly inlined in TListShell.
 */
export function maybeTranslateKey(
  translate: ((key: string) => string) | undefined,
  value: string | undefined,
  fallback: string,
): string {
  const t = (k: string) => (translate ? translate(k) : k)
  if (!value) return t(fallback)
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) return t(value)
  return value
}
