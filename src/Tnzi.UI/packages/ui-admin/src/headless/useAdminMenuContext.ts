/**
 * `useAdminMenuContext` - Phase E composable that gives the 4 hybrid layout
 * modes the layered menu data they need (1st level / 2nd level / 3rd level
 * slices) plus cross-component state (which top-level item is currently
 * selected) so multiple menu surfaces (top, rail, drawer) can co-ordinate.
 *
 * Mirrors soybean's `provideMixMenuContext()` / `useMixMenuContext()` pair
 * (`src/layouts/modules/global-menu/context.ts`). Without this, the 3
 * hybrid modes can only render the full menu tree everywhere - they look
 * identical to vertical+horizontal stacked together.
 *
 * Usage (in TAdminShell):
 * ```ts
 * const ctx = useAdminMenuContext({ routeStore, route, autoSelectFirstWith: true })
 * provide(ADMIN_MENU_CONTEXT_KEY, ctx)
 * ```
 *
 * Usage (in a hybrid layout sub-component):
 * ```ts
 * const ctx = inject(ADMIN_MENU_CONTEXT_KEY)!
 * // bind <NMenu :options="ctx.firstLevelMenus.value">
 * ```
 */
import { computed, ref, watch, type ComputedRef, type Ref, type InjectionKey } from 'vue'
import type { AdminMenuItem } from '../stores/useAdminRouteStore'

export interface AdminMenuContext {
  /** Top-level menu items (1st level only). */
  firstLevelMenus: ComputedRef<AdminMenuItem[]>
  /** Children of the currently-active 1st level item (2nd level). */
  secondLevelMenus: ComputedRef<AdminMenuItem[]>
  /** Children of the currently-active 2nd level item (3rd level). */
  childLevelMenus: ComputedRef<AdminMenuItem[]>
  /** Key of the currently-active 1st level item. */
  activeFirstLevelMenuKey: Ref<string>
  /** Key of the currently-active 2nd level item. */
  activeSecondLevelMenuKey: Ref<string>
  /** True iff the active 1st level item has children (drives sider visibility
      in `top-hybrid-header-first` mode). */
  isActiveFirstLevelMenuHasChildren: ComputedRef<boolean>
  /** Switch the active 1st level item by key. Mutates `activeFirstLevelMenuKey`. */
  handleSelectFirstLevelMenu: (key: string) => void
  /** Switch the active 2nd level item by key. Mutates `activeSecondLevelMenuKey`. */
  handleSelectSecondLevelMenu: (key: string) => void
  /**
   * Locate the route's nearest matching 1st level item.
   *
   * A named route that matches nothing is OFF-MENU (a `hideInMenu` page such
   * as the Settings Center) and resolves to `''` - no highlight, no sub-menu.
   * `autoSelectFirstWith` only applies when there is no route name at all
   * (cold load), so the vertical-mix drawer / hybrid sider still has something
   * to render on initial load.
   */
  resolveFirstLevelKeyForRoute: (routeName: string) => string
}

export const ADMIN_MENU_CONTEXT_KEY: InjectionKey<AdminMenuContext> =
  Symbol('tnzi-admin-menu-context')

export interface UseAdminMenuContextOptions {
  /** Reactive source of the menu tree (typically `routeStore.menus`). */
  menus: Ref<AdminMenuItem[]> | ComputedRef<AdminMenuItem[]>
  /** Reactive route name (typically `() => useRoute().name`). */
  routeName: ComputedRef<string>
  /**
   * When true (default), fall back to the first 1st level item that has
   * children while the route name is still empty (cold load). Mirrors
   * soybean's `getActiveFirstLevelMenuKey` / `autoSelectFirstMenu`. A named
   * route that matches no menu item is off-menu and resolves to `''`
   * regardless of this flag.
   */
  autoSelectFirstWith?: boolean
  /**
   * When true (default), changing the active 1st level item auto-selects the
   * first 2nd level child if the previous 2nd level key is no longer in
   * scope. Set to a reactive ref bound to `themeStore.autoSelectFirstMenu`
   * so the Theme Drawer toggle actually controls this behaviour.
   */
  autoSelectSecondLevel?: Ref<boolean> | ComputedRef<boolean> | boolean
}

export function useAdminMenuContext(opts: UseAdminMenuContextOptions): AdminMenuContext {
  const {
    menus,
    routeName,
    autoSelectFirstWith = true,
    autoSelectSecondLevel = true,
  } = opts

  // Normalise autoSelectSecondLevel so we can read it as a value-or-ref-or-computed.
  function readAutoSelectSecondLevel(): boolean {
    if (typeof autoSelectSecondLevel === 'boolean') return autoSelectSecondLevel
    return autoSelectSecondLevel.value !== false
  }

  const activeFirstLevelMenuKey = ref<string>('')
  const activeSecondLevelMenuKey = ref<string>('')

  const firstLevelMenus = computed<AdminMenuItem[]>(() => menus.value.slice())

  function findFirstLevelByDescendantKey(target: string): AdminMenuItem | null {
    if (!target) return null
    function containsKey(item: AdminMenuItem): boolean {
      if (item.key === target) return true
      if (!item.children?.length) return false
      return item.children.some(containsKey)
    }
    return menus.value.find(containsKey) ?? null
  }

  function resolveFirstLevelKeyForRoute(name: string): string {
    // 1. Direct match - the route name *is* a 1st level menu key (e.g. a
    //    leaf top-level like 'dashboard'). Without this, leaf top-level
    //    routes would fall through to step 2 / 3 and silently jump the
    //    rail to a sibling that *does* have children.
    if (name) {
      const direct = menus.value.find((m) => m.key === name)
      if (direct) return direct.key
    }
    // 2. Descendant match - find the 1st level ancestor of a nested route.
    const match = findFirstLevelByDescendantKey(name)
    if (match) return match.key
    // 2b. A NAMED route that matches nothing lives outside the menu tree -
    //     the Settings Center and the User Center are `hideInMenu` and reached
    //     from the shell's own chrome, not a menu row. Report "no active
    //     module" instead of guessing: falling through to step 3 highlighted
    //     the first module with children (Identity) and made the hybrid sider
    //     render ITS sub-menu while the user was on Settings - a confidently
    //     wrong answer, worse than none. Off-menu pages get no highlight and
    //     no sub-menu, exactly like the top-level leaf `dashboard`.
    if (name) return ''
    // 3. No route signal at all (cold load before the router settles): fall
    //    back to the first item with children so the vertical-mix drawer /
    //    hybrid sider has something to render.
    if (autoSelectFirstWith) {
      const firstWithChildren = menus.value.find((m) => (m.children?.length ?? 0) > 0)
      if (firstWithChildren) return firstWithChildren.key
    }
    return menus.value[0]?.key ?? ''
  }

  // Keep activeFirstLevelMenuKey in sync with the current route. We only
  // *push* the route's key into the active state - the user can still
  // hover-override via handleSelectFirstLevelMenu without us yanking it back.
  watch(
    routeName,
    (name) => {
      const next = resolveFirstLevelKeyForRoute(name)
      if (next && next !== activeFirstLevelMenuKey.value) {
        activeFirstLevelMenuKey.value = next
      }
    },
    { immediate: true },
  )

  const activeFirstLevelMenu = computed<AdminMenuItem | null>(() => {
    const key = activeFirstLevelMenuKey.value
    if (!key) return null
    return menus.value.find((m) => m.key === key) ?? null
  })

  const secondLevelMenus = computed<AdminMenuItem[]>(
    () => activeFirstLevelMenu.value?.children?.slice() ?? [],
  )

  const isActiveFirstLevelMenuHasChildren = computed<boolean>(
    () => secondLevelMenus.value.length > 0,
  )

  // When the 1st level changes, default the 2nd level to either the route's
  // matching 2nd level (if any) or the first child. The auto-select-first
  // fallback is gated on `autoSelectSecondLevel` so users can opt out via
  // the Theme Drawer toggle.
  watch(
    [activeFirstLevelMenuKey, secondLevelMenus],
    () => {
      if (!secondLevelMenus.value.length) {
        activeSecondLevelMenuKey.value = ''
        return
      }
      // If current 2nd level key is still in the children list, keep it.
      if (
        secondLevelMenus.value.some((m) => m.key === activeSecondLevelMenuKey.value)
      ) {
        return
      }
      if (readAutoSelectSecondLevel()) {
        activeSecondLevelMenuKey.value = secondLevelMenus.value[0]?.key ?? ''
      } else {
        // User opted out of auto-select - leave it cleared rather than
        // silently snapping to the first child.
        activeSecondLevelMenuKey.value = ''
      }
    },
    { immediate: true },
  )

  const activeSecondLevelMenu = computed<AdminMenuItem | null>(() => {
    const key = activeSecondLevelMenuKey.value
    if (!key) return null
    return secondLevelMenus.value.find((m) => m.key === key) ?? null
  })

  const childLevelMenus = computed<AdminMenuItem[]>(
    () => activeSecondLevelMenu.value?.children?.slice() ?? [],
  )

  function handleSelectFirstLevelMenu(key: string): void {
    activeFirstLevelMenuKey.value = key
  }
  function handleSelectSecondLevelMenu(key: string): void {
    activeSecondLevelMenuKey.value = key
  }

  return {
    firstLevelMenus,
    secondLevelMenus,
    childLevelMenus,
    activeFirstLevelMenuKey,
    activeSecondLevelMenuKey,
    isActiveFirstLevelMenuHasChildren,
    handleSelectFirstLevelMenu,
    handleSelectSecondLevelMenu,
    resolveFirstLevelKeyForRoute,
  }
}
