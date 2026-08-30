/**
 * Which languages this admin application offers.
 *
 * ## Why this exists
 *
 * The locale set used to be spelled out by hand in six places - the
 * `AdminLocale` union, the store's `locale` ref, `extendLocaleMessages`'s two
 * named fields, the header's `langOptions` array, the desktop tray's own copy
 * of that array, and `loadLocaleMessages`'s ternary. A consumer could not add a
 * language without editing all six, and could not remove one at all: an app
 * shipping English and French still showed 中文 in the switcher.
 *
 * Worse, four of those six failed SILENTLY. `extendLocaleMessages` dropped a
 * dictionary whose key it did not recognise, `loadLocaleMessages` installed the
 * ENGLISH pack under any unknown code, and both switcher arrays simply did not
 * offer the language. Nothing threw, nothing warned.
 *
 * So the set became one registry, and every one of those surfaces reads it.
 * A hand-copied list drifts entry by entry; a derived one cannot.
 *
 * ## Deliberately not here: the Naive UI bundle resolution
 *
 * A definition may carry a `naive` bundle, but this module only stores it - it
 * never imports `getNaiveLocale`. `translate.ts` (and through it this registry)
 * is reached from ~45 components, so anything imported here lands in the shell
 * chunk. Resolution lives in `headless/useAdminLocale`, which only the header,
 * the tray and the two config providers import.
 */
import { shallowRef, triggerRef } from 'vue'
import type { NaiveLocaleBundle } from '@tnzi/ui'
import {
  resetLocaleMessagesForTest,
  setLocaleLoader,
  setLocaleMessages,
  type AdminLocale,
} from './messages'

/** A language the admin shell offers, as supplied by the consumer. */
export interface AdminLocaleDefinition {
  /**
   * BCP-47-ish code, lower-cased by convention (`en`, `zh-cn`, `fr`,
   * `fr-ca`). Used as the dictionary key, the persisted value, and - unless
   * `intlTag` says otherwise - the `Intl` tag.
   */
  code: AdminLocale
  /**
   * What the switcher shows. Write the ENDONYM ("Français", not "French"):
   * a language menu is read by someone who cannot yet read the current
   * interface language.
   */
  label: string
  /**
   * Dictionary for this locale, installed synchronously at registration.
   * Omit for the two bundled codes (`en` / `zh-cn`) - their packs are async
   * chunks and load on demand. Use `loadMessages` instead when your own
   * dictionary is big enough to be worth splitting.
   */
  messages?: Record<string, unknown>
  /**
   * Lazy alternative to `messages` - called at most once, when the locale
   * first becomes active. Lets a consumer's dictionary sit in its own chunk
   * exactly like the bundled ones do.
   */
  loadMessages?: () => Promise<Record<string, unknown>>
  /**
   * Naive UI's own component strings (pagination, date picker, empty states).
   * naive-ui ships these per language - `import { frFR, dateFrFR } from 'naive-ui'`
   * - and the framework cannot import all ~50 of them speculatively, so pass
   * the pair for any locale it does not bundle. Omitted → the shell resolves
   * `en`/`zh-cn` itself and falls back to English for everything else.
   */
  naive?: NaiveLocaleBundle
  /**
   * Tag handed to `Intl.*` for dates and numbers, when it differs from `code`.
   * `en` → `en-US` and `zh-cn` → `zh-CN` are applied for the bundled two;
   * every other code is its own tag unless overridden.
   */
  intlTag?: string
}

/** A registered locale, with `intlTag` filled in. */
export interface RegisteredAdminLocale extends AdminLocaleDefinition {
  intlTag: string
}

/**
 * The two dictionaries this package ships. Registered by default so an app
 * that says nothing about languages behaves exactly as it did before the
 * registry existed.
 */
const BUNDLED: readonly RegisteredAdminLocale[] = Object.freeze([
  Object.freeze({ code: 'en', label: 'English', intlTag: 'en-US' }),
  Object.freeze({ code: 'zh-cn', label: '中文', intlTag: 'zh-CN' }),
]) as readonly RegisteredAdminLocale[]

/**
 * Reactive: the switcher is a `computed` over this, so a consumer registering
 * languages during `install()` repaints the menu rather than freezing whatever
 * was there at first render. `shallowRef` + `triggerRef` for the same reason
 * `messages.ts` uses it - the entries are replaced wholesale, never patched.
 */
const registry = shallowRef<readonly RegisteredAdminLocale[]>(BUNDLED)

function normalise(def: AdminLocaleDefinition): RegisteredAdminLocale {
  const bundled = BUNDLED.find((b) => b.code === def.code)
  return {
    ...def,
    intlTag: def.intlTag ?? bundled?.intlTag ?? def.code,
  }
}

/**
 * Declare the languages this application offers.
 *
 * The array IS the set, in switcher order - it does not merge with the
 * bundled two. That is the point: an app that supports English and French
 * passes both and gets exactly those, with no 中文 entry to explain to a
 * coordinator. Passing an empty array is ignored (an admin shell with no
 * language is not a state worth honouring); to offer a single language, pass
 * one entry - the switcher hides itself when only one locale is registered.
 *
 * Call it once, before `app.mount()`. `defineAdminApp({ localeOptions })` does
 * it for you at the right moment; reach for this directly only when
 * assembling a shell by hand.
 */
export function registerAdminLocales(definitions: readonly AdminLocaleDefinition[]): void {
  if (!definitions.length) return
  const resolved = definitions.map(normalise)
  registry.value = Object.freeze(resolved)
  triggerRef(registry)
  for (const def of resolved) installMessages(def)
}

/**
 * Add one language, keeping everything already registered. Use this to extend
 * the bundled two rather than replace them.
 */
export function registerAdminLocale(definition: AdminLocaleDefinition): void {
  const resolved = normalise(definition)
  const rest = registry.value.filter((l) => l.code !== resolved.code)
  registry.value = Object.freeze([...rest, resolved])
  triggerRef(registry)
  installMessages(resolved)
}

/**
 * Hand any dictionary the definition carries to the message registry. Eager
 * `messages` install immediately; `loadMessages` is registered as a loader so
 * `loadLocaleMessages(code)` picks it up when the locale first becomes active.
 */
function installMessages(def: RegisteredAdminLocale): void {
  if (def.messages) {
    setLocaleMessages(def.code, def.messages)
  } else if (def.loadMessages) {
    setLocaleLoader(def.code, def.loadMessages)
  }
}

/** Every registered locale, in switcher order. */
export function getRegisteredAdminLocales(): readonly RegisteredAdminLocale[] {
  return registry.value
}

/** One registered locale, or `undefined` when the code is not offered. */
export function getRegisteredAdminLocale(code: AdminLocale): RegisteredAdminLocale | undefined {
  return registry.value.find((l) => l.code === code)
}

/** Whether `code` is one of the languages this application offers. */
export function isAdminLocaleRegistered(code: AdminLocale): boolean {
  return registry.value.some((l) => l.code === code)
}

/**
 * The locale to fall back to - the first registered one.
 *
 * Needed because `locale` is PERSISTED. An app that drops a language (or a
 * user whose localStorage predates the change) would otherwise boot pinned to
 * a locale with no dictionary, no switcher entry, and no way back.
 */
export function getDefaultAdminLocale(): AdminLocale {
  return registry.value[0]?.code ?? 'en'
}

/**
 * Reset to the two bundled locales AND drop any dictionary those extra locales
 * installed. Test-only - a module-level registry outlives a component mount,
 * so a spec that registers `fr` would otherwise leak it into every spec that
 * runs after it.
 */
export function resetAdminLocalesForTest(): void {
  registry.value = BUNDLED
  triggerRef(registry)
  resetLocaleMessagesForTest()
}
