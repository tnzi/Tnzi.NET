import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import 'pinia-plugin-persistedstate'

/**
 * Admin layout modes — 6 variants modeled after soybean-admin.
 *
 * - `vertical`              — left sidebar (default)
 * - `horizontal`            — top menu only, no sidebar
 * - `vertical-mix`          — narrow first-level sidebar + sub-sidebar with children
 * - `vertical-hybrid-header-first` — top first-level menu + sidebar always shows children
 * - `top-hybrid-sidebar-first`     — top first-level menu + sidebar with children (sidebar always visible)
 * - `top-hybrid-header-first`      — top first-level menu + sidebar conditionally (only if active first-level has children)
 */
export type AdminLayoutMode =
  | 'vertical'
  | 'horizontal'
  | 'vertical-mix'
  | 'vertical-hybrid-header-first'
  | 'top-hybrid-sidebar-first'
  | 'top-hybrid-header-first'

/**
 * Page transition names. The first six match soybean-admin's signature route
 * animations and are the new (0.2.12+) defaults. The next four (`slide-left`,
 * `slide-right`, `zoom`, `none`) are retained for backwards compatibility with
 * 0.2.x consumers.
 */
export type PageTransition =
  | 'fade'
  | 'fade-slide'
  | 'fade-bottom'
  | 'fade-scale'
  | 'zoom-fade'
  | 'zoom-out'
  | 'slide-left'
  | 'slide-right'
  | 'zoom'
  | 'none'

// Phase G — soybean ships three tab styles: chrome (SVG-arc tabs),
// button (rounded-rect chip with border), slider (transparent chip with
// 2px primary bottom border on active). Phase C accidentally dropped
// 'slider' on the assumption that it was visually identical to chrome —
// the diagnosis G2 confirmed slider is independent (no SVG arc, the
// active accent is a 2px bottom border + 10% primary bg fill).
export type TabStyle = 'chrome' | 'button' | 'slider'

export interface WatermarkSettings {
  enabled: boolean
  text: string
  includeUserName: boolean
  includeDate: boolean
  opacity: number
  fontSize: number
}

const VALID_LAYOUT_MODES: AdminLayoutMode[] = [
  'vertical',
  'horizontal',
  'vertical-mix',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
]
const VALID_TRANSITIONS: PageTransition[] = [
  'fade',
  'fade-slide',
  'fade-bottom',
  'fade-scale',
  'zoom-fade',
  'zoom-out',
  'slide-left',
  'slide-right',
  'zoom',
  'none',
]
const VALID_TAB_STYLES: TabStyle[] = ['chrome', 'button', 'slider']

const DEFAULT_WATERMARK: WatermarkSettings = {
  enabled: false,
  text: 'Tnzi Admin',
  includeUserName: true,
  includeDate: true,
  opacity: 0.15,
  fontSize: 16,
}

/**
 * Admin theme store — admin-specific theme knobs on top of the base theme
 * system from `@tnzi/ui` (which owns color tokens + dark mode).
 *
 * This store does NOT own color state. It owns admin-specific layout toggles:
 * layout mode, sider/header/tab sizing, watermark, page transition style.
 */
export const useAdminThemeStore = defineStore('admin-theme', () => {
  // Layout mode
  const layoutMode = ref<AdminLayoutMode>('vertical')

  // Visibility toggles
  const headerVisible = ref(true)
  const tabVisible = ref(true)
  const footerVisible = ref(true)
  const breadcrumbVisible = ref(true)

  // Sizing — 4-tier sider width system (matches soybean-admin).
  // 240px chosen so 16-char menu labels (e.g. "Organization Management",
  // "Session Management") fit without truncation. Soybean uses 220px but
  // its labels are 4-char CJK strings — Tnzi's English labels need more
  // horizontal room.
  const siderWidth = ref(220)
  const siderCollapsedWidth = ref(60)
  /** Width of the first-level rail in vertical-mix mode (narrow, icon-first). */
  const mixSiderWidth = ref(90)
  /** Width of the first-level rail when the sider is collapsed (vertical-mix). */
  const mixCollapsedWidth = ref(64)
  /** Width of the second-level drawer in vertical-mix mode. */
  const mixChildMenuWidth = ref(200)
  /** Phase H2 H23: when true, clicking a hybrid first-level menu item
      auto-navigates to its deepest leaf. Mirrors soybean's
      `themeStore.sider.autoSelectFirstMenu`. */
  const autoSelectFirstMenu = ref(true)
  const headerHeight = ref(50)
  const tabHeight = ref(44)
  const footerHeight = ref(42)

  // Tab style (chrome / button / slider)
  const tabStyle = ref<TabStyle>('chrome')

  // Page transition
  const pageTransition = ref<PageTransition>('fade-slide')
  const pageAnimate = ref(true)

  // Theme radius (Phase I.6.6) — drives --tnzi-admin-radius* globally
  // (sm/md/lg derived by scale in setThemeRadius). 0-16px range.
  const themeRadius = ref(4)

  // Inverted color scheme for sider (orthogonal to global dark mode).
  // Note: soybean only inverts the sider — the header always follows the
  // global dark/light mode. We removed `invertHeader` because our previous
  // implementation produced black-bg + grey-icon visual garbage (children
  // had higher CSS specificity than the wrapper hack), and there was no
  // soybean reference to align against.
  const invertSider = ref(false)

  // Fixed positioning
  const fixedHeader = ref(true)
  const fixedTab = ref(true)
  const fixedFooter = ref(false)

  // Watermark
  const watermark = ref<WatermarkSettings>({ ...DEFAULT_WATERMARK })

  // Phase D — additional toggles & settings parity with soybean.
  /** Whether the swatches list in the appearance tab should suggest a
      curated palette derived from the current primary color. UI-only
      (consumers can read this to choose between curated vs preset list). */
  const recommendColor = ref(true)
  /** When true, info color tracks primary on every primary change. */
  const infoFollowPrimary = ref(false)
  /** Whether keep-alive caching is applied to tab pages (route-based). */
  const tabCache = ref(true)
  /** Whether the breadcrumb shows the menu icon next to each segment. */
  const breadcrumbShowIcon = ref(true)
  /** Visibility of the language switcher in the header. */
  const multilingualVisible = ref(true)
  /** Visibility of the global search shortcut in the header. */
  const globalSearchVisible = ref(true)
  /** Visibility of the fullscreen toggle in the header. */
  const fullscreenVisible = ref(true)
  /** Visibility of the theme schema (light/dark/auto) cycle button. */
  const themeSchemaVisible = ref(true)
  /** Visibility of the page reload button in the header. */
  const reloadVisible = ref(false)
  /** Phase F — full-page grayscale filter (accessibility / mourning mode). */
  const grayscale = ref(false)
  /** Phase F — full-page color-weakness simulation (invert filter). */
  const colourWeakness = ref(false)
  /** Phase G — close tab via middle mouse click (soybean's
      `themeStore.tab.closeTabByMiddleClick`). Defaults to false to
      match soybean. */
  const closeTabByMiddleClick = ref(false)
  /** Phase H1 A2: how the layout scrolls.
      - `'content'` (default, matches soybean): only the main content
        area scrolls; header / tab / footer stay pinned to the chrome.
      - `'wrapper'`: the entire page scrolls (window scroll). In this
        mode `fixedHeaderAndTab` / `footer.fixed` decide whether the
        header/tab/footer pin to the viewport top/bottom while
        scrolling. soybean's `theme.layout.scrollMode`. */
  const scrollMode = ref<'content' | 'wrapper'>('content')

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
  function setMixSiderWidth(w: number): void {
    mixSiderWidth.value = w
  }
  function setMixCollapsedWidth(w: number): void {
    mixCollapsedWidth.value = w
  }
  function setMixChildMenuWidth(w: number): void {
    mixChildMenuWidth.value = w
  }
  function setAutoSelectFirstMenu(v: boolean): void {
    autoSelectFirstMenu.value = v
  }
  function setHeaderHeight(h: number): void {
    headerHeight.value = h
  }
  function setTabHeight(h: number): void {
    tabHeight.value = h
  }
  function setFooterHeight(h: number): void {
    footerHeight.value = h
    // CSS var written by the watcher near the bottom of the setup
    // function — single source of truth.
  }

  function setTabStyle(s: TabStyle): void {
    if (VALID_TAB_STYLES.includes(s)) {
      tabStyle.value = s
    }
  }

  function setPageTransition(t: PageTransition): void {
    if (VALID_TRANSITIONS.includes(t)) {
      pageTransition.value = t
    }
  }
  function setPageAnimate(v: boolean): void {
    pageAnimate.value = v
  }
  function setThemeRadius(r: number): void {
    themeRadius.value = Math.max(0, Math.min(16, Math.round(r)))
    // CSS variables are written by the `watch(themeRadius, ...)` block
    // further down — single source of truth so persisted-state hydration
    // and user setter calls take the same path.
  }

  function toggleInvertSider(): void {
    invertSider.value = !invertSider.value
  }

  function setFixedHeader(v: boolean): void {
    fixedHeader.value = v
  }
  function setFixedTab(v: boolean): void {
    fixedTab.value = v
  }
  function setFixedFooter(v: boolean): void {
    fixedFooter.value = v
  }

  function setWatermark(patch: Partial<WatermarkSettings>): void {
    watermark.value = { ...watermark.value, ...patch }
  }
  function resetWatermark(): void {
    watermark.value = { ...DEFAULT_WATERMARK }
  }

  // Phase D — setters for the new toggles.
  function setRecommendColor(v: boolean): void {
    recommendColor.value = v
  }
  function setInfoFollowPrimary(v: boolean): void {
    infoFollowPrimary.value = v
  }
  function setTabCache(v: boolean): void {
    tabCache.value = v
  }
  function setBreadcrumbShowIcon(v: boolean): void {
    breadcrumbShowIcon.value = v
  }
  function setMultilingualVisible(v: boolean): void {
    multilingualVisible.value = v
  }
  function setGlobalSearchVisible(v: boolean): void {
    globalSearchVisible.value = v
  }
  function setFullscreenVisible(v: boolean): void {
    fullscreenVisible.value = v
  }
  function setThemeSchemaVisible(v: boolean): void {
    themeSchemaVisible.value = v
  }
  function setReloadVisible(v: boolean): void {
    reloadVisible.value = v
  }

  // Phase F — accessibility filters. Applies/strips the CSS filter on
  // documentElement so the entire page renders through the chosen lens.
  // Mirrors soybean's `toggleAuxiliaryColorModes` shared.ts:191-196.
  function applyAuxFilter(): void {
    if (typeof document === 'undefined') return
    const filters: string[] = []
    if (grayscale.value) filters.push('grayscale(100%)')
    if (colourWeakness.value) filters.push('invert(80%)')
    document.documentElement.style.filter = filters.join(' ') || ''
  }
  function setGrayscale(v: boolean): void {
    grayscale.value = v
    applyAuxFilter()
  }
  function setColourWeakness(v: boolean): void {
    colourWeakness.value = v
    applyAuxFilter()
  }
  function setCloseTabByMiddleClick(v: boolean): void {
    closeTabByMiddleClick.value = v
  }
  function setScrollMode(v: 'content' | 'wrapper'): void {
    if (v === 'content' || v === 'wrapper') scrollMode.value = v
  }

  /**
   * Apply a theme preset (Phase I.6.6) — partial patch of layout / radius /
   * scheme / transition. Color values must be applied via the base
   * `@tnzi/ui` theme store (this store doesn't own colors); consumers
   * typically call `themeStore.setPrimary(preset.primaryColor)` themselves
   * after calling applyPreset to keep the two stores in sync.
   */
  function applyPreset(preset: {
    themeRadius?: number
    layout?: {
      siderWidth?: number
      siderCollapsedWidth?: number
      headerHeight?: number
      tabHeight?: number
    }
    pageTransition?: PageTransition
  }): void {
    if (typeof preset.themeRadius === 'number') {
      setThemeRadius(preset.themeRadius)
    }
    if (preset.layout) {
      if (typeof preset.layout.siderWidth === 'number') {
        siderWidth.value = preset.layout.siderWidth
      }
      if (typeof preset.layout.siderCollapsedWidth === 'number') {
        siderCollapsedWidth.value = preset.layout.siderCollapsedWidth
      }
      if (typeof preset.layout.headerHeight === 'number') {
        headerHeight.value = preset.layout.headerHeight
      }
      if (typeof preset.layout.tabHeight === 'number') {
        tabHeight.value = preset.layout.tabHeight
      }
    }
    if (preset.pageTransition && VALID_TRANSITIONS.includes(preset.pageTransition)) {
      pageTransition.value = preset.pageTransition
    }
  }

  function reset(): void {
    layoutMode.value = 'vertical'
    themeRadius.value = 4
    headerVisible.value = true
    tabVisible.value = true
    footerVisible.value = true
    breadcrumbVisible.value = true
    siderWidth.value = 220
    siderCollapsedWidth.value = 60
    mixSiderWidth.value = 90
    mixCollapsedWidth.value = 64
    mixChildMenuWidth.value = 200
    autoSelectFirstMenu.value = true
    headerHeight.value = 50
    tabHeight.value = 44
    footerHeight.value = 42
    tabStyle.value = 'chrome'
    pageTransition.value = 'fade-slide'
    pageAnimate.value = true
    invertSider.value = false
    fixedHeader.value = true
    fixedTab.value = true
    fixedFooter.value = false
    watermark.value = { ...DEFAULT_WATERMARK }
    recommendColor.value = true
    infoFollowPrimary.value = false
    tabCache.value = true
    breadcrumbShowIcon.value = true
    multilingualVisible.value = true
    globalSearchVisible.value = true
    fullscreenVisible.value = true
    themeSchemaVisible.value = true
    reloadVisible.value = false
    grayscale.value = false
    colourWeakness.value = false
    closeTabByMiddleClick.value = false
    scrollMode.value = 'content'
    if (typeof document !== 'undefined') {
      document.documentElement.style.filter = ''
    }
  }

  // ── Init + reactive CSS-var sync ──
  // The CSS variables (`--tnzi-admin-radius-*`, `--tnzi-admin-footer-height`)
  // are only ever written by the setter functions. Without watchers,
  //   - fresh page loads keep the polish.css fallback literals (4/8/12,
  //     48px) instead of the store's initial values, and
  //   - piniaPluginPersistedstate hydration silently bypasses setters,
  //     so a persisted custom value won't repaint.
  // Watching with `immediate: true` covers both: first call pushes the
  // initial values, subsequent calls catch persistent-state restores.
  if (typeof document !== 'undefined') {
    watch(
      themeRadius,
      (r) => {
        const clamped = Math.max(0, Math.min(16, Math.round(r)))
        const style = document.documentElement.style
        style.setProperty('--tnzi-admin-radius', `${clamped}px`)
        style.setProperty('--tnzi-admin-radius-sm', `${Math.max(0, Math.floor(clamped * 0.5))}px`)
        style.setProperty('--tnzi-admin-radius-md', `${clamped}px`)
        style.setProperty('--tnzi-admin-radius-lg', `${clamped + 4}px`)
      },
      { immediate: true },
    )
    watch(
      footerHeight,
      (h) => {
        document.documentElement.style.setProperty('--tnzi-admin-footer-height', `${h}px`)
      },
      { immediate: true },
    )
  }

  return {
    // state
    layoutMode,
    headerVisible,
    tabVisible,
    footerVisible,
    breadcrumbVisible,
    siderWidth,
    siderCollapsedWidth,
    mixSiderWidth,
    mixCollapsedWidth,
    mixChildMenuWidth,
    autoSelectFirstMenu,
    headerHeight,
    tabHeight,
    footerHeight,
    tabStyle,
    pageTransition,
    pageAnimate,
    themeRadius,
    invertSider,
    fixedHeader,
    fixedTab,
    fixedFooter,
    watermark,
    recommendColor,
    infoFollowPrimary,
    tabCache,
    breadcrumbShowIcon,
    multilingualVisible,
    globalSearchVisible,
    fullscreenVisible,
    themeSchemaVisible,
    reloadVisible,
    grayscale,
    colourWeakness,
    closeTabByMiddleClick,
    scrollMode,
    // setters
    setLayoutMode,
    setHeaderVisible,
    setTabVisible,
    setFooterVisible,
    setBreadcrumbVisible,
    setSiderWidth,
    setSiderCollapsedWidth,
    setMixSiderWidth,
    setMixCollapsedWidth,
    setMixChildMenuWidth,
    setAutoSelectFirstMenu,
    setHeaderHeight,
    setTabHeight,
    setFooterHeight,
    setTabStyle,
    setPageTransition,
    setPageAnimate,
    setThemeRadius,
    applyPreset,
    toggleInvertSider,
    setFixedHeader,
    setFixedTab,
    setFixedFooter,
    setWatermark,
    resetWatermark,
    setRecommendColor,
    setInfoFollowPrimary,
    setTabCache,
    setBreadcrumbShowIcon,
    setMultilingualVisible,
    setGlobalSearchVisible,
    setFullscreenVisible,
    setThemeSchemaVisible,
    setReloadVisible,
    setGrayscale,
    setColourWeakness,
    setCloseTabByMiddleClick,
    setScrollMode,
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
      'mixSiderWidth',
      'mixCollapsedWidth',
      'mixChildMenuWidth',
      'autoSelectFirstMenu',
      'headerHeight',
      'tabHeight',
      'footerHeight',
      'tabStyle',
      'pageTransition',
      'pageAnimate',
      'themeRadius',
      'invertSider',
      'fixedHeader',
      'fixedTab',
      'fixedFooter',
      'watermark',
      'recommendColor',
      'infoFollowPrimary',
      'tabCache',
      'breadcrumbShowIcon',
      'multilingualVisible',
      'globalSearchVisible',
      'fullscreenVisible',
      'themeSchemaVisible',
      'reloadVisible',
      'grayscale',
      'colourWeakness',
      'closeTabByMiddleClick',
      'scrollMode',
    ],
  },
})
