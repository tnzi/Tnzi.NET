/**
 * Route-table transforms for `defineAdminApp`.
 *
 * These are the pure functions that turn the shipped `defaultAdminRoutes`
 * table into the one an app actually mounts: prefixing the base path, marking
 * which groups are framework built-ins, applying the consumer's
 * hide/show/order/override/append options, and converting the result back into
 * the shape the route store consumes.
 *
 * They live here rather than inside `defineAdminApp.ts` because they are a
 * cohesive, side-effect-free unit with no dependency on the factory's closure -
 * which also makes them directly testable without building an app.
 */
import type { Component } from 'vue'
import type { RouteRecordRaw } from 'vue-router'
import type { AdminRouteRecord } from '../stores/useAdminRouteStore'

export function normalizeName(name: string): string {
  return name.toLowerCase().replace(/\./g, '-')
}

/**
 * Normalize the `basePath` option to the canonical form used internally:
 * leading slash, no trailing slash (except for the domain-root sentinel `'/'`).
 *
 *   `undefined` / `null` / `''`     → `'/admin'`
 *   `'admin'` / `'admin/'`           → `'/admin'`
 *   `'/admin/'` / `'/admin'`         → `'/admin'`
 *   `'/console/'` / `'console'`     → `'/console'`
 *   `'/'`                            → `'/'`
 */
export function normalizeBasePath(basePath?: string | null): string {
  if (basePath == null) return '/admin'
  let bp = String(basePath).trim()
  if (bp === '') return '/admin'
  if (bp === '/') return '/'
  if (!bp.startsWith('/')) bp = '/' + bp
  while (bp.length > 1 && bp.endsWith('/')) bp = bp.slice(0, -1)
  return bp
}

/**
 * Rewrite every **top-level** route's path so it sits under `basePath`.
 *
 *   `/admin`              → `basePath`
 *   `/login/:module(...)` → `${basePath}/login/:module(...)`
 *                            (no leading `//` when `basePath === '/'`)
 *   `/403`                → `${basePath}/403`
 *
 * This applies to the DEFAULT `/admin` basePath too (since 0.2.71): every
 * route in the table, login and 403 included, lives under the single
 * basePath prefix. Before that, login/403 stayed at the domain root under
 * the default: 99% of URLs happened to work when the SPA was mounted as
 * an IIS sub-application (the internal `/admin` prefix coincided with the
 * deployment prefix), but any login/403 redirect escaped the
 * sub-application (e.g. `https://host/login` instead of
 * `https://host/admin/login`) and 404'd.
 *
 * `admin-root.children` use relative paths and are not touched - they
 * inherit the new parent automatically when vue-router resolves the tree.
 *
 * Returns a new array; input is not mutated.
 */
export function applyBasePath(
  routes: RouteRecordRaw[],
  basePath: string,
): RouteRecordRaw[] {
  // Domain-root deployment: only rewrite `/admin` (→ `/`); leave `/login`,
  // `/403`, and other top-level siblings on their existing paths.
  if (basePath === '/') {
    return routes.map((route) => {
      if (route.path === '/admin') {
        return { ...route, path: '/' } as RouteRecordRaw
      }
      return route
    })
  }
  return routes.map((route) => {
    if (typeof route.path !== 'string') return route
    if (route.path === '/admin') {
      return { ...route, path: basePath } as RouteRecordRaw
    }
    // Only rewrite top-level absolute paths; relative child paths (none
    // exist at top level in the preset, but defend in depth) stay as-is.
    if (route.path.startsWith('/')) {
      return { ...route, path: basePath + route.path } as RouteRecordRaw
    }
    return route
  })
}

/**
 * Stamp `meta.builtIn: true` on the top-level module groups under `/admin`
 * - the framework's preset admin pages. Runs against the ORIGINAL preset
 * before `addModules` appends consumer routes, so consumer menus stay
 * unstamped and survive the sidebar's built-in-menus toggle. Clones the
 * touched nodes instead of mutating `defaultAdminRoutes` (a shared module
 * constant reused across `defineAdminApp` calls).
 */
export function markBuiltInModules(routes: RouteRecordRaw[]): RouteRecordRaw[] {
  return routes.map((route) => {
    if (route.path !== '/admin' || !route.children) return route
    return {
      ...route,
      children: route.children.map((child) => ({
        ...child,
        meta: { ...child.meta, builtIn: true },
      })),
    } as RouteRecordRaw
  })
}

/** Walk the route tree and return a new tree with the named module subtrees removed. */
export function filterModules(
  routes: RouteRecordRaw[],
  hideSet: Set<string>,
  showOnlySet: Set<string> | null,
): RouteRecordRaw[] {
  return routes.map((route) => {
    // Only filter at the level beneath `/admin`. Modules are second-level
    // children (e.g. `/admin/identity`, `/admin/ai`).
    if (route.path !== '/admin') return route
    if (!route.children) return route
    const filteredChildren = route.children.filter((child) => {
      const key = normalizeName(typeof child.name === 'string' ? child.name : '')
      if (showOnlySet && !showOnlySet.has(key)) return false
      if (hideSet.has(key)) return false
      return true
    })
    return { ...route, children: filteredChildren }
  })
}

/**
 * Walk the children (and grandchildren) of every `/admin` route and set
 * `meta.hideInMenu = true` on any route whose `name` (string, exact
 * match - case-sensitive) appears in `hideSet`. Returns a new route tree;
 * the input is not mutated.
 *
 * Top-level routes (`/login`, `/403`, `/admin`) are never considered for
 * matching - `hideRoutes` is intended for sub-menu entries only.
 */
export function applyHideRoutes(
  routes: RouteRecordRaw[],
  hideSet: Set<string>,
): RouteRecordRaw[] {
  if (hideSet.size === 0) return routes
  function walk(route: RouteRecordRaw): RouteRecordRaw {
    let next: RouteRecordRaw = route
    const name = typeof route.name === 'string' ? route.name : ''
    if (name && hideSet.has(name)) {
      const nextMeta = { ...(route.meta as Record<string, unknown> | undefined), hideInMenu: true }
      next = { ...route, meta: nextMeta } as RouteRecordRaw
    }
    if (next.children && next.children.length > 0) {
      next = { ...next, children: next.children.map(walk) } as RouteRecordRaw
    }
    return next
  }
  return routes.map((route) => {
    if (route.path !== '/admin') return route
    if (!route.children) return route
    return { ...route, children: route.children.map(walk) }
  })
}

/**
 * Walk the children (and grandchildren) of every `/admin` route and
 * override `meta.order` on any route whose `name` (string, exact
 * match - case-sensitive) is a key in `orders`. Returns a new route
 * tree; the input is not mutated.
 *
 * Top-level routes (`/login`, `/403`, `/admin`) are never considered for
 * matching - `routeOrders` is intended for sub-route ordering only
 * (the menu builder in `useAdminRouteStore` sorts by `meta.order` on
 * each level).
 */
export function applyRouteOrders(
  routes: RouteRecordRaw[],
  orders: Record<string, number>,
): RouteRecordRaw[] {
  const keys = Object.keys(orders)
  if (keys.length === 0) return routes
  function walk(route: RouteRecordRaw): RouteRecordRaw {
    let next: RouteRecordRaw = route
    const name = typeof route.name === 'string' ? route.name : ''
    if (name && Object.prototype.hasOwnProperty.call(orders, name)) {
      const nextMeta = { ...(route.meta as Record<string, unknown> | undefined), order: orders[name] }
      next = { ...route, meta: nextMeta } as RouteRecordRaw
    }
    if (next.children && next.children.length > 0) {
      next = { ...next, children: next.children.map(walk) } as RouteRecordRaw
    }
    return next
  }
  return routes.map((route) => {
    if (route.path !== '/admin') return route
    if (!route.children) return route
    return { ...route, children: route.children.map(walk) }
  })
}

/** Walk the route tree and apply component overrides by route name. */
export function applyOverrides(
  routes: RouteRecordRaw[],
  overrides: Record<string, Component | (() => Promise<unknown>)>,
): RouteRecordRaw[] {
  function walk(route: RouteRecordRaw): RouteRecordRaw {
    const name = typeof route.name === 'string' ? route.name : ''
    const override = name ? overrides[name] : undefined
    const next: RouteRecordRaw = override
      ? ({ ...route, component: override as Component } as RouteRecordRaw)
      : route
    if (next.children && next.children.length > 0) {
      return { ...next, children: next.children.map(walk) } as RouteRecordRaw
    }
    return next
  }
  return routes.map(walk)
}

/** Append consumer-supplied child routes under `/admin`. */
export function appendUnderAdmin(
  routes: RouteRecordRaw[],
  extras: RouteRecordRaw[],
): RouteRecordRaw[] {
  if (extras.length === 0) return routes
  return routes.map((route) => {
    if (route.path !== '/admin') return route
    return {
      ...route,
      children: [...(route.children ?? []), ...extras],
    }
  })
}

/** Replace placeholder login / forbidden components when consumer provides their own. */
export function applyPlaceholders(
  routes: RouteRecordRaw[],
  login?: Component,
  forbidden?: Component,
): RouteRecordRaw[] {
  return routes.map((route) => {
    // After Phase I.7.1 the default login route is
    // `/login/:module(pwd-login|...)?` - match by name instead of literal
    // path so consumer overrides keep working.
    if (login && route.name === 'login') {
      return { ...route, component: login } as RouteRecordRaw
    }
    if (forbidden && route.path === '/403') {
      return { ...route, component: forbidden } as RouteRecordRaw
    }
    return route
  })
}

/**
 * Convert vue-router `RouteRecordRaw` into the `AdminRouteRecord` shape that
 * `useAdminRouteStore` expects. Drops `component` (which the store doesn't
 * care about) and preserves children + meta.
 */
export function toAdminRouteRecords(
  routes: RouteRecordRaw[],
  pathPrefix = '',
): AdminRouteRecord[] {
  function joinPath(parent: string, child: string): string {
    if (child.startsWith('/')) return child
    const left = parent.endsWith('/') ? parent.slice(0, -1) : parent
    const right = child.startsWith('/') ? child.slice(1) : child
    return left ? `${left}/${right}` : `/${right}`
  }
  function walk(route: RouteRecordRaw, parentPath: string): AdminRouteRecord {
    const absolutePath = joinPath(parentPath, route.path)
    const rawMeta = route.meta as Record<string, unknown> | undefined
    const meta: AdminRouteRecord['meta'] = {
      title: (rawMeta?.title as string | undefined) ?? (
        typeof route.name === 'string' ? route.name : route.path
      ),
      i18nKey: rawMeta?.i18nKey as string | undefined,
      icon: rawMeta?.icon as string | undefined,
      order: rawMeta?.order as number | undefined,
      constant: rawMeta?.constant as boolean | undefined,
      keepAlive: rawMeta?.keepAlive as boolean | undefined,
      hideInMenu: rawMeta?.hideInMenu as boolean | undefined,
      // Carry BOTH the singular `permission` (what the real route table uses, 71×)
      // and plural `permissions`. Dropping the singular one was the root of the
      // silently-disabled menu permission filter.
      permission: rawMeta?.permission as string | undefined,
      permissions: rawMeta?.permissions as string[] | undefined,
      roles: rawMeta?.roles as string[] | undefined,
      activeMenu: rawMeta?.activeMenu as string | undefined,
      fixedIndexInTab: rawMeta?.fixedIndexInTab as number | undefined,
      multiTab: rawMeta?.multiTab as boolean | undefined,
      // Module-availability gate marker. Like `permission` above, this MUST be
      // copied through the vue-router-record → AdminRouteRecord round-trip, or
      // the store's `moduleGateKey` sees `undefined` and never gates the node
      // (the sidebar keeps showing menus for modules the backend never loaded).
      moduleGate: rawMeta?.moduleGate as boolean | string | undefined,
      // Built-in marker (stamped by markBuiltInModules) - same round-trip
      // rule as `permission`/`moduleGate`: dropped here = the built-in-menus
      // toggle silently never filters anything.
      builtIn: rawMeta?.builtIn as boolean | undefined,
    }
    const record: AdminRouteRecord = {
      name: typeof route.name === 'string' ? route.name : route.path,
      // Store absolute paths so the menu builder can navigate directly
      // (avoids "/identity/users" relative-path silent failure).
      path: absolutePath,
      meta,
    }
    if (route.children && route.children.length > 0) {
      record.children = route.children.map((c) => walk(c, absolutePath))
    }
    return record
  }
  return routes.map((r) => walk(r, pathPrefix))
}
