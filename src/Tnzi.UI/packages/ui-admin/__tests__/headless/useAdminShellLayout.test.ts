import { describe, it, expect, beforeEach, vi } from 'vitest'
import { ref } from 'vue'

// @vueuse 14's useBreakpoints returns strictly-readonly computed refs, so the
// store's `isMobile`/`isTablet`/`isDesktop` (bp.smaller/between/greaterOrEqual)
// can no longer be flipped by assigning `appStore.isMobile = true` in tests
// (worked under @vueuse 11 where they were plain refs). Mock useBreakpoints to
// hand back writable refs so these layout tests can drive the responsive state
// directly; production still uses the real window-driven breakpoints.
vi.mock('@vueuse/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@vueuse/core')>()
  const { ref: vueRef } = await import('vue')
  const flag = () => vueRef(false)
  return {
    ...actual,
    useBreakpoints: () => ({
      smaller: flag,
      smallerOrEqual: flag,
      greater: flag,
      greaterOrEqual: flag,
      between: flag,
    }),
  }
})
import { createPinia, setActivePinia } from 'pinia'
import {
  useAdminShellLayout,
  type SiderConfig,
  type HeaderConfig,
  type TabsConfig,
  type FooterConfig,
  type ContentConfig,
} from '../../src/headless/useAdminShellLayout'
import type { AdminMenuContext } from '../../src/headless/useAdminMenuContext'
import type { AdminMenuItem } from '../../src/stores/useAdminRouteStore'
import type { AdminLayoutMode } from '../../src/stores/useAdminThemeStore'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'
import { useAdminAppStore } from '../../src/stores/useAdminAppStore'

function makeMenuCtx(
  overrides: Partial<AdminMenuContext> = {},
): AdminMenuContext {
  const empty: AdminMenuItem[] = []
  const ctx: AdminMenuContext = {
    firstLevelMenus: ref(empty) as never,
    secondLevelMenus: ref(empty) as never,
    childLevelMenus: ref(empty) as never,
    activeFirstLevelMenuKey: ref(''),
    activeSecondLevelMenuKey: ref(''),
    isActiveFirstLevelMenuHasChildren: ref(false) as never,
    handleSelectFirstLevelMenu: () => undefined,
    handleSelectSecondLevelMenu: () => undefined,
    handleClearActiveSecondLevelMenu: () => undefined,
    resolveFirstLevelKeyForRoute: () => '',
  } as never
  return { ...ctx, ...overrides }
}

function setupShell(opts: {
  mode?: AdminLayoutMode
  sider?: SiderConfig
  header?: HeaderConfig
  tabs?: TabsConfig
  footer?: FooterConfig
  content?: ContentConfig
  isDark?: boolean
  isMobile?: boolean
  menuCtx?: AdminMenuContext
} = {}) {
  setActivePinia(createPinia())
  const appStore = useAdminAppStore()
  if (opts.isMobile) appStore.isMobile = true
  return useAdminShellLayout({
    mode: ref<AdminLayoutMode | undefined>(opts.mode),
    sider: ref<SiderConfig>(opts.sider ?? {}),
    header: ref<HeaderConfig>(opts.header ?? {}),
    tabs: ref<TabsConfig>(opts.tabs ?? {}),
    footer: ref<FooterConfig>(opts.footer ?? {}),
    content: ref<ContentConfig>(opts.content ?? {}),
    isDark: ref(opts.isDark ?? false),
    menuCtx: opts.menuCtx ?? makeMenuCtx(),
  })
}

describe('useAdminShellLayout', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('falls back to theme store layout mode when prop is undefined', () => {
    const layout = setupShell()
    const theme = useAdminThemeStore()
    expect(layout.effectiveMode.value).toBe(theme.layoutMode)
  })

  it('prop mode takes precedence over the theme store', () => {
    const layout = setupShell({ mode: 'horizontal' })
    expect(layout.effectiveMode.value).toBe('horizontal')
  })

  it('forces vertical mode on mobile regardless of requested mode', () => {
    const layout = setupShell({ mode: 'horizontal', isMobile: true })
    expect(layout.effectiveMode.value).toBe('vertical')
  })

  it('siderInverted is gated by light mode + vertical-family mode + theme.invertSider', () => {
    const layout = setupShell({ mode: 'vertical' })
    const theme = useAdminThemeStore()
    theme.invertSider = true
    expect(layout.siderInverted.value).toBe(true)
  })

  it('siderInverted is false under dark mode even if theme.invertSider is true', () => {
    const layout = setupShell({ mode: 'vertical', isDark: true })
    const theme = useAdminThemeStore()
    theme.invertSider = true
    expect(layout.siderInverted.value).toBe(false)
  })

  it('siderInverted is false in horizontal mode regardless of theme.invertSider', () => {
    const layout = setupShell({ mode: 'horizontal' })
    const theme = useAdminThemeStore()
    theme.invertSider = true
    expect(layout.siderInverted.value).toBe(false)
  })

  it('topMenuVariant: null in vertical, full in horizontal, first-level in hybrid', () => {
    expect(setupShell({ mode: 'vertical' }).topMenuVariant.value).toBe(null)
    expect(setupShell({ mode: 'horizontal' }).topMenuVariant.value).toBe('full')
    expect(setupShell({ mode: 'top-hybrid-header-first' }).topMenuVariant.value).toBe(
      'first-level',
    )
  })

  it('showMainSider: false when sider.visible=false', () => {
    const layout = setupShell({ mode: 'vertical', sider: { visible: false } })
    expect(layout.showMainSider.value).toBe(false)
  })

  it('showMainSider: false on mobile (drawer replaces it)', () => {
    const layout = setupShell({ mode: 'vertical', isMobile: true })
    expect(layout.showMainSider.value).toBe(false)
  })

  it('showMainSider: false in horizontal mode (no sider)', () => {
    const layout = setupShell({ mode: 'horizontal' })
    expect(layout.showMainSider.value).toBe(false)
  })

  it('showMainSider: false in top-hybrid-header-first when active 1st level has no children', () => {
    const menuCtx = makeMenuCtx({
      isActiveFirstLevelMenuHasChildren: ref(false) as never,
    })
    const layout = setupShell({ mode: 'top-hybrid-header-first', menuCtx })
    expect(layout.showMainSider.value).toBe(false)
  })

  it('showMainSider: true in top-hybrid-header-first when active 1st level has children', () => {
    const menuCtx = makeMenuCtx({
      isActiveFirstLevelMenuHasChildren: ref(true) as never,
    })
    const layout = setupShell({ mode: 'top-hybrid-header-first', menuCtx })
    expect(layout.showMainSider.value).toBe(true)
  })

  it('siderItems: undefined in vertical/vertical-mix (full tree)', () => {
    expect(setupShell({ mode: 'vertical' }).siderItems.value).toBeUndefined()
    expect(setupShell({ mode: 'vertical-mix' }).siderItems.value).toBeUndefined()
  })

  it('siderItems: secondLevelMenus in top-hybrid-header-first', () => {
    const children: AdminMenuItem[] = [{ key: 'a', label: 'A' }]
    const menuCtx = makeMenuCtx({
      secondLevelMenus: ref(children) as never,
    })
    const layout = setupShell({
      mode: 'top-hybrid-header-first',
      menuCtx,
    })
    expect(layout.siderItems.value).toEqual(children)
  })

  it('shouldRenderHeaderLogo: true for top-hybrid-header-first + horizontal only', () => {
    expect(setupShell({ mode: 'horizontal' }).shouldRenderHeaderLogo.value).toBe(true)
    expect(setupShell({ mode: 'top-hybrid-header-first' }).shouldRenderHeaderLogo.value).toBe(true)
    expect(setupShell({ mode: 'vertical' }).shouldRenderHeaderLogo.value).toBe(false)
    expect(setupShell({ mode: 'vertical-mix' }).shouldRenderHeaderLogo.value).toBe(false)
  })

  it('showSubSider: only true in vertical-mix on desktop', () => {
    expect(setupShell({ mode: 'vertical-mix' }).showSubSider.value).toBe(true)
    expect(setupShell({ mode: 'vertical' }).showSubSider.value).toBe(false)
    expect(setupShell({ mode: 'vertical-mix', isMobile: true }).showSubSider.value).toBe(false)
  })

  it('primarySiderWidth: respects consumer-supplied sider.width', () => {
    const layout = setupShell({ mode: 'vertical', sider: { width: 300 } })
    expect(layout.primarySiderWidth.value).toBe(300)
  })

  it('primarySiderWidth: collapses to siderCollapsedWidth when collapsed', () => {
    setActivePinia(createPinia())
    const layout = useAdminShellLayout({
      mode: ref<AdminLayoutMode | undefined>('vertical'),
      sider: ref({}),
      header: ref({}),
      tabs: ref({}),
      footer: ref({}),
      content: ref({}),
      isDark: ref(false),
      menuCtx: makeMenuCtx(),
    })
    const app = useAdminAppStore()
    const theme = useAdminThemeStore()
    app.setSiderCollapse(true)
    expect(layout.primarySiderWidth.value).toBe(theme.siderCollapsedWidth)
  })

  it('tabsVisible / footerVisible follow theme store toggles', () => {
    const layout = setupShell({ mode: 'vertical' })
    const theme = useAdminThemeStore()
    theme.tabVisible = false
    expect(layout.tabsVisible.value).toBe(false)
    theme.tabVisible = true
    theme.footerVisible = false
    expect(layout.footerVisible.value).toBe(false)
  })

  it('tabsVisible: hidden on mobile even when the theme toggle is on (tabs are a desktop affordance)', () => {
    const layout = setupShell({ mode: 'vertical', isMobile: true })
    const theme = useAdminThemeStore()
    theme.tabVisible = true
    expect(layout.tabsVisible.value).toBe(false)
  })

  it('headerVisible: always true in horizontal/hybrid modes (header hosts the menu)', () => {
    const layout = setupShell({ mode: 'horizontal', header: { visible: false } })
    expect(layout.headerVisible.value).toBe(true)
  })

  it('headerVisible: respects prop / theme store toggle in vertical modes', () => {
    const layout = setupShell({ mode: 'vertical', header: { visible: false } })
    expect(layout.headerVisible.value).toBe(false)
  })

  it('resolvedTransition: forces "none" when theme.pageAnimate is off', () => {
    const layout = setupShell({ mode: 'vertical', content: { transition: 'fade' } })
    const theme = useAdminThemeStore()
    theme.pageAnimate = false
    expect(layout.resolvedTransition.value).toBe('none')
  })

  it('mobileDrawerWidth: clamps to viewport - 40 with min 240', () => {
    const layout = setupShell({ mode: 'vertical', sider: { width: 10000 } })
    // Default viewport in useBreakpoint is 1024 if not exposed via test env.
    expect(layout.mobileDrawerWidth.value).toBeGreaterThanOrEqual(240)
  })

  it('drawerOpen: only true on mobile when sider is expanded', () => {
    setActivePinia(createPinia())
    const layout = useAdminShellLayout({
      mode: ref<AdminLayoutMode | undefined>('vertical'),
      sider: ref({}),
      header: ref({}),
      tabs: ref({}),
      footer: ref({}),
      content: ref({}),
      isDark: ref(false),
      menuCtx: makeMenuCtx(),
    })
    const app = useAdminAppStore()
    app.isMobile = true
    app.setSiderCollapse(false)
    expect(layout.drawerOpen.value).toBe(true)
    app.setSiderCollapse(true)
    expect(layout.drawerOpen.value).toBe(false)
  })
})
