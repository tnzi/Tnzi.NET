import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import 'pinia-plugin-persistedstate'

export interface AdminTab {
  id: string
  title: string
  fullPath: string
  query?: Record<string, unknown>
  params?: Record<string, unknown>
  meta?: {
    keepAlive?: boolean
    fixedIndexInTab?: number
    multiTab?: boolean
    icon?: string
  }
}

interface RouteLike {
  name: string
  fullPath: string
  path: string
  query?: Record<string, unknown>
  params?: Record<string, unknown>
  meta?: {
    title?: string
    keepAlive?: boolean
    fixedIndexInTab?: number
    multiTab?: boolean
    icon?: string
    /** When true the route never becomes a tab (auth / exception / consumer-flagged). */
    hideInTab?: boolean
  }
}

function hasOwnKeys(o?: object | null): boolean {
  return !!o && Object.keys(o).length > 0
}

/**
 * Route names that must NEVER become a tab. `login` (+ its module sub-routes),
 * the exception pages (403/404/500) and the bare `/admin` redirect are chrome-
 * less full-page layouts rendered OUTSIDE the admin shell - a persisted "Login"
 * tab (e.g. carried over from an older build / a session-expiry bounce) would
 * otherwise linger in the bar. `addTab` refuses them and `afterHydrate` strips
 * any that were persisted before this guard existed. Consumers can flag their
 * own non-tab routes with `meta.hideInTab: true`.
 */
const NON_TAB_ROUTE_NAMES = new Set([
  'login',
  'forbidden',
  'not-found',
  'server-error',
  'admin-root',
])

function isNonTabRoute(name: string, meta?: { hideInTab?: boolean } | null): boolean {
  return NON_TAB_ROUTE_NAMES.has(name) || meta?.hideInTab === true
}

/**
 * Decide whether a route should get its OWN tab per instance (detail / deep-link
 * pages) instead of reusing the route-name tab. A route is multi-instance when it
 * carries dynamic params (e.g. `/agents/:id` - customer A and customer B are
 * different records and must each own a tab) OR it explicitly opts in via
 * `meta.multiTab` together with a query string (e.g. a report page split by
 * `?type=`).
 */
export function isMultiInstanceRoute(route: {
  params?: object | null
  query?: object | null
  meta?: Record<string, unknown> | null
}): boolean {
  return hasOwnKeys(route.params) || (route.meta?.multiTab === true && hasOwnKeys(route.query))
}

/**
 * Stable per-instance id/key for a multi-instance route.
 *  - **Param routes** (`/agents/:id`): key by `path` (the resolved pathname,
 *    WITHOUT query) so a detail page that syncs volatile query state - e.g.
 *    `?section=` deep-links - keeps ONE tab / ONE component instance instead of
 *    spawning a new tab and remounting on every section switch.
 *  - **multiTab + query routes** (reports `?type=`): key by `fullPath` since the
 *    query string IS the differentiator between instances.
 */
export function multiInstanceKey(route: { path: string; fullPath: string; params?: object | null }): string {
  return hasOwnKeys(route.params) ? route.path : route.fullPath
}

function routeToTab(route: RouteLike): AdminTab {
  // Multi-instance routes get a per-record/per-query id; single-instance routes
  // reuse the stable route name as the id.
  const id = isMultiInstanceRoute(route) ? multiInstanceKey(route) : route.name
  return {
    id,
    title: route.meta?.title ?? route.name,
    fullPath: route.fullPath,
    query: route.query,
    params: route.params,
    meta: {
      keepAlive: route.meta?.keepAlive,
      fixedIndexInTab: route.meta?.fixedIndexInTab,
      multiTab: route.meta?.multiTab,
      icon: route.meta?.icon,
    },
  }
}

export const useAdminTabStore = defineStore('admin-tab', () => {
  const tabs = ref<AdminTab[]>([])
  const activeTabId = ref<string>('')
  const homeTab = ref<AdminTab | null>(null)
  // Phase G - pinned (fixed) tab IDs. soybean's `tabStore.fixTab/unfixTab/
  // isTabRetain` powers the right-click context menu's "Pin / Unpin" item
  // and prevents pinned tabs from being closed (close button is hidden,
  // close-others / close-left / close-right skip them).
  const fixedTabIds = ref<string[]>([])

  const allTabs = computed<AdminTab[]>(() => {
    return homeTab.value ? [homeTab.value, ...tabs.value] : [...tabs.value]
  })

  function findTab(id: string): AdminTab | undefined {
    return tabs.value.find((t) => t.id === id)
  }

  // Phase G - pin/unpin/isTabRetain. `isTabRetain` is true for both the
  // home tab and any pinned tab; close-related ops should skip these.
  function isTabPinned(id: string): boolean {
    return fixedTabIds.value.includes(id)
  }
  function isTabRetain(id: string): boolean {
    if (homeTab.value && homeTab.value.id === id) return true
    return isTabPinned(id)
  }
  function fixTab(id: string): void {
    if (!findTab(id)) return
    if (fixedTabIds.value.includes(id)) return
    fixedTabIds.value = [...fixedTabIds.value, id]
  }
  function unfixTab(id: string): void {
    fixedTabIds.value = fixedTabIds.value.filter((x) => x !== id)
  }

  function addTab(route: RouteLike): void {
    // Auth / exception / redirect routes are chrome-less full pages - never tab them.
    if (isNonTabRoute(route.name, route.meta)) return
    const tab = routeToTab(route)
    const existing = findTab(tab.id)
    if (existing) {
      // Refresh the stored location so the tab points at the LATEST url for this
      // record (e.g. a detail page that moved its `?section=`), keeping clicks +
      // reload-restore accurate. Title is left untouched so a dynamic title set
      // via `useTabTitle` (the record name) isn't clobbered by the static one.
      existing.fullPath = tab.fullPath
      existing.query = tab.query
      existing.params = tab.params
      activeTabId.value = tab.id
      return
    }
    tabs.value.push(tab)
    activeTabId.value = tab.id
  }

  function setHomeTab(tab: AdminTab): void {
    homeTab.value = tab
  }

  /**
   * Drop tabs whose route the current user is no longer allowed to open. Called
   * by the framework right after `loadPermissions` with the route store's
   * `deniedRouteNames`, so a persisted tab from a prior (higher-privilege)
   * session doesn't survive into a lower-privilege sign-in and 403 on click.
   *
   * Matches a tab by its `id` (for single-instance routes the id IS the route
   * name - which is what `deniedRouteNames` holds). Pinned tabs are pruned too
   * (an unauthorized pin is still unauthorized). Re-points `activeTabId` to a
   * surviving tab when the active one was removed.
   */
  function pruneTabs(deniedNames: Set<string>): void {
    if (!deniedNames || deniedNames.size === 0) return
    const before = tabs.value.length
    tabs.value = tabs.value.filter((t) => !deniedNames.has(t.id))
    if (tabs.value.length === before) return
    fixedTabIds.value = fixedTabIds.value.filter((id) => !deniedNames.has(id))
    if (!tabs.value.find((t) => t.id === activeTabId.value)) {
      activeTabId.value =
        tabs.value[tabs.value.length - 1]?.id ?? (homeTab.value?.id ?? '')
    }
  }

  function removeTab(id: string): string | null {
    // Phase G: refuse to remove pinned tabs (or the home tab).
    if (isTabRetain(id)) return null
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx < 0) return null
    tabs.value.splice(idx, 1)
    // If we removed the active one, switch to neighbor
    if (activeTabId.value === id) {
      const next = tabs.value[idx] || tabs.value[idx - 1]
      activeTabId.value = next?.id ?? (homeTab.value?.id ?? '')
      return activeTabId.value
    }
    return null
  }

  function removeLeftTabs(id: string): void {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx <= 0) return
    // Phase G: keep pinned tabs that fall in the to-remove range.
    const removed = tabs.value.slice(0, idx)
    const survivors = removed.filter((t) => isTabPinned(t.id))
    tabs.value = [...survivors, ...tabs.value.slice(idx)]
  }

  function removeRightTabs(id: string): void {
    const idx = tabs.value.findIndex((t) => t.id === id)
    if (idx < 0 || idx >= tabs.value.length - 1) return
    const survivors = tabs.value.slice(idx + 1).filter((t) => isTabPinned(t.id))
    tabs.value = [...tabs.value.slice(0, idx + 1), ...survivors]
  }

  function removeOtherTabs(id: string): void {
    const tab = findTab(id)
    if (!tab) return
    // Phase G: keep pinned tabs alongside the chosen one.
    const pinnedOthers = tabs.value.filter((t) => t.id !== id && isTabPinned(t.id))
    tabs.value = [...pinnedOthers, tab]
    activeTabId.value = id
  }

  function clearAllTabs(): void {
    // Phase G: pinned tabs are preserved across "close all".
    tabs.value = tabs.value.filter((t) => isTabPinned(t.id))
    if (!tabs.value.find((t) => t.id === activeTabId.value)) {
      activeTabId.value = homeTab.value?.id ?? (tabs.value[0]?.id ?? '')
    }
  }

  function moveTab(fromIndex: number, toIndex: number): void {
    if (fromIndex === toIndex) return
    if (fromIndex < 0 || fromIndex >= tabs.value.length) return
    if (toIndex < 0 || toIndex >= tabs.value.length) return
    const [moved] = tabs.value.splice(fromIndex, 1)
    if (!moved) return
    tabs.value.splice(toIndex, 0, moved)
  }

  function updateTabTitle(id: string, title: string): void {
    const tab = findTab(id)
    if (tab) tab.title = title
  }

  function setActiveTab(id: string): void {
    activeTabId.value = id
  }

  // Presentation stub - Task 2.29 router guard will replace this with real navigation.
  // Keeping it on the store lets TAdminTabs stay router-agnostic and unit-testable.
  function switchRouteByTab(tab: AdminTab): void {
    activeTabId.value = tab.id
  }

  return {
    tabs,
    activeTabId,
    homeTab,
    fixedTabIds,
    allTabs,
    addTab,
    setHomeTab,
    pruneTabs,
    removeTab,
    removeLeftTabs,
    removeRightTabs,
    removeOtherTabs,
    clearAllTabs,
    moveTab,
    updateTabTitle,
    setActiveTab,
    switchRouteByTab,
    findTab,
    isTabPinned,
    isTabRetain,
    fixTab,
    unfixTab,
  }
}, {
  persist: {
    key: 'tnzi-admin-tabs',
    pick: ['tabs', 'activeTabId', 'fixedTabIds'],
    // Strip any auth / exception tabs that were persisted before the addTab
    // guard existed (a stale "Login" tab from an older build / a session-expiry
    // bounce). Hydration bypasses `addTab`, so this is the only place to catch
    // them; `id` is the route name for these single-instance routes.
    afterHydrate: (ctx) => {
      const store = ctx.store as unknown as {
        tabs: AdminTab[]
        fixedTabIds: string[]
        activeTabId: string
      }
      const survives = (t: AdminTab) => !NON_TAB_ROUTE_NAMES.has(t.id)
      if (store.tabs.some((t) => !survives(t))) {
        store.tabs = store.tabs.filter(survives)
        store.fixedTabIds = store.fixedTabIds.filter((id) => !NON_TAB_ROUTE_NAMES.has(id))
        if (!store.tabs.find((t) => t.id === store.activeTabId)) {
          store.activeTabId = store.tabs[store.tabs.length - 1]?.id ?? ''
        }
      }
    },
  },
})
