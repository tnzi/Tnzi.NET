/**
 * The one source the language switcher and the Naive UI locale binding read.
 *
 * There used to be two hand-written `langOptions` arrays - one in the header,
 * one in the desktop taskbar tray - each a two-item literal whose comment said
 * "when a new locale is added, extend this list". A hand-copied list drifts
 * entry by entry; this package has watched that happen to `permission`,
 * `moduleGate`, `builtIn`, `hideInTab`, `color` and `size`. So both switchers
 * now render whatever `i18n/locale-registry` holds, and neither knows the name
 * of a single language.
 *
 * Naive UI resolution lives here rather than in the registry because
 * `getNaiveLocale` pulls Naive's own locale objects, and the registry is
 * reachable from `i18n/translate` - which ~45 components import. Only the
 * header, the tray and the two config providers import this file.
 */
import { computed, type ComputedRef } from 'vue'
import { getNaiveLocale, type NaiveLocaleBundle } from '@tnzi/ui'
import { useAdminAppStore } from '../stores/useAdminAppStore'
import {
  getDefaultAdminLocale,
  getRegisteredAdminLocale,
  getRegisteredAdminLocales,
} from '../i18n/locale-registry'
import type { AdminLocale } from '../i18n/messages'

/** One entry of the language switcher. */
export interface AdminLocaleOption {
  code: AdminLocale
  label: string
  /** Whether this is the locale currently in use. */
  active: boolean
}

export interface UseAdminLocaleReturn {
  /** The active locale code. */
  current: ComputedRef<AdminLocale>
  /** Every language this application offers, in switcher order. */
  options: ComputedRef<AdminLocaleOption[]>
  /**
   * Whether a switcher is worth rendering at all. One language is not a
   * choice, and a menu with a single item that cannot change anything reads as
   * a broken control.
   */
  hasChoice: ComputedRef<boolean>
  /** Naive UI's own strings for the active locale. */
  naive: ComputedRef<NaiveLocaleBundle>
  /** `Intl` tag for the active locale - clocks, calendars, number formatting. */
  intlTag: ComputedRef<string>
  /** Switch language. Unknown codes are ignored rather than stranding the UI. */
  setLocale: (code: AdminLocale) => void
}

export function useAdminLocale(): UseAdminLocaleReturn {
  // `useAdminAppStore()` needs an active Pinia. `TAdminAppRoot` - the outermost
  // config provider, and the first thing that reads this - is mounted bare in
  // tests and can be mounted bare by a consumer assembling its own shell, so a
  // missing store degrades to the default locale rather than taking the whole
  // provider stack down. Same guard `i18n/translate` has carried all along.
  let appStore: ReturnType<typeof useAdminAppStore> | null = null
  try {
    appStore = useAdminAppStore()
  } catch {
    appStore = null
  }

  const current = computed<AdminLocale>(() => appStore?.locale ?? getDefaultAdminLocale())

  const options = computed<AdminLocaleOption[]>(() =>
    getRegisteredAdminLocales().map((l) => ({
      code: l.code,
      label: l.label,
      active: l.code === current.value,
    })),
  )

  const hasChoice = computed(() => options.value.length > 1)

  /**
   * The definition's own bundle first, then Naive's own `en`/`zh-cn` mapping.
   *
   * The framework cannot speculatively import all ~50 locales Naive ships just
   * in case one is selected, so a consumer registering `fr` passes
   * `{ locale: frFR, dateLocale: dateFrFR }` at registration. Without one,
   * `getNaiveLocale` falls back to English - documented, and visible only in
   * Naive's own strings (pagination, date picker, empty states).
   */
  const naive = computed<NaiveLocaleBundle>(
    () => getRegisteredAdminLocale(current.value)?.naive ?? getNaiveLocale(current.value),
  )

  const intlTag = computed(
    () => getRegisteredAdminLocale(current.value)?.intlTag ?? current.value,
  )

  function setLocale(code: AdminLocale): void {
    if (!appStore || !getRegisteredAdminLocale(code)) return
    appStore.setLocale(code)
  }

  return { current, options, hasChoice, naive, intlTag, setLocale }
}
