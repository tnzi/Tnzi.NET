import { defineStore } from 'pinia'
import { ref } from 'vue'
import 'pinia-plugin-persistedstate'

export type AdminLayoutMode = 'vertical' | 'vertical-mix' | 'horizontal'
export type PageTransition = 'fade' | 'slide-left' | 'slide-right' | 'zoom' | 'none'

const VALID_LAYOUT_MODES: AdminLayoutMode[] = ['vertical', 'vertical-mix', 'horizontal']
const VALID_TRANSITIONS: PageTransition[] = ['fade', 'slide-left', 'slide-right', 'zoom', 'none']

/**
 * Admin theme store — admin-specific theme knobs on top of the base theme
 * system from @tnzi/ui. This store does NOT own color state (that lives in
 * the ui package theme context); it owns admin-layout-specific toggles like
 * which layout mode is active, whether the tab bar is visible, etc.
 */
export const useAdminThemeStore = defineStore('admin-theme', () => {
  // Layout mode
  const layoutMode = ref<AdminLayoutMode>('vertical')

  // Visibility toggles
  const headerVisible = ref(true)
  const tabVisible = ref(true)
  const footerVisible = ref(true)
  const breadcrumbVisible = ref(true)

  // Sizing
  const siderWidth = ref(220)
  const siderCollapsedWidth = ref(64)
  const mixSiderWidth = ref(220)
  const headerHeight = ref(56)
  const tabHeight = ref(44)

  // Page transition
  const pageTransition = ref<PageTransition>('fade')

  // Inverted color scheme for sider / header (orthogonal to global dark mode)
  const invertSider = ref(false)
  const invertHeader = ref(false)

  function setLayoutMode(mode: AdminLayoutMode): void {
    if (VALID_LAYOUT_MODES.includes(mode)) {
      layoutMode.value = mode
    }
  }

  function setHeaderVisible(v: boolean): void {
    headerVisible.value = v
  }
  function setTabVisible(v: boolean): void {
    tabVisible.value = v
  }
  function setFooterVisible(v: boolean): void {
    footerVisible.value = v
  }
  function setBreadcrumbVisible(v: boolean): void {
    breadcrumbVisible.value = v
  }

  function setSiderWidth(w: number): void {
    siderWidth.value = w
  }
  function setSiderCollapsedWidth(w: number): void {
    siderCollapsedWidth.value = w
  }
  function setHeaderHeight(h: number): void {
    headerHeight.value = h
  }
  function setTabHeight(h: number): void {
    tabHeight.value = h
  }

  function setPageTransition(t: PageTransition): void {
    if (VALID_TRANSITIONS.includes(t)) {
      pageTransition.value = t
    }
  }

  function toggleInvertSider(): void {
    invertSider.value = !invertSider.value
  }
  function toggleInvertHeader(): void {
    invertHeader.value = !invertHeader.value
  }

  function reset(): void {
    layoutMode.value = 'vertical'
    headerVisible.value = true
    tabVisible.value = true
    footerVisible.value = true
    breadcrumbVisible.value = true
    siderWidth.value = 220
    siderCollapsedWidth.value = 64
    headerHeight.value = 56
    tabHeight.value = 44
    pageTransition.value = 'fade'
    invertSider.value = false
    invertHeader.value = false
  }

  return {
    layoutMode,
    headerVisible,
    tabVisible,
    footerVisible,
    breadcrumbVisible,
    siderWidth,
    siderCollapsedWidth,
    mixSiderWidth,
    headerHeight,
    tabHeight,
    pageTransition,
    invertSider,
    invertHeader,
    setLayoutMode,
    setHeaderVisible,
    setTabVisible,
    setFooterVisible,
    setBreadcrumbVisible,
    setSiderWidth,
    setSiderCollapsedWidth,
    setHeaderHeight,
    setTabHeight,
    setPageTransition,
    toggleInvertSider,
    toggleInvertHeader,
    reset,
  }
}, {
  persist: {
    key: 'tnzi-admin-theme',
    pick: [
      'layoutMode',
      'headerVisible',
      'tabVisible',
      'footerVisible',
      'breadcrumbVisible',
      'siderWidth',
      'siderCollapsedWidth',
      'pageTransition',
      'invertSider',
      'invertHeader',
    ],
  },
})
