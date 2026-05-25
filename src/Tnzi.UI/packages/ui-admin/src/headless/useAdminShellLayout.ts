/**
 * `useAdminShellLayout` — derived layout state for {@link TAdminShell}.
 *
 * Sunk out of `TAdminShell.vue` in 0.2.72+ (B5). Bundles the 16 read-only
 * computed properties that decide which surface (top menu / primary
 * sider / sub-sider / header / footer / tabs) renders in each of the
 * 6 layout modes, plus their widths, transition, and mobile drawer
 * shape. Pure derivations on top of the admin app + theme stores +
 * `useBreakpoint()` — no DOM, no lifecycle hooks, no event handlers.
 *
 * What stays in `TAdminShell.vue`:
 *  - `vertical-mix` interactive state (drawerVisible + mouseleave handler)
 *  - menu-context construction (`useAdminMenuContext`)
 *  - global keyboard listener (Cmd+K / Esc)
 *  - emit handlers (`onMenuSelect`, `onTopMenuSelect`, …)
 *
 * Extracting those would force a far wider `inject`+`emit` surface on
 * the composable; this split keeps the layout pure and the
 * interaction-heavy state co-located with its component.
 */
import { computed, type ComputedRef, type Ref } from 'vue'
import type { PageTransition } from '../stores/useAdminThemeStore'
import {
  useAdminThemeStore,
  type AdminLayoutMode,
} from '../stores/useAdminThemeStore'
import { useAdminAppStore } from '../stores/useAdminAppStore'
import { useBreakpoint } from './useBreakpoint'
import type { AdminMenuItem } from '../stores/useAdminRouteStore'
import type { AdminMenuContext } from './useAdminMenuContext'

/** Modes that surface the menu in the top header bar. */
export const HORIZONTAL_HOSTED_MODES: AdminLayoutMode[] = [
  'horizontal',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
]

/** Modes that render a primary sidebar. */
export const SIDER_HOSTED_MODES: AdminLayoutMode[] = [
  'vertical',
  'vertical-mix',
  'vertical-hybrid-header-first',
  'top-hybrid-sidebar-first',
  'top-hybrid-header-first',
]

/** Modes where `invertSider` actually flips the sider — soybean parity:
 *  `darkMenu = !darkMode && layout.includes('vertical')`. */
export const VERTICAL_INVERT_MODES: AdminLayoutMode[] = [
  'vertical',
  'vertical-mix',
]

export interface SiderConfig {
  visible?: boolean
  width?: number
  subWidth?: number
  collapsedWidth?: number
  fixed?: boolean
  brand?: string
  brandIcon?: string
}

export interface HeaderConfig {
  visible?: boolean
  fixed?: boolean
  showToggler?: boolean
  showBreadcrumb?: boolean
  showSearch?: boolean
  showFullscreen?: boolean
  showThemeBtn?: boolean
  showThemeSchemaBtn?: boolean
  showLangSwitch?: boolean
  showReload?: boolean
  showNotification?: boolean
  showUser?: boolean
}

export interface TabsConfig {
  visible?: boolean
  closeByMiddleClick?: boolean
  draggable?: boolean
  showReload?: boolean
}

export interface FooterConfig {
  visible?: boolean
}

export interface ContentConfig {
  transition?: PageTransition
}

export interface UseAdminShellLayoutOptions {
  /** Layout mode prop. Undefined → fall through to `themeStore.layoutMode`. */
  mode: Ref<AdminLayoutMode | undefined>
  /** Sider prop bag (`width` / `subWidth` / `collapsedWidth` / `visible`). */
  sider: Ref<SiderConfig>
  /** Header prop bag (`visible` only — the rest are passed through to TAdminHeader). */
  header: Ref<HeaderConfig>
  /** Tabs prop bag (`visible` only — the rest are passed through to TAdminTabs). */
  tabs: Ref<TabsConfig>
  /** Footer prop bag (`visible` only). */
  footer: Ref<FooterConfig>
  /** Content prop bag (`transition` — overrides the theme store's default). */
  content: Ref<ContentConfig>
  /** Whether the @tnzi/ui theme reports dark mode (drives invertSider gating). */
  isDark: Ref<boolean>
  /** Layered menu context (active 1st level, 2nd level slices, etc.). */
  menuCtx: AdminMenuContext
}

export interface UseAdminShellLayoutReturn {
  /** Resolved layout mode (prop wins, else theme store). On mobile,
   *  always `vertical` — mobile uses a drawer regardless of preference. */
  effectiveMode: ComputedRef<AdminLayoutMode>
  /** Whether the sider should render with inverted (dark) tones. */
  siderInverted: ComputedRef<boolean>
  /** Variant of the top menu — null when hidden, `'full'` for horizontal,
   *  `'first-level'` for hybrid modes that show only top-level items. */
  topMenuVariant: ComputedRef<'full' | 'first-level' | null>
  /** Whether the primary sider should render at all. */
  showMainSider: ComputedRef<boolean>
  /** Menu items the primary sider renders in the current mode (or
   *  `undefined` to render the full menu tree per `routeStore.menus`). */
  siderItems: ComputedRef<AdminMenuItem[] | undefined>
  /** Menu items the top menu renders in the current mode. */
  topItems: ComputedRef<AdminMenuItem[] | undefined>
  /** Active key for the top menu. */
  topActiveKey: ComputedRef<string | undefined>
  /** Whether the brand logo lives in the header instead of the sider. */
  shouldRenderHeaderLogo: ComputedRef<boolean>
  /** Whether the sub-sider drawer (vertical-mix mode) should render. */
  showSubSider: ComputedRef<boolean>
  /** Visual mode the primary sidebar reports to its child surfaces. */
  sidebarPresentationMode: ComputedRef<'vertical' | 'vertical-mix'>
  /** Width (px) of the primary sider in the current mode + collapse state. */
  primarySiderWidth: ComputedRef<number>
  /** Width (px) of the sub-sider in vertical-mix mode. */
  subSiderWidth: ComputedRef<number>
  /** Whether the tabs bar should render. */
  tabsVisible: ComputedRef<boolean>
  /** Whether the footer should render. */
  footerVisible: ComputedRef<boolean>
  /** Whether the header should render. (Always `true` in horizontal/hybrid
   *  modes regardless of consumer / theme preferences — those modes
   *  rely on the header for navigation.) */
  headerVisible: ComputedRef<boolean>
  /** Resolved content transition (forced to `'none'` when pageAnimate is off). */
  resolvedTransition: ComputedRef<PageTransition>
  /** Width (px) of the mobile-mode drawer, accounting for viewport gutters. */
  mobileDrawerWidth: ComputedRef<number>
  /** Whether the mobile drawer is currently open. */
  drawerOpen: ComputedRef<boolean>
}

export function useAdminShellLayout(
  options: UseAdminShellLayoutOptions,
): UseAdminShellLayoutReturn {
  const themeStore = useAdminThemeStore()
  const appStore = useAdminAppStore()
  const bp = useBreakpoint()
  const { mode, sider, header, tabs, footer, content, isDark, menuCtx } = options

  const effectiveMode = computed<AdminLayoutMode>(() => {
    const requested = mode.value ?? themeStore.layoutMode
    // Mobile is always vertical drawer-based regardless of desktop preference.
    return appStore.isMobile ? 'vertical' : requested
  })

  const siderInverted = computed(
    () =>
      themeStore.invertSider &&
      !isDark.value &&
      VERTICAL_INVERT_MODES.includes(effectiveMode.value),
  )

  const topMenuVariant = computed<'full' | 'first-level' | null>(() => {
    if (!HORIZONTAL_HOSTED_MODES.includes(effectiveMode.value)) return null
    return effectiveMode.value === 'horizontal' ? 'full' : 'first-level'
  })

  const showMainSider = computed<boolean>(() => {
    if (sider.value.visible === false) return false
    if (appStore.isMobile) return false
    if (!SIDER_HOSTED_MODES.includes(effectiveMode.value)) return false
    // top-hybrid-header-first hides the sider when the active 1st level
    // item has no children (the menu lives entirely in the header bar).
    if (effectiveMode.value === 'top-hybrid-header-first') {
      return menuCtx.isActiveFirstLevelMenuHasChildren.value
    }
    return true
  })

  // What the sider should render in each layout mode:
  //   vertical / vertical-mix → full menu tree
  //   vertical-hybrid-header-first → children of the active 1st level
  //   top-hybrid-sidebar-first → 1st level items only
  //   top-hybrid-header-first → children of the active 1st level
  const siderItems = computed<AdminMenuItem[] | undefined>(() => {
    switch (effectiveMode.value) {
      case 'vertical-hybrid-header-first':
      case 'top-hybrid-header-first':
        return menuCtx.secondLevelMenus.value
      case 'top-hybrid-sidebar-first':
        return menuCtx.firstLevelMenus.value
      case 'vertical':
      case 'vertical-mix':
      default:
        return undefined
    }
  })

  // What the top menu should render in each layout mode:
  //   horizontal → full tree (default)
  //   vertical-hybrid-header-first / top-hybrid-header-first → 1st level only
  //   top-hybrid-sidebar-first → children of the active 1st level
  const topItems = computed<AdminMenuItem[] | undefined>(() => {
    if (effectiveMode.value === 'top-hybrid-sidebar-first') {
      return menuCtx.secondLevelMenus.value
    }
    return undefined
  })

  const topActiveKey = computed<string | undefined>(() => {
    if (effectiveMode.value === 'top-hybrid-sidebar-first') {
      return menuCtx.activeSecondLevelMenuKey.value || undefined
    }
    if (HORIZONTAL_HOSTED_MODES.includes(effectiveMode.value)) {
      return menuCtx.activeFirstLevelMenuKey.value || undefined
    }
    return undefined
  })

  // Render brand logo in the header for layout modes where the sider
  // doesn't have its own header logo. horizontal mode has no sider;
  // top-hybrid-* keep a sider but it's compact / context-dependent, so
  // the brand belongs in the header. vertical / vertical-mix /
  // vertical-hybrid-header-first keep it in the sider.
  const shouldRenderHeaderLogo = computed<boolean>(
    () =>
      effectiveMode.value === 'horizontal' ||
      effectiveMode.value === 'top-hybrid-sidebar-first' ||
      effectiveMode.value === 'top-hybrid-header-first',
  )

  const showSubSider = computed<boolean>(
    () => effectiveMode.value === 'vertical-mix' && !appStore.isMobile,
  )

  const sidebarPresentationMode = computed<'vertical' | 'vertical-mix'>(() =>
    effectiveMode.value === 'vertical-mix' ? 'vertical-mix' : 'vertical',
  )

  // 4-tier sider width:
  //   vertical-mix expanded  → mixSiderWidth (90px rail)
  //   vertical-mix collapsed → mixCollapsedWidth (64px icon-only rail)
  //   other hosted, collapsed → siderCollapsedWidth (64px)
  //   other hosted, expanded  → siderWidth (220px)
  // Consumer-provided `sider.width` always wins.
  const primarySiderWidth = computed<number>(() => {
    if (effectiveMode.value === 'vertical-mix') {
      return appStore.siderCollapse
        ? (sider.value.collapsedWidth ?? themeStore.mixCollapsedWidth)
        : (sider.value.width ?? themeStore.mixSiderWidth)
    }
    return appStore.siderCollapse
      ? (sider.value.collapsedWidth ?? themeStore.siderCollapsedWidth)
      : (sider.value.width ?? themeStore.siderWidth)
  })

  const subSiderWidth = computed<number>(
    () => sider.value.subWidth ?? themeStore.mixChildMenuWidth,
  )

  const tabsVisible = computed<boolean>(
    () => tabs.value.visible !== false && themeStore.tabVisible,
  )

  const footerVisible = computed<boolean>(
    () => footer.value.visible !== false && themeStore.footerVisible,
  )

  const headerVisible = computed<boolean>(() => {
    // In horizontal/hybrid layouts the header hosts the menu — hiding
    // it strands the user with no way to navigate. Force visible.
    if (HORIZONTAL_HOSTED_MODES.includes(effectiveMode.value)) return true
    return header.value.visible !== false && themeStore.headerVisible
  })

  const resolvedTransition = computed<PageTransition>(() =>
    themeStore.pageAnimate
      ? (content.value.transition ?? themeStore.pageTransition)
      : 'none',
  )

  // Mobile drawer width caps the consumer-supplied `sider.width` at
  // `viewport - 40` so the drawer never covers the close affordance.
  // Below 360px viewport the cap falls to 88vw (iPhone SE friendly).
  const mobileDrawerWidth = computed<number>(() => {
    const requested = sider.value.width ?? 260
    const viewport = bp.width.value || 1024
    const minGutter = viewport < 360 ? viewport * 0.12 : 40
    return Math.max(240, Math.min(requested, viewport - minGutter))
  })

  const drawerOpen = computed<boolean>(
    () => appStore.isMobile && !appStore.siderCollapse,
  )

  return {
    effectiveMode,
    siderInverted,
    topMenuVariant,
    showMainSider,
    siderItems,
    topItems,
    topActiveKey,
    shouldRenderHeaderLogo,
    showSubSider,
    sidebarPresentationMode,
    primarySiderWidth,
    subSiderWidth,
    tabsVisible,
    footerVisible,
    headerVisible,
    resolvedTransition,
    mobileDrawerWidth,
    drawerOpen,
  }
}
