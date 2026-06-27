import { defineStore } from 'pinia'
import { hasInjectionContext, inject, ref, watch } from 'vue'
import { THEME_CONTEXT_KEY } from '@tnzi/ui'
import 'pinia-plugin-persistedstate'

/**
 * Admin layout modes — 4 variants modeled after soybean-admin.
 *
 * - `vertical`              — left sidebar (default)
 * - `horizontal`            — top menu only, no sidebar
 * - `vertical-mix`          — narrow first-level sidebar + sub-sidebar with children
 * - `top-hybrid-header-first` — top first-level menu + sidebar with the active
 *                               first-level's children (sider hides when it has none)
 *
 * The two header-first/sidebar-first hybrid variants soybean ships were
 * dropped (2026-06-26): `vertical-hybrid-header-first` rendered an empty
 * sider whenever the active first-level had no children (e.g. the Dashboard
 * landing page), and `top-hybrid-sidebar-first` double-rendered the menu
 * (full tree in the sider + second level in the header). Both are redundant
 * under Tnzi's wide-but-shallow (11 top-level × 2 deep) menu — they only pay
 * off with soybean's 3-level menus.
 */
export type AdminLayoutMode =
  | 'vertical'
  | 'horizontal'
  | 'vertical-mix'
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

/**
 * Theme color schema (light / dark / auto). The LIVE value is owned by the
 * `@tnzi/ui` theme context (`settings.mode` — header cycle button and theme
 * drawer mutate it via `setMode`), but that context has no persistence of its
 * own, so a dark-mode choice used to revert to the app default on every full
 * page reload. This store keeps a persisted mirror and replays it into the
 * context on hydration. `null` = "user never chose" → the app default from
 * `createTnziUi()` / `createTnziUiAdmin()` wins.
 */
export type AdminThemeSchema = 'light' | 'dark' | 'auto'

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
const VALID_THEME_SCHEMAS: AdminThemeSchema[] = ['light', 'dark', 'auto']

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
  // The `@tnzi/ui` theme context is provided at app level (`createTnziUi()`
  // or the `createTnziUiAdmin()` fallback). Pinia runs this setup inside the
  // app's injection context when the store is first used from a component,
  // so `inject()` resolves; bare test pinias (no app) skip the wiring.
  const themeCtx = hasInjectionContext() ? inject(THEME_CONTEXT_KEY, undefined) : undefined

  // Layout mode
  const layoutMode = ref<AdminLayoutMode>('vertical')

  // Theme schema (light / dark / auto) — persisted mirror of the theme
  // context's `settings.mode`; see the `AdminThemeSchema` type doc above.
  const themeSchema = ref<AdminThemeSchema | null>(null)

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

  // Tab style (chrome / button / slider) — defaults to `button` (the
  // rounded-chip style; least visual noise, no SVG-arc geometry).
  const tabStyle = ref<TabStyle>('button')

  // Page transition
  const pageTransition = ref<PageTransition>('fade-slide')
  const pageAnimate = ref(true)

  // Theme radius (Phase I.6.6) — drives --tnzi-admin-radius* globally
  // (sm/md/lg derived by scale in setThemeRadius). 0-16px range.
  const themeRadius = ref(4)

  // Background color overrides — `null` = fall back to default token value.
  // When non-null the value is written to the corresponding CSS custom
  // property on `document.documentElement` so it overrides the token binding
  // from variables.css without touching any other property.
  const siderBg = ref<string | null>(null)
  const headerBg = ref<string | null>(null)
  const contentBg = ref<string | null>(null)
  const containerBg = ref<string | null>(null)

  // Inverted color scheme for sider (orthogonal to global dark mode).
  // Note: soybean only inverts the sider — the header always follows the
  // global dark/light mode. We removed `invertHeader` because our previous
  // implementation produced black-bg + grey-icon visual garbage (children
  // had higher CSS specificity than the wrapper hack), and there was no
  // soybean reference to align against.
  // Defaults to `true`: the shipped theme uses a dark sider on a light
  // body (the common "inverted sider" admin look). Only takes visual effect
  // in light mode + vertical-family layouts (see useAdminShellLayout gating).
  const invertSider = ref(true)

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
  /** When the active tab changes, the tab bar auto-scrolls the active tab
      into view (so it's never hidden off-screen when there are many tabs).
      This toggle only controls the SCROLL ANIMATION:
      - `false` (default): the tab snaps into view instantly (`behavior:'auto'`)
        — still visible, no motion. Off by default because the long-distance
        smooth glide can feel disorienting / uncomfortable for some users.
      - `true`: smooth animated scroll (`behavior:'smooth'`). */
  const tabScrollAnimation = ref(false)
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

  function setThemeSchema(mode: AdminThemeSchema): void {
    if (!VALID_THEME_SCHEMAS.includes(mode)) return
    themeSchema.value = mode
    if (themeCtx && themeCtx.settings.value.mode !== mode) {
      themeCtx.setMode(mode)
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

  function applySiderBg(v: string | null): void {
    if (typeof document === 'undefined') return
    if (v) {
      document.documentElement.style.setProperty('--tnzi-admin-sider-bg', v)
    } else {
      document.documentElement.style.removeProperty('--tnzi-admin-sider-bg')
    }
  }
  function setSiderBg(v: string | null): void {
    siderBg.value = v
    applySiderBg(v)
  }
  function resetSiderBg(): void {
    siderBg.value = null
    applySiderBg(null)
  }
  function applyHeaderBg(v: string | null): void {
    if (typeof document === 'undefined') return
    if (v) {
      document.documentElement.style.setProperty('--tnzi-admin-header-bg', v)
    } else {
      document.documentElement.style.removeProperty('--tnzi-admin-header-bg')
    }
  }
  function setHeaderBg(v: string | null): void {
    headerBg.value = v
    applyHeaderBg(v)
  }
  function resetHeaderBg(): void {
    headerBg.value = null
    applyHeaderBg(null)
  }
  function applyContentBg(v: string | null): void {
    if (typeof document === 'undefined') return
    if (v) {
      document.documentElement.style.setProperty('--tnzi-admin-content-bg', v)
      document.documentElement.style.setProperty('--tnzi-layout-bg', v)
    } else {
      document.documentElement.style.removeProperty('--tnzi-admin-content-bg')
      document.documentElement.style.removeProperty('--tnzi-layout-bg')
    }
  }
  function setContentBg(v: string | null): void {
    contentBg.value = v
    applyContentBg(v)
  }
  function resetContentBg(): void {
    contentBg.value = null
    applyContentBg(null)
  }
  function applyContainerBg(v: string | null): void {
    if (typeof document === 'undefined') return
    if (v) {
      document.documentElement.style.setProperty('--tnzi-container-bg', v)
    } else {
      document.documentElement.style.removeProperty('--tnzi-container-bg')
    }
  }
  function setContainerBg(v: string | null): void {
    containerBg.value = v
    applyContainerBg(v)
  }
  function resetContainerBg(): void {
    containerBg.value = null
    applyContainerBg(null)
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
  function setTabScrollAnimation(v: boolean): void {
    tabScrollAnimation.value = v
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
    // Back to "no explicit user choice" — the app default mode wins again.
    themeSchema.value = null
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
    tabStyle.value = 'button'
    pageTransition.value = 'fade-slide'
    pageAnimate.value = true
    invertSider.value = true
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
    tabScrollAnimation.value = false
    scrollMode.value = 'content'
    applySiderBg(null)
    siderBg.value = null
    applyHeaderBg(null)
    headerBg.value = null
    applyContentBg(null)
    contentBg.value = null
    applyContainerBg(null)
    containerBg.value = null
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
    // Background color overrides — `immediate: true` covers persisted-state
    // hydration (pinia-plugin-persistedstate bypasses setters, so the CSS var
    // would otherwise not be written after a page reload). Normal user
    // interactions go through the setter functions which call the apply*
    // helpers directly and are therefore synchronous.
    watch(siderBg, applySiderBg, { immediate: true })
    watch(headerBg, applyHeaderBg, { immediate: true })
    watch(contentBg, applyContentBg, { immediate: true })
    watch(containerBg, applyContainerBg, { immediate: true })
  }

  // ── Theme-schema ⇄ @tnzi/ui context sync ──
  // The context owns the live mode but persists nothing; this store persists
  // the mirror but renders nothing. Two watchers keep them aligned (each one
  // no-ops when already in sync, so there is no feedback loop):
  if (themeCtx) {
    // Persisted mirror → context. Fires on persisted-state hydration (which
    // bypasses setters) so a saved "dark" survives a full page reload.
    watch(
      themeSchema,
      (mode) => {
        if (mode && themeCtx.settings.value.mode !== mode) {
          themeCtx.setMode(mode)
        }
      },
      { immediate: true },
    )
    // Context → persisted mirror. Captures every mutation path that talks to
    // the context directly (header schema cycle button, theme drawer, app
    // code), so the user's last choice is the one that persists.
    watch(
      () => themeCtx.settings.value.mode,
      (mode) => {
        if (mode && mode !== themeSchema.value) {
          themeSchema.value = mode
        }
      },
    )
  }

  return {
    // state
    layoutMode,
    themeSchema,
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
    tabScrollAnimation,
    scrollMode,
    siderBg,
    headerBg,
    contentBg,
    containerBg,
    // setters
    setLayoutMode,
    setThemeSchema,
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
    setTabScrollAnimation,
    setScrollMode,
    setSiderBg,
    resetSiderBg,
    setHeaderBg,
    resetHeaderBg,
    setContentBg,
    resetContentBg,
    setContainerBg,
    resetContainerBg,
    reset,
  }
}, {
  persist: {
    key: 'tnzi-admin-theme',
    pick: [
      'layoutMode',
      'themeSchema',
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
      'tabScrollAnimation',
      'scrollMode',
      'siderBg',
      'headerBg',
      'contentBg',
      'containerBg',
    ],
    // Migration: a user who persisted one of the two removed hybrid layout
    // modes (vertical-hybrid-header-first / top-hybrid-sidebar-first) would
    // hydrate into an unknown mode that renders neither a sider nor a top
    // menu — i.e. no navigation at all. Coerce any stale/unknown persisted
    // mode back to the default vertical layout. Hydration bypasses
    // `setLayoutMode`, so this is the only place that can catch it.
    afterHydrate: (ctx) => {
      const store = ctx.store as unknown as { layoutMode: AdminLayoutMode }
      if (!VALID_LAYOUT_MODES.includes(store.layoutMode)) {
        store.layoutMode = 'vertical'
      }
    },
  },
})
