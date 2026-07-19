import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { en } from '../locales/en'
import { zhCn } from '../locales/zh-cn'
import { DEFAULT_ROUTE_ICONS } from '../router/routeIcons'
import { humanise } from '../pages/_shared/translate'
import { useAdminAppStore } from './useAdminAppStore'
import { useAdminAuthStore } from './useAdminAuthStore'
import type { MenuTreeNode } from '@tnzi/core/services/system'
import { normalizeModuleName } from '../services/admin-shell-modules'

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

/**
 * Resolve the module-availability gate key for a route, or null when the route
 * isn't gated. `meta.moduleGate === true` → the route's own `name`; a string →
 * that explicit module short name; both normalized (lowercase, dots → dashes)
 * to line up with the backend's loaded-module short names.
 */
function moduleGateKey(route: AdminRouteRecord): string | null {
  const gate = route.meta?.moduleGate
  if (gate === true) return normalizeModuleName(route.name)
  if (typeof gate === 'string' && gate) return normalizeModuleName(gate)
  return null
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
  /**
   * Backend module-availability gate. When `defineAdminApp({ moduleGating })`
   * is on, a top-level module node carrying `moduleGate` is HIDDEN from the
   * menu and made unreachable if the backend host didn't load its module (per
   * `GET /admin/shell/modules`). `true` = gate by the route's own `name`;
   * a string = gate by that explicit module short name. Orthogonal to
   * permissions, so the gate holds for super-admins too. Absent = never gated.
   */
  moduleGate?: boolean | string
  /**
   * Stamped by `defineAdminApp` on the top-level module groups that ship
   * with `defaultAdminRoutes` (framework built-in admin pages). Drives the
   * sidebar's built-in-menus toggle: hiding built-ins leaves only the
   * consumer app's own menus (routes added via `addModules` stay
   * unstamped) plus neutral built-ins like the landing dashboard.
   * Display-only — never consulted by guards.
   */
  builtIn?: boolean
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
  /**
   * Loaded framework module short names (normalized), from `GET /admin/shell/modules`.
   * `null` = signal unavailable / not yet fetched → module gating is OFF
   * (fail-open, show everything). A Set (even empty) = signal known → gate
   * top-level `moduleGate` nodes whose module the backend didn't load.
   */
  const availableModules = ref<Set<string> | null>(null)

  /**
   * True while the module-availability signal is being fetched for the first
   * time (the `defineAdminApp().install()` probe). SIDE-EFFECTFUL surfaces
   * (the built-in chat host, dashboard data widgets, pollers) defer mounting
   * while this is true so they never fire requests at a module the incoming
   * signal is about to rule out. Pure-VISIBILITY surfaces (menus, `v-module`)
   * ignore this flag and stay fail-open, so the sidebar is never blanked
   * while the signal is in flight.
   */
  const moduleSignalPending = ref(false)

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
    // Role gate source — consumer routes only (no framework route sets
    // `meta.roles`), so this is zero-impact for existing apps. Same OR /
    // super-user-bypass / fail-open semantics as the permission gate: it lets a
    // role-driven app (e.g. Contoso's Owner/Management-only Staff pages) declare
    // `meta.roles` on a route instead of hand-mutating `hideInMenu` after every
    // navigation.
    const grantedRoles = new Set(authStore.userRoles.map((r) => r.toLowerCase()))
    const permissionsLoaded = authStore.userInfo !== null
    // Fail-open ONLY while a SESSION IS ACTIVE (token present) but its permission
    // list hasn't arrived yet — the async gap between setToken and setUserInfo on
    // login / session-restore, and consumers that wire auth but never call
    // loadPermissions. When LOGGED OUT (no token) do NOT fail-open: filter
    // normally so the sidebar collapses to public entries instead of flashing
    // EVERY menu. `userInfo === null` is reached in two very different runtime
    // states — "logged in, permissions still loading" (isLogin true → fail-open
    // is right) and "logged out" (isLogin false). Treating them the same is what
    // (a) flashed the full menu for the 1-2s the backend logout call takes before
    // the login redirect, and (b) let a freshly-switched role transiently see the
    // previous / full menu and click a page it can't open (→ 403). Gating on
    // `isLogin` separates the two. Super users still bypass unconditionally.
    const bypassPermissionFilter =
      authStore.isSuperUser || (!permissionsLoaded && authStore.isLogin)
    function isVisible(route: AdminRouteRecord): boolean {
      if (bypassPermissionFilter) return true
      // Permission gate (unchanged): singular wins; else plural ANY-of.
      const single = route.meta?.permission
      if (single) {
        if (!grantedPermissions.has(single.toLowerCase())) return false
      } else {
        const multi = route.meta?.permissions
        if (multi && multi.length > 0 && !multi.some((p) => grantedPermissions.has(p.toLowerCase()))) return false
      }
      // Role gate (consumer routes only): a route declaring `meta.roles` is
      // visible only to those roles (ANY-of). Absent → no role restriction.
      const roles = route.meta?.roles
      if (roles && roles.length > 0 && !roles.some((r) => grantedRoles.has(r.toLowerCase()))) return false
      return true
    }
    // Built-in-menus toggle (sidebar footer, super admin) — DISPLAY-ONLY,
    // orthogonal to both the permission filter and module gating. When OFF,
    // top-level groups stamped `meta.builtIn` (the framework's preset admin
    // pages) hide, leaving only the consumer app's own menus plus neutral
    // built-ins (the landing dashboard, which carries no permission anywhere
    // in its subtree). Guards and tabs are deliberately untouched: this
    // never creates a lockout, hidden pages stay reachable by URL. Gated on
    // isSuperUser so a persisted OFF from a super-admin session never hides
    // menus for the next (non-super) sign-in on the same browser.
    const hideBuiltIn = !appStore.showBuiltInMenus && authStore.isSuperUser
    function subtreeHasAnyPermission(route: AdminRouteRecord): boolean {
      const single = route.meta?.permission
      const multi = route.meta?.permissions
      if (single || (multi && multi.length > 0)) return true
      return (route.children ?? []).some(subtreeHasAnyPermission)
    }
    function passesBuiltInFilter(route: AdminRouteRecord): boolean {
      if (!hideBuiltIn) return true
      if (route.meta?.builtIn !== true) return true
      // Neutral built-in subtree (no permission anywhere — the landing
      // dashboard) → always visible.
      return !subtreeHasAnyPermission(route)
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
      // Module-availability gate — ORTHOGONAL to permissions, so it holds for
      // super users too (unlike isVisible below, which bypasses for them). When
      // the loaded-module signal is known (non-null) and this gated node's
      // module isn't loaded, drop it. Signal unknown (null) = fail-open.
      const gateKey = moduleGateKey(route)
      if (gateKey && availableModules.value !== null && !availableModules.value.has(gateKey)) {
        return null
      }
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
    // Built-in filter runs at the TOP LEVEL only — a kept group renders all
    // its leaves; consumer routes (unstamped) are never filtered here.
    let items = allRoutes.value
      .filter((r) => passesBuiltInFilter(r))
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

  /**
   * Route names the current user is NOT allowed to open — the inverse of the
   * sidebar filter, computed over the FULL route table (hidden-in-menu routes
   * included). Feeds `useAdminTabStore.pruneTabs` so a persisted tab pointing at
   * a page the freshly-signed-in user can't access is dropped instead of
   * lingering and 403'ing on click (the cross-session tab leak, sibling of the
   * `isSuperUser` leak).
   *
   * Uses the SAME fail-open rules as `menus`: empty (deny nothing) for super
   * users and before the permission list loads — the backend `[ApiAuthorize]`
   * stays the real enforcement. A route with no `meta.permission` (and no plural
   * `permissions`) is public and never denied, so hidden utility routes
   * (user-center, settings, id-driven detail pages) survive.
   */
  const deniedRouteNames = computed<Set<string>>(() => {
    const authStore = useAdminAuthStore()
    const denied = new Set<string>()
    const permissionsLoaded = authStore.userInfo !== null
    if (authStore.isSuperUser || !permissionsLoaded) return denied
    const granted = new Set(authStore.userPermissions.map((p) => p.toLowerCase()))
    const grantedRoles = new Set(authStore.userRoles.map((r) => r.toLowerCase()))
    function isAllowed(route: AdminRouteRecord): boolean {
      const single = route.meta?.permission
      if (single) {
        if (!granted.has(single.toLowerCase())) return false
      } else {
        const multi = route.meta?.permissions
        if (multi && multi.length > 0 && !multi.some((p) => granted.has(p.toLowerCase()))) return false
      }
      // Role gate mirrors the menu filter, so a persisted tab pointing at a
      // role-restricted page the current user can't hold is pruned + the guard
      // bounces its deep link to `forbidden`.
      const roles = route.meta?.roles
      if (roles && roles.length > 0 && !roles.some((r) => grantedRoles.has(r.toLowerCase()))) return false
      return true
    }
    function walk(route: AdminRouteRecord): void {
      if (!isAllowed(route)) denied.add(route.name)
      route.children?.forEach(walk)
    }
    allRoutes.value.forEach(walk)
    return denied
  })

  /**
   * Route names unreachable because their FRAMEWORK MODULE isn't loaded by the
   * backend — the module-availability twin of `deniedRouteNames`. Computed over
   * the FULL route table (a gated top-level node + ALL its descendants), so the
   * navigation guard can bounce a deep link / persisted tab into an unloaded
   * module to /403 and such tabs get pruned. ORTHOGONAL to permissions, so it
   * applies to super-admins too; empty when the loaded-module signal is
   * unavailable (null) — fail-open, same as the menu layer.
   */
  const unavailableRouteNames = computed<Set<string>>(() => {
    const denied = new Set<string>()
    const available = availableModules.value
    if (available === null) return denied
    const collect = (route: AdminRouteRecord): void => {
      denied.add(route.name)
      route.children?.forEach(collect)
    }
    const walk = (route: AdminRouteRecord): void => {
      const gateKey = moduleGateKey(route)
      if (gateKey && !available.has(gateKey)) {
        collect(route)
        return
      }
      route.children?.forEach(walk)
    }
    allRoutes.value.forEach(walk)
    return denied
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

  /**
   * Set the loaded-module signal (from `GET /admin/shell/modules`). Pass a Set
   * of normalized module short names to enable module gating, or `null` to
   * disable it (fail-open). Drives both the `menus` module gate and
   * `unavailableRouteNames` (guard + tab pruning).
   */
  function setAvailableModules(names: Set<string> | null): void {
    availableModules.value = names
  }

  /**
   * Flip the "module signal in flight" flag. `defineAdminApp().install()` sets
   * it `true` when it starts the availability probe and `false` once the probe
   * settles (fetched, failed, or timed out) — see {@link moduleSignalPending}.
   */
  function setModuleSignalPending(pending: boolean): void {
    moduleSignalPending.value = pending
  }

  function clearRoutes(): void {
    constantRoutes.value = []
    authRoutes.value = []
    backendMenuNodes.value = []
    availableModules.value = null
    moduleSignalPending.value = false
    cacheRoutes.value = []
    routesLoaded.value = false
  }

  return {
    constantRoutes,
    authRoutes,
    allRoutes,
    routesLoaded,
    backendMenuNodes,
    availableModules,
    moduleSignalPending,
    menus,
    deniedRouteNames,
    unavailableRouteNames,
    cacheRoutes,
    setConstantRoutes,
    setAuthRoutes,
    setBackendMenus,
    setAvailableModules,
    setModuleSignalPending,
    resetRouteCache,
    clearRoutes,
  }
})
