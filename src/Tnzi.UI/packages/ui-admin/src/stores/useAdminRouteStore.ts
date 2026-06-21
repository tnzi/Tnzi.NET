import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { en } from '../locales/en'
import { zhCn } from '../locales/zh-cn'
import { DEFAULT_ROUTE_ICONS } from '../router/routeIcons'
import { humanise } from '../pages/_shared/translate'
import { useAdminAppStore } from './useAdminAppStore'
import { useAdminAuthStore } from './useAdminAuthStore'
import type { MenuTreeNode } from '@tnzi/core/services/system'

/**
 * Resolve a dotted i18n key against the bundled admin locale pack.
 * Returns the original key unchanged if no entry matches. Missing-key
 * fallback humanises the last segment (shared `humanise` from
 * `_shared/translate`) so the sidebar / breadcrumb / tabs all show the
 * same label when a key is missing.
 */
/**
 * Walk a dotted path through a messages tree, returning the string leaf or
 * undefined if any segment is missing / not a string.
 */
function lookupMessage(messages: Record<string, unknown>, path: string): string | undefined {
  let node: unknown = messages
  for (const part of path.split('.')) {
    if (typeof node === 'object' && node !== null && part in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[part]
    } else {
      return undefined
    }
  }
  return typeof node === 'string' ? node : undefined
}

function resolveI18nKey(
  key: string,
  locale: 'en' | 'zh-cn',
  overrides?: Record<string, unknown>,
): string {
  if (!key) return key
  // Bare labels (not dotted i18n keys) — return as-is.
  if (!key.startsWith('admin.') && !key.startsWith('tnzi.admin.')) {
    return key
  }
  // Strip optional `tnzi.` prefix — bundled locales are rooted at `admin.*`.
  const normalized = key.startsWith('tnzi.') ? key.slice(5) : key
  // Consumer-supplied overrides win — host apps register their own
  // `admin.modules.{module}.…` keys via `useAdminAppStore.extendLocaleMessages`
  // and we look those up before the bundled framework dictionary.
  if (overrides) {
    const hit = lookupMessage(overrides, normalized)
    if (hit !== undefined) return hit
  }
  const messages = (locale === 'zh-cn' ? zhCn : en) as Record<string, unknown>
  const hit = lookupMessage(messages, normalized)
  if (hit !== undefined) return hit
  // Phase I.7.10: missing-key fallback — humanise the last segment so
  // users never see raw `tnzi.admin.…` strings in the sidebar / tabs /
  // breadcrumb (mirrors the same fallback in `TAdminAutoBreadcrumb` and
  // `TAdminTabs.renderTitle`).
  return humanise(key)
}

export interface AdminRouteMeta {
  title: string
  i18nKey?: string
  icon?: string
  order?: number
  constant?: boolean
  keepAlive?: boolean
  hideInMenu?: boolean
  /**
   * Single permission code required to SEE this menu entry. Matches the real
   * route table's `meta.permission` (71 occurrences) and the navigation guard.
   * Prefer this over `permissions`.
   */
  permission?: string
  /** Multiple permission codes (OR semantics). Back-compat / advanced use. */
  permissions?: string[]
  roles?: string[]
  activeMenu?: string
  fixedIndexInTab?: number
  multiTab?: boolean
}

export interface AdminRouteRecord {
  name: string
  path: string
  component?: () => Promise<unknown>
  meta?: AdminRouteMeta
  children?: AdminRouteRecord[]
}

export interface AdminMenuItem {
  key: string
  label: string
  i18nKey?: string
  icon?: string
  path: string
  meta?: AdminRouteMeta
  children?: AdminMenuItem[]
}

/**
 * Index a backend menu tree (`MenuTreeNode[]`) by `menuKey` so the 'merge' menu
 * source can override the matching route-derived entry. Walks children too.
 */
function indexMenuOverrides(
  nodes: MenuTreeNode[],
  map: Map<string, MenuTreeNode> = new Map(),
): Map<string, MenuTreeNode> {
  for (const node of nodes) {
    if (node.menuKey) map.set(node.menuKey, node)
    if (node.children?.length) indexMenuOverrides(node.children, map)
  }
  return map
}

/**
 * Apply backend overrides onto the route-derived menu tree, keyed by route name.
 * The route table stays the source of truth for WHICH pages exist; a backend row
 * keyed by a route's name can retitle / re-icon / reorder / hide it without a
 * redeploy. Recurses into children; an override with `isHidden` drops the entry.
 * No-op for entries without a matching backend row.
 */
function applyMenuOverrides(
  items: AdminMenuItem[],
  byKey: Map<string, MenuTreeNode>,
): AdminMenuItem[] {
  const result: AdminMenuItem[] = []
  for (const item of items) {
    const override = byKey.get(item.key)
    if (override?.isHidden) continue
    let next = item
    if (override) {
      next = {
        ...item,
        label: override.name?.trim() ? override.name : item.label,
        icon: override.icon?.trim() ? override.icon : item.icon,
        meta:
          typeof override.sortOrder === 'number'
            ? ({ ...item.meta, order: override.sortOrder } as AdminRouteMeta)
            : item.meta,
      }
    }
    if (next.children?.length) {
      next = { ...next, children: applyMenuOverrides(next.children, byKey) }
    }
    result.push(next)
  }
  return result
}

/**
 * Admin route store — manages constant routes (always available), auth routes
 * (filtered by permissions), the derived menu tree, and the keepAlive cache list.
 */
export const useAdminRouteStore = defineStore('admin-route', () => {
  const constantRoutes = ref<AdminRouteRecord[]>([])
  const authRoutes = ref<AdminRouteRecord[]>([])
  const routesLoaded = ref(false)
  /** Backend Sys_Menu tree for the 'merge' source (overrides by menuKey). Empty = 'route' source. */
  const backendMenuNodes = ref<MenuTreeNode[]>([])

  const allRoutes = computed<AdminRouteRecord[]>(() => [
    ...constantRoutes.value,
    ...authRoutes.value,
  ])

  /** Cache-eligible route names (meta.keepAlive === true). Feeds `<KeepAlive :include>`. */
  const cacheRoutes = ref<string[]>([])

  /** Derived menu tree from allRoutes, excluding hideInMenu entries, sorted by meta.order. */
  const menus = computed<AdminMenuItem[]>(() => {
    const appStore = useAdminAppStore()
    const authStore = useAdminAuthStore()
    const locale = appStore.locale
    // Permission-driven visibility. Reading these reactive auth fields HERE is
    // what finally wires the sidebar to real permissions — `menus` recomputes
    // the moment the user logs in / their permission list loads.
    //  • super-user  → see everything (the backend also returns the full code
    //    catalogue for super-admins, so this is belt-and-suspenders);
    //  • no user yet → fail-OPEN (show all) so the sidebar is never blank before
    //    the permission list is fetched, and apps that never wire permissions
    //    keep the historical behaviour;
    //  • otherwise   → keep entries whose singular `meta.permission` (the real
    //    route-table field) — or any of plural `meta.permissions` (OR) — is
    //    granted; entries with no requirement are public.
    // Lowercased set → case-insensitive matching to mirror the backend
    // (StringComparer.OrdinalIgnoreCase). Codes are all lowercase today, but
    // pinning this prevents the silent-filter failure mode if casing drifts.
    const grantedPermissions = new Set(
      authStore.userPermissions.map((p) => p.toLowerCase()),
    )
    const permissionsLoaded = authStore.userInfo !== null
    const bypassPermissionFilter = authStore.isSuperUser || !permissionsLoaded
    function isVisible(route: AdminRouteRecord): boolean {
      if (bypassPermissionFilter) return true
      const single = route.meta?.permission
      if (single) return grantedPermissions.has(single.toLowerCase())
      const multi = route.meta?.permissions
      if (multi && multi.length > 0) return multi.some((p) => grantedPermissions.has(p.toLowerCase()))
      return true
    }
    // Host-app messages registered via `extendLocaleMessages`. Passed
    // through to `resolveI18nKey` so consumer-owned route titles
    // (e.g. `tnzi.admin.modules.acme.blog.posts.title`) resolve in the
    // active locale instead of humanising to English.
    const overrides = appStore.messageOverrides?.[locale] as
      | Record<string, unknown>
      | undefined
    /**
     * Build absolute paths during the tree walk. vue-router stores child
     * `route.path` as relative ("users") so a naive copy produces unrouteable
     * menu items — AdminShellRoot's `router.push(menu.path)` then resolves
     * relative to the current page and silently fails.
     */
    function joinPath(parent: string, child: string): string {
      if (child.startsWith('/')) return child
      const left = parent.endsWith('/') ? parent.slice(0, -1) : parent
      const right = child.startsWith('/') ? child.slice(1) : child
      return left ? `${left}/${right}` : `/${right}`
    }
    function toMenuItem(route: AdminRouteRecord, parentPath: string): AdminMenuItem | null {
      if (route.meta?.hideInMenu) return null
      if (!isVisible(route)) return null
      const rawTitle = route.meta?.title ?? route.name
      const absolutePath = joinPath(parentPath, route.path)
      // Phase I.7.6: when `meta.icon` is missing, fall back to the curated
      // default map keyed by route name (covers the 42 built-in admin pages).
      // Consumer-supplied routes still control via `meta.icon` if set.
      const resolvedIcon =
        route.meta?.icon ?? DEFAULT_ROUTE_ICONS[route.name]
      const item: AdminMenuItem = {
        key: route.name,
        label: resolveI18nKey(rawTitle, locale, overrides),
        i18nKey: route.meta?.i18nKey,
        icon: resolvedIcon,
        path: absolutePath,
        meta: route.meta,
      }
      if (route.children && route.children.length > 0) {
        const children = route.children
          .map((c) => toMenuItem(c, absolutePath))
          .filter(Boolean) as AdminMenuItem[]
        if (children.length > 0) {
          item.children = children
        } else {
          // A directory whose children were all filtered out (permission /
          // hideInMenu) would render as an empty, unclickable parent — drop it.
          return null
        }
      }
      return item
    }
    let items = allRoutes.value
      .map((r) => toMenuItem(r, ''))
      .filter(Boolean) as AdminMenuItem[]
    // 'merge' menu source: overlay backend Sys_Menu overrides (retitle / re-icon
    // / reorder / hide) keyed by route name. Reading backendMenuNodes here keeps
    // the menu reactive to it; empty (the default 'route' source) is a no-op.
    const backendOverrides = backendMenuNodes.value
    if (backendOverrides.length > 0) {
      items = applyMenuOverrides(items, indexMenuOverrides(backendOverrides))
    }
    items.sort((a, b) => (a.meta?.order ?? 999) - (b.meta?.order ?? 999))
    return items
  })

  function collectCacheRouteNames(routes: AdminRouteRecord[]): string[] {
    const names: string[] = []
    for (const route of routes) {
      if (route.meta?.keepAlive) names.push(route.name)
      if (route.children) {
        names.push(...collectCacheRouteNames(route.children))
      }
    }
    return names
  }

  function setConstantRoutes(routes: AdminRouteRecord[]): void {
    constantRoutes.value = routes
    cacheRoutes.value = collectCacheRouteNames([...routes, ...authRoutes.value])
  }

  /**
   * Register the application's auth routes. ALL routes are kept — vue-router and
   * the navigation guards still need them resolvable. Menu *visibility* is now
   * filtered reactively in the `menus` getter from the auth store, NOT here: the
   * historical install-time `filterRoutesByPermissions(routes, [])` ran before
   * login with an empty permission set, which (had the field names matched)
   * would have permanently stripped every protected route.
   *
   * `_userPermissions` is accepted for backward compatibility but no longer used
   * for physical filtering; pass it or omit it freely.
   */
  function setAuthRoutes(routes: AdminRouteRecord[], _userPermissions: string[] = []): void {
    authRoutes.value = routes
    cacheRoutes.value = collectCacheRouteNames([...constantRoutes.value, ...authRoutes.value])
    routesLoaded.value = true
  }

  function resetRouteCache(routeName: string): void {
    cacheRoutes.value = cacheRoutes.value.filter((n) => n !== routeName)
  }

  /** Feed the backend Sys_Menu tree for the 'merge' source; [] reverts to 'route'. */
  function setBackendMenus(nodes: MenuTreeNode[]): void {
    backendMenuNodes.value = nodes
  }

  function clearRoutes(): void {
    constantRoutes.value = []
    authRoutes.value = []
    backendMenuNodes.value = []
    cacheRoutes.value = []
    routesLoaded.value = false
  }

  return {
    constantRoutes,
    authRoutes,
    allRoutes,
    routesLoaded,
    backendMenuNodes,
    menus,
    cacheRoutes,
    setConstantRoutes,
    setAuthRoutes,
    setBackendMenus,
    resetRouteCache,
    clearRoutes,
  }
})
