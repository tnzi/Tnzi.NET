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

export type AdminLocale = 'en' | 'zh-cn'

type Dictionary = Record<string, unknown>

/**
 * Reactive so a late-arriving dictionary repaints labels that were resolved
 * before it landed. `shallowRef` + `triggerRef` rather than `reactive`: the
 * dictionaries are big frozen literals and deep-tracking them would cost far
 * more than the one signal actually needed.
 */
const dictionaries = shallowRef(new Map<AdminLocale, Dictionary>())
const inFlight = new Map<AdminLocale, Promise<void>>()

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
 * Load one bundled dictionary. Idempotent, and concurrent calls share a single
 * import. Resolves immediately when the dictionary is already present.
 *
 * The `import()` specifiers are literal on purpose: a computed specifier would
 * defeat static analysis and bundlers would emit the whole `locales/` folder as
 * one chunk, putting us back where we started.
 */
export function loadLocaleMessages(locale: AdminLocale): Promise<void> {
  if (dictionaries.value.has(locale)) return Promise.resolve()

  const existing = inFlight.get(locale)
  if (existing) return existing

  const task = (locale === 'zh-cn'
    ? import('../locales/zh-cn').then((m) => m.zhCn as Dictionary)
    : import('../locales/en').then((m) => m.en as Dictionary)
  )
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
