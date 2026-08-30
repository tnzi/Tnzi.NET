import { defineStore } from 'pinia'
import { ref, watch, nextTick } from 'vue'
import { useBreakpoints, breakpointsTailwind } from '@vueuse/core'
import 'pinia-plugin-persistedstate'
import { getDefaultAdminLocale, isAdminLocaleRegistered } from '../i18n/locale-registry'
import type { AdminLocale } from '../i18n/messages'

/**
 * Admin app store - global UI state for the admin shell.
 *
 * Responsibilities:
 * - Sider collapse state (persisted)
 * - Locale (persisted)
 * - Responsive breakpoint detection (isMobile/isTablet/isDesktop)
 * - Full content mode (hides sider + header for distraction-free view)
 * - Reload flag for forcing page re-render (used by tab close/refresh)
 *
 * Pattern: Pinia setup store, supports pinia-plugin-persistedstate for persistence.
 */
export const useAdminAppStore = defineStore('admin-app', () => {
  // State
  const siderCollapse = ref(false)
  const locale = ref<AdminLocale>('en')
  const fullContent = ref(false)
  const reloadFlag = ref(true)
  /**
   * Consumer-supplied locale messages, keyed by locale code and deep-merged on
   * top of whatever dictionary that code resolves to, by every chrome path
   * that translates a key (`resolveI18nKey`, `translatePageKey`,
   * `translateChromeKey`). Any code is accepted, including one the framework
   * ships no dictionary for.
   * Lets a host app register its own
   * `admin.modules.{module}.{page}.title` keys so the sidebar / breadcrumb
   * / tabs resolve them in the active locale - without this slot, custom
   * route titles would only humanise to English regardless of locale.
   *
   * Mutated through `extendLocaleMessages`.
   *
   * Intentionally NOT in `persist.pick` below. Trade-off:
   *  - Pro: avoids serialising a large dictionary tree into localStorage
   *    and dodges the stale-translations-across-deploys footgun (host bumps
   *    their bundle's i18n, but persisted overrides stick around forever).
   *  - Con: on a hard reload, the very first frame of the sidebar / tabs /
   *    breadcrumb sees an empty override map and humanises host-app route
   *    titles to English until `extendLocaleMessages` runs. Host apps MUST
   *    call it synchronously between `install()` and `app.mount('#app')`
   *    to keep that window down to one render frame - i.e. invisible.
   *    ⚠️ AFTER `install()`, never before: install() registers
   *    pinia-plugin-persistedstate, and persistence only attaches to stores
   *    created after that registration - touching this store earlier
   *    silently disables persistence for the WHOLE admin-app store
   *    (sider collapse / locale / ops view stop surviving reloads).
   */
  const messageOverrides = ref<Record<AdminLocale, Record<string, unknown>>>({
    en: {},
    'zh-cn': {},
  })
  /**
   * Pin the second-level sub-sider when the layout is `vertical-mix`.
   * Default `false` - sub-sider auto-hides on mouseleave. When pinned,
   * the drawer stays open so the user can browse children freely.
   * Mirrors soybean-admin's `appStore.mixSiderFixed`.
   */
  const mixSiderFixed = ref(false)
  /**
   * Sidebar toggle: show or hide the FRAMEWORK BUILT-IN admin menus (the
   * route groups shipped by `defaultAdminRoutes`, stamped `meta.builtIn`).
   * OFF leaves only the consumer app's own menus plus neutral built-ins
   * (the landing dashboard). DISPLAY ONLY - navigation guards and deep
   * links are untouched, so hidden pages stay reachable by URL (no lockout
   * path). Default ON. Only consumed when the signed-in user is a super
   * admin (the toggle renders super-admin-only); harmless residue for
   * everyone else.
   */
  const showBuiltInMenus = ref(true)

  // Saved desktop config for mobile restoration
  const desktopSiderCollapseSnapshot = ref<boolean | null>(null)

  // Responsive breakpoints via @vueuse/core
  const bp = useBreakpoints(breakpointsTailwind)
  const isMobile = bp.smaller('md')
  const isTablet = bp.between('md', 'lg')
  const isDesktop = bp.greaterOrEqual('lg')
  // "Below desktop" = mobile OR tablet (viewport < lg / 1024px). Drives
  // the auto-collapse watcher below.
  const isBelowDesktop = bp.smaller('lg')

  // Auto-collapse the sider whenever the viewport is narrower than `lg`
  // (mobile OR tablet): a 220px sider eats nearly a third of a tablet
  // screen, and on mobile the sider becomes an overlay drawer. A SINGLE
  // backup/restore watcher (mirroring soybean-admin's `isMobile` watcher)
  // snapshots the desktop collapse state on the way into a narrow viewport
  // and restores it the moment the viewport widens back to desktop - so
  // temporarily shrinking the window (desktop → tablet → desktop, without
  // ever crossing into the mobile drawer) no longer leaves the sider stuck
  // collapsed. The earlier two-watcher split had a collapse branch for the
  // tablet range but NO restore branch, which is exactly why widening from
  // tablet back to desktop never reopened the sider.
  watch(
    isBelowDesktop,
    (narrow) => {
      if (narrow) {
        if (desktopSiderCollapseSnapshot.value === null) {
          desktopSiderCollapseSnapshot.value = siderCollapse.value
        }
        siderCollapse.value = true
      } else if (desktopSiderCollapseSnapshot.value !== null) {
        siderCollapse.value = desktopSiderCollapseSnapshot.value
        desktopSiderCollapseSnapshot.value = null
      }
    },
    { immediate: false },
  )

  // Actions
  function toggleSiderCollapse(): void {
    siderCollapse.value = !siderCollapse.value
  }

  function setSiderCollapse(value: boolean): void {
    siderCollapse.value = value
  }

  function setLocale(lang: AdminLocale): void {
    locale.value = lang
  }

  /**
   * Drop a persisted locale the application no longer offers.
   *
   * `locale` survives reloads, so an app that stops shipping a language - or a
   * user whose localStorage predates that change - would boot pinned to a code
   * with no dictionary, no entry in the switcher, and therefore no way back to
   * a language they can read. Called by `defineAdminApp().install()` once the
   * consumer's `localeOptions` are registered.
   */
  function ensureLocaleRegistered(): void {
    if (!isAdminLocaleRegistered(locale.value)) {
      locale.value = getDefaultAdminLocale()
    }
  }

  function toggleFullContent(): void {
    fullContent.value = !fullContent.value
  }

  async function reloadPage(): Promise<void> {
    reloadFlag.value = false
    await nextTick()
    reloadFlag.value = true
  }

  function toggleMixSiderFixed(): void {
    mixSiderFixed.value = !mixSiderFixed.value
  }
  function setMixSiderFixed(v: boolean): void {
    mixSiderFixed.value = v
  }

  function toggleBuiltInMenus(): void {
    showBuiltInMenus.value = !showBuiltInMenus.value
  }
  function setShowBuiltInMenus(v: boolean): void {
    showBuiltInMenus.value = v
  }

  /**
   * Register additional locale messages on top of the bundled dictionaries.
   * Pass partial trees keyed by locale code - ANY code, not just the two this
   * package bundles. Subsequent calls deep-merge: later keys win, disjoint
   * paths accumulate.
   *
   * Used by host apps to surface their own
   * `admin.modules.{module}.{page}.title` keys to the sidebar/breadcrumb/tabs,
   * and to supply the whole dictionary for a language the framework does not
   * ship.
   *
   * ⚠️ This used to check for `en` and `'zh-cn'` BY NAME and merge only those,
   * so `extendLocaleMessages({ fr })` was dropped on the floor - no error, no
   * warning, and `createAdminApp({ locales })` is the documented way to supply
   * consumer messages. Iterate the entries; never re-introduce a name check.
   */
  function extendLocaleMessages(
    messages: Partial<Record<AdminLocale, Record<string, unknown>>>,
  ): void {
    for (const [code, tree] of Object.entries(messages)) {
      if (!tree) continue
      messageOverrides.value[code] = deepMerge(messageOverrides.value[code] ?? {}, tree)
    }
  }

  return {
    siderCollapse,
    locale,
    fullContent,
    reloadFlag,
    mixSiderFixed,
    showBuiltInMenus,
    messageOverrides,
    isMobile,
    isTablet,
    isDesktop,
    toggleSiderCollapse,
    setSiderCollapse,
    setLocale,
    ensureLocaleRegistered,
    toggleFullContent,
    reloadPage,
    toggleMixSiderFixed,
    setMixSiderFixed,
    toggleBuiltInMenus,
    setShowBuiltInMenus,
    extendLocaleMessages,
  }
}, {
  persist: {
    key: 'tnzi-admin-app',
    pick: ['siderCollapse', 'locale', 'mixSiderFixed', 'showBuiltInMenus'],
  },
})

/**
 * Recursively merge `ext` onto a clone of `base`. `ext` values win on key
 * conflict; plain objects deep-merge, arrays and primitives replace.
 * Kept inline (not extracted to a util) to avoid a circular dep - this is
 * the only call site inside ui-admin.
 */
function deepMerge(
  base: Record<string, unknown>,
  ext: Record<string, unknown>,
): Record<string, unknown> {
  const out: Record<string, unknown> = { ...base }
  for (const [k, v] of Object.entries(ext)) {
    const cur = out[k]
    if (isPlainObject(v) && isPlainObject(cur)) {
      out[k] = deepMerge(cur, v)
    } else {
      out[k] = v
    }
  }
  return out
}

function isPlainObject(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v)
}
