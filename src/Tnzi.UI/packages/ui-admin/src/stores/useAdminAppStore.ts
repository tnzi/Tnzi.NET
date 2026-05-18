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

  // Auto-collapse on mobile, restore on desktop
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

  return {
    siderCollapse,
    locale,
    fullContent,
    reloadFlag,
    mixSiderFixed,
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
  }
}, {
  persist: {
    key: 'tnzi-admin-app',
    pick: ['siderCollapse', 'locale', 'mixSiderFixed'],
  },
})
