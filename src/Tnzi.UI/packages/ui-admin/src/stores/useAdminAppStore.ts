import { defineStore } from 'pinia'
import { ref, watch, nextTick } from 'vue'
import { useBreakpoints, breakpointsTailwind } from '@vueuse/core'
import 'pinia-plugin-persistedstate'

/**
 * Admin app store — global UI state for the admin shell.
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
  const locale = ref<'en' | 'zh-cn'>('en')
  const fullContent = ref(false)
  const reloadFlag = ref(true)
  /**
   * Consumer-supplied locale messages, deep-merged on top of ui-admin's
   * bundled `en`/`zh-cn` dictionaries by `useAdminRouteStore.resolveI18nKey`.
   * Lets a host app (e.g. Acme) register its own
   * `admin.modules.{module}.{page}.title` keys so the sidebar / breadcrumb
   * / tabs resolve them in the active locale — without this slot, custom
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
   *    call it synchronously between `app.use(pinia)` and `app.mount('#app')`
   *    to keep that window down to one render frame — i.e. invisible.
   */
  const messageOverrides = ref<{ en: Record<string, unknown>; 'zh-cn': Record<string, unknown> }>({
    en: {},
    'zh-cn': {},
  })
  /**
   * Pin the second-level sub-sider when the layout is `vertical-mix`.
   * Default `false` — sub-sider auto-hides on mouseleave. When pinned,
   * the drawer stays open so the user can browse children freely.
   * Mirrors soybean-admin's `appStore.mixSiderFixed`.
   */
  const mixSiderFixed = ref(false)

  // Saved desktop config for mobile restoration
  const desktopSiderCollapseSnapshot = ref<boolean | null>(null)

  // Responsive breakpoints via @vueuse/core
  const bp = useBreakpoints(breakpointsTailwind)
  const isMobile = bp.smaller('md')
  const isTablet = bp.between('md', 'lg')
  const isDesktop = bp.greaterOrEqual('lg')

  // Auto-collapse the sider when the viewport is narrower than `lg`
  // (i.e. mobile OR tablet). On tablets the 220px sider eats nearly a
  // third of the screen, so collapsing to icon-rail keeps the content
  // area readable. Restoring to the previous state happens on the
  // first transition back to desktop.
  watch(
    isMobile,
    (mobile) => {
      if (mobile) {
        if (desktopSiderCollapseSnapshot.value === null) {
          desktopSiderCollapseSnapshot.value = siderCollapse.value
        }
        siderCollapse.value = true
      } else {
        if (desktopSiderCollapseSnapshot.value !== null) {
          siderCollapse.value = desktopSiderCollapseSnapshot.value
          desktopSiderCollapseSnapshot.value = null
        }
      }
    },
    { immediate: false },
  )
  watch(
    isTablet,
    (tablet) => {
      // Only act on the rising edge into tablet from desktop — mobile
      // already auto-collapses via the watcher above.
      if (tablet && !isMobile.value) {
        if (desktopSiderCollapseSnapshot.value === null) {
          desktopSiderCollapseSnapshot.value = siderCollapse.value
        }
        siderCollapse.value = true
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

  function setLocale(lang: 'en' | 'zh-cn'): void {
    locale.value = lang
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

  /**
   * Register additional locale messages on top of the bundled dictionaries.
   * Pass partial trees keyed by locale code; both locales are optional so
   * a host can extend just one if it only ships a single language. Subsequent
   * calls deep-merge — later keys win, but disjoint paths accumulate.
   *
   * Used by host apps to surface their own
   * `admin.modules.{module}.{page}.title` keys to the sidebar/breadcrumb/tabs.
   */
  function extendLocaleMessages(messages: {
    en?: Record<string, unknown>
    'zh-cn'?: Record<string, unknown>
  }): void {
    if (messages.en) {
      messageOverrides.value.en = deepMerge(messageOverrides.value.en, messages.en)
    }
    if (messages['zh-cn']) {
      messageOverrides.value['zh-cn'] = deepMerge(messageOverrides.value['zh-cn'], messages['zh-cn'])
    }
  }

  return {
    siderCollapse,
    locale,
    fullContent,
    reloadFlag,
    mixSiderFixed,
    messageOverrides,
    isMobile,
    isTablet,
    isDesktop,
    toggleSiderCollapse,
    setSiderCollapse,
    setLocale,
    toggleFullContent,
    reloadPage,
    toggleMixSiderFixed,
    setMixSiderFixed,
    extendLocaleMessages,
  }
}, {
  persist: {
    key: 'tnzi-admin-app',
    pick: ['siderCollapse', 'locale', 'mixSiderFixed'],
  },
})

/**
 * Recursively merge `ext` onto a clone of `base`. `ext` values win on key
 * conflict; plain objects deep-merge, arrays and primitives replace.
 * Kept inline (not extracted to a util) to avoid a circular dep — this is
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
