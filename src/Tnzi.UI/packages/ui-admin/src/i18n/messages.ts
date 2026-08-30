/**
 * Bundled-dictionary registry.
 *
 * ## Why this exists
 *
 * The two shipped dictionaries are large - roughly 57 kB and 62 kB gzipped -
 * and they used to be pulled in with a plain `import { en } from '../locales/en'`
 * at the top of the translate helper. That helper is imported by ~45 components,
 * widgets and layout pieces, so **both** dictionaries landed in the entry graph
 * of any consumer that imported a single admin component: an English-only app
 * shipped the Chinese pack and vice versa, together eating about half of the
 * package's whole 240 kB gzip budget.
 *
 * Nothing required them to be static. Lookup never falls back across locales
 * (a miss goes straight to `humanise`, not to English), so only the ACTIVE
 * locale's dictionary is ever read. Loading them through `import()` lets a
 * bundler put each one in its own async chunk and fetch only the one in use.
 *
 * ## Consequence for callers
 *
 * `getLocaleMessages()` is synchronous and returns `undefined` until the chunk
 * lands, which reads to `translatePageKey` exactly like a dictionary miss - the
 * humanised key. To keep that from being visible, the store is **reactive**:
 * anything that resolved a label inside a render effect re-renders when the
 * dictionary arrives. `defineAdminApp().install()` starts the fetch before the
 * first paint, and `createAdminApp()` exposes the promise as `localeReady` for
 * apps that would rather await it than repaint.
 */
import { shallowRef, triggerRef } from 'vue'

/**
 * A locale code.
 *
 * Deliberately an OPEN set. The two bundled codes stay spelled out so editors
 * still complete them and typos in framework code still fail, but any string
 * is accepted: a consumer registering `fr` (see `i18n/locale-registry`) must be
 * able to call `setLocale('fr')`, `getLocaleMessages('fr')` and
 * `extendLocaleMessages({ fr })` without a cast at every call site. The
 * `string & Record<never, never>` arm is the standard idiom for "any string,
 * but keep the literals in autocomplete".
 */
export type AdminLocale = 'en' | 'zh-cn' | (string & Record<never, never>)

/** The dictionaries this package ships as async chunks. */
export const BUNDLED_LOCALES = ['en', 'zh-cn'] as const

type Dictionary = Record<string, unknown>

/**
 * Reactive so a late-arriving dictionary repaints labels that were resolved
 * before it landed. `shallowRef` + `triggerRef` rather than `reactive`: the
 * dictionaries are big frozen literals and deep-tracking them would cost far
 * more than the one signal actually needed.
 */
const dictionaries = shallowRef(new Map<AdminLocale, Dictionary>())
const inFlight = new Map<AdminLocale, Promise<void>>()
/**
 * Consumer-registered dictionary loaders, keyed by locale code. Populated by
 * `registerAdminLocales` for definitions that pass `loadMessages`, so a
 * consumer's own pack can be an async chunk exactly like the bundled ones.
 * Kept here rather than in the registry so this module never has to import it
 * (the dependency runs registry → messages, never back).
 */
const loaders = new Map<AdminLocale, () => Promise<Record<string, unknown>>>()

/**
 * Register a lazy dictionary loader for `locale`. Called by
 * `registerAdminLocales`; direct callers are assembling a shell by hand.
 */
export function setLocaleLoader(
  locale: AdminLocale,
  loader: () => Promise<Record<string, unknown>>,
): void {
  loaders.set(locale, loader)
}

/** Dictionary for `locale`, or `undefined` when it has not been loaded yet. */
export function getLocaleMessages(locale: AdminLocale): Dictionary | undefined {
  return dictionaries.value.get(locale)
}

/**
 * Install a dictionary directly.
 *
 * Used by the bundled loader below, and by tests / consumers that want a
 * dictionary present synchronously instead of awaiting a chunk.
 */
export function setLocaleMessages(locale: AdminLocale, messages: Dictionary): void {
  dictionaries.value.set(locale, messages)
  triggerRef(dictionaries)
}

/**
 * Load a dictionary. Idempotent, and concurrent calls share a single import.
 * Resolves immediately when the dictionary is already present, and also when
 * the locale has no pack at all (see `resolveLoader`).
 */
export function loadLocaleMessages(locale: AdminLocale): Promise<void> {
  if (dictionaries.value.has(locale)) return Promise.resolve()

  const existing = inFlight.get(locale)
  if (existing) return existing

  const source = resolveLoader(locale)
  // No pack for this code: a consumer-registered locale whose strings arrive
  // through `extendLocaleMessages` instead. Resolve without installing
  // anything. This used to fall through to the `en` branch of a ternary, which
  // installed the ENGLISH dictionary under the requested code - so a French app
  // rendered English chrome and looked like a translation gap rather than a bug.
  if (!source) return Promise.resolve()

  const task = source()
    .then((messages) => {
      setLocaleMessages(locale, messages)
    })
    .catch(() => {
      // A failed chunk fetch degrades to humanised keys - a readable UI - which
      // beats an unhandled rejection taking the shell down. Drop the memo so a
      // later attempt (e.g. after the network comes back) can retry.
      inFlight.delete(locale)
    })

  inFlight.set(locale, task)
  return task
}

/**
 * Drop every dictionary, loader and in-flight memo for a locale this package
 * does not bundle.
 *
 * Test-only, and called by `resetAdminLocalesForTest` so ONE reset undoes
 * everything a spec registered - a reset that only rolls back half the state
 * is worse than none, because the leftover half looks like a passing test.
 * The bundled two survive: the global test setup installs them once per file
 * and every other spec expects them present.
 */
export function resetLocaleMessagesForTest(): void {
  for (const code of [...dictionaries.value.keys()]) {
    if (!(BUNDLED_LOCALES as readonly string[]).includes(code)) dictionaries.value.delete(code)
  }
  for (const code of [...inFlight.keys()]) {
    if (!(BUNDLED_LOCALES as readonly string[]).includes(code)) inFlight.delete(code)
  }
  loaders.clear()
  triggerRef(dictionaries)
}

/**
 * Pick the loader for `locale`: a consumer-registered one first, then the
 * bundled packs.
 *
 * The bundled `import()` specifiers stay literal on purpose - a computed
 * specifier defeats static analysis and bundlers emit the whole `locales/`
 * folder as one chunk, putting us back where we started.
 */
function resolveLoader(locale: AdminLocale): (() => Promise<Dictionary>) | undefined {
  const registered = loaders.get(locale)
  if (registered) return registered as () => Promise<Dictionary>
  if (locale === 'en') return () => import('../locales/en').then((m) => m.en as Dictionary)
  if (locale === 'zh-cn') return () => import('../locales/zh-cn').then((m) => m.zhCn as Dictionary)
  return undefined
}
