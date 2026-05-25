/**
 * `defineAdminApp()` — convenience factory for booting a Tnzi admin app.
 *
 * Wraps three previously-manual steps that every consumer (Acme, music,
 * webshop, Fabrikam-AI…) had to repeat in their own `admin/main.ts`:
 *   1. Take `defaultAdminRoutes` and filter / override / extend them.
 *   2. Bridge consumer-supplied `RouteRecordRaw[]` to the admin route store
 *      (so `TAdminSidebar` can render the menu).
 *   3. Install the `@tnzi/ui-admin` plugin (pinia plugin + DI + global
 *      Ctrl/Cmd-K shortcut).
 *
 * Phase B (0.2.4) ships this as the new front-door API. Consumers can still
 * call `createTnziUiAdmin()` directly if they want fine-grained control.
 *
 * @example
 * ```ts
 * // admin/main.ts (consumer side, ~10 lines)
 * import { createApp } from 'vue'
 * import { createPinia } from 'pinia'
 * import { createRouter, createWebHistory } from 'vue-router'
 * import { defineAdminApp } from '@tnzi/ui-admin'
 * import App from './App.vue'
 * import { getAcmeApp } from '@acme/shared'
 *
 * const { http } = getAcmeApp()
 * const { routes, install } = defineAdminApp({
 *   client: http,
 *   hideModules: ['Payment'],          // optional
 *   overridePages: { 'identity.users': MyUserPage }, // optional
 * })
 * const router = createRouter({ history: createWebHistory(), routes })
 * const pinia = createPinia()
 * const app = createApp(App)
 * app.use(pinia); app.use(router)
 * install(app, pinia)
 * app.mount('#app')
 * ```
 */

import type { App, Component } from 'vue'
import type { Pinia } from 'pinia'
import type { RouteRecordRaw, Router } from 'vue-router'
import type { HttpClient } from '@tnzi/core/http'
import { defaultAdminRoutes } from '../router/routes'
import {
  createTnziUiAdmin,
  type TnziUiAdminInstance,
  type TnziUiAdminOptions,
} from './index'
import type { AdminLoginConfig } from './loginConfig'
import type { AdminWorkbenchConfig } from './workbenchConfig'
import {
  useAdminRouteStore,
  type AdminRouteRecord,
} from '../stores/useAdminRouteStore'
import { useRouteProgress } from '../headless/useRouteProgress'

export interface DefineAdminAppOptions {
  /** Backend HttpClient that admin bridges use to talk to the API. */
  client: HttpClient

  /**
   * Root path the admin SPA is deployed under inside vue-router. Rewrites every
   * top-level framework route so the router's internal path matches the
   * browser URL the consumer actually sees.
   *
   * Defaults to `'/admin'` — the historical behaviour. Pass `'/console'`,
   * `'/portal'`, ... to deploy under a different prefix, or `'/'` to deploy
   * at the domain root (no prefix).
   *
   * Affects every top-level route in the preset table:
   *   - `admin-root` (`/admin`) → `basePath`
   *   - `login` (`/login/:module(...)?`) → `${basePath}/login/:module(...)?`
   *     (no leading `//` when `basePath === '/'`)
   *   - `forbidden` (`/403`) → `${basePath}/403`
   *
   * Routes under `admin-root.children` use relative paths and are not
   * touched — they inherit the new parent automatically.
   *
   * Normalization:
   *   - `'admin'`, `'/admin'`, `'/admin/'` → `'/admin'`
   *   - `''`, `null`, `undefined` → `'/admin'` (default)
   *   - `'/'` is left as-is (domain-root deployment)
   *
   * Does **not** touch `createWebHistory()` / `createWebHashHistory()`
   * base — that argument controls the browser URL the router observes and
   * is the consumer's responsibility. `basePath` only rewrites the routes
   * inside vue-router's table, so when an IIS sub-application (or nginx
   * `location`) mounts the admin SPA under `/admin/` and serves
   * `index.html` from that prefix, `createWebHistory()` (default `'/'`)
   * keeps the browser URL and router-internal paths aligned.
   *
   * @example
   * ```ts
   * // Default — same behaviour as today (basePath = '/admin')
   * defineAdminApp({ client })
   *
   * // Custom base path
   * defineAdminApp({ client, basePath: '/console' })
   *
   * // Deploy at domain root (no prefix)
   * defineAdminApp({ client, basePath: '/' })
   * ```
   */
  basePath?: string

  /**
   * Module short names to hide from the menu (case-insensitive, dot/dash
   * normalized — so `"AI.Skills"`, `"ai.skills"`, `"ai-skills"` all match).
   * Hidden modules have their child routes stripped entirely.
   */
  hideModules?: string[]

  /**
   * Module short names to show — when set, anything not listed is hidden.
   * Useful when you want to keep just a few modules instead of listing
   * everything to hide.
   */
  showOnlyModules?: string[]

  /**
   * Hide individual sub-menu entries from the sidebar without removing
   * them from the route table. Accepts exact vue-router route names
   * (case-sensitive — `"identity.tenants"` matches, `"Identity.Tenants"`
   * does not) and walks `/admin` children + grandchildren, setting
   * `meta.hideInMenu = true` on every match.
   *
   * Use this when you want to keep the route reachable (deep links,
   * programmatic `router.push`, breadcrumb fallback) but suppress it
   * from `useAdminRouteStore.menus` so the sidebar doesn't show it.
   *
   * For hiding entire top-level modules (which also strips their child
   * routes from the table), use `hideModules` instead — the two options
   * are independent and may be combined.
   *
   * @example
   * ```ts
   * defineAdminApp({
   *   client,
   *   hideModules: ['Payment'],
   *   hideRoutes: ['identity.tenants', 'identity.organizations', 'system.signalr'],
   * })
   * ```
   */
  hideRoutes?: string[]

  /**
   * Override the default `meta.order` of any framework route. Keyed by
   * exact vue-router route `name` (case-sensitive — `"workbench"` matches,
   * `"Workbench"` does not). Walks `/admin` children + grandchildren and
   * writes `meta.order = routeOrders[route.name]` on every match.
   *
   * Useful when the default ordering ships the modules in a position you
   * don't want — e.g. lift `authorization` ahead of `identity`, or push
   * `workbench` down a notch.
   *
   * Framework defaults (step 10, leaving room for consumer entries):
   *   - `workbench` = 0
   *   - `identity` = 100
   *   - `authorization` = 110
   *   - `system` = 120
   *   - `audit` = 130
   *   - `chat` = 140
   *   - `ai` = 150
   *   - `storage` = 160
   *   - `notification` = 170
   *   - `payment` = 180
   *   - `template` = 190
   *
   * Consumer modules registered via `addModules` with `meta.order` in
   * `1..99` slot between Workbench and the first framework module, and
   * `200+` slot after the last one — no mutation of the route table
   * needed.
   *
   * `routeOrders` is independent of `hideRoutes` / `hideModules` /
   * `overridePages` / `addModules` and may be combined freely.
   *
   * @example
   * ```ts
   * defineAdminApp({
   *   client,
   *   routeOrders: {
   *     workbench: 5,        // shift Workbench from 0
   *     authorization: 95,    // pull ahead of identity (100)
   *   },
   * })
   * ```
   */
  routeOrders?: Record<string, number>

  /**
   * Replace specific built-in page components. Keyed by route name
   * (e.g. `"identity.users"`, `"ai.agents.list"`).
   *
   * Override semantics:
   *   - The route table position and `meta` are preserved
   *   - Only the `component` field is swapped
   *   - Permission checks, keepAlive, breadcrumb labels stay intact
   */
  overridePages?: Record<string, Component | (() => Promise<unknown>)>

  /**
   * Append additional route children under `/admin`. Useful for consumer
   * business pages that don't belong to any built-in module.
   */
  addModules?: RouteRecordRaw[]

  /**
   * Replace the built-in `/login/:module(…)?` route component (rare —
   * consumers usually configure `login` below instead, keeping the
   * Phase I.7 shell and only swapping callbacks / branding).
   */
  loginComponent?: Component

  /**
   * Configuration for the built-in `/login/:module(…)?` route shipped by
   * `@tnzi/ui-admin` since Phase I.7. Provides auth callbacks (mandatory
   * for a working login), brand title, demo accounts, etc.
   */
  login?: AdminLoginConfig

  /**
   * Configuration for the built-in Workbench dashboard. Declare your
   * widget deck inline; omit to use the bundled
   * `defaultWorkbenchWidgets()` set (HeaderBanner + KPIs + business
   * stats + activity timeline + tips).
   */
  workbench?: AdminWorkbenchConfig

  /** Replace the placeholder `/403` forbidden component. */
  forbiddenComponent?: Component

  /**
   * Extra options forwarded to `createTnziUiAdmin()`. Mostly: pass a custom
   * `globalSearchShortcut`, or set `installPersistedstate: false` if you
   * already installed the pinia plugin yourself.
   */
  pluginOptions?: Omit<TnziUiAdminOptions, 'client' | 'pinia'>
}

export interface DefineAdminAppResult {
  /** Filtered + extended route table ready to feed `createRouter({ routes })`. */
  routes: RouteRecordRaw[]

  /**
   * Install the admin plugin. Call after `app.use(pinia)` and
   * `app.use(router)` — wires up the HttpClient DI, persistedstate, the
   * Ctrl/Cmd-K shortcut, and seeds `useAdminRouteStore` from `routes`
   * so the sidebar renders.
   *
   * Passing `router` is optional but recommended — the install will then
   * attach the soybean-style route progress bar
   * (`useRouteProgress(router)`).
   */
  install(app: App, pinia?: Pinia, router?: Router): TnziUiAdminInstance
}

function normalizeName(name: string): string {
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
function normalizeBasePath(basePath?: string | null): string {
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
 * `admin-root.children` use relative paths and are not touched — they
 * inherit the new parent automatically when vue-router resolves the tree.
 *
 * Returns a new array; input is not mutated.
 */
function applyBasePath(
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
  // Skip if the caller asked for the default — preserves identity so
  // tests that compare against `defaultAdminRoutes` directly keep passing.
  if (basePath === '/admin') return routes
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

/** Walk the route tree and return a new tree with the named module subtrees removed. */
function filterModules(
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
 * match — case-sensitive) appears in `hideSet`. Returns a new route tree;
 * the input is not mutated.
 *
 * Top-level routes (`/login`, `/403`, `/admin`) are never considered for
 * matching — `hideRoutes` is intended for sub-menu entries only.
 */
function applyHideRoutes(
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
 * match — case-sensitive) is a key in `orders`. Returns a new route
 * tree; the input is not mutated.
 *
 * Top-level routes (`/login`, `/403`, `/admin`) are never considered for
 * matching — `routeOrders` is intended for sub-route ordering only
 * (the menu builder in `useAdminRouteStore` sorts by `meta.order` on
 * each level).
 */
function applyRouteOrders(
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
function applyOverrides(
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
function appendUnderAdmin(
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
function applyPlaceholders(
  routes: RouteRecordRaw[],
  login?: Component,
  forbidden?: Component,
): RouteRecordRaw[] {
  return routes.map((route) => {
    // After Phase I.7.1 the default login route is
    // `/login/:module(pwd-login|...)?` — match by name instead of literal
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
function toAdminRouteRecords(
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
      permissions: rawMeta?.permissions as string[] | undefined,
      roles: rawMeta?.roles as string[] | undefined,
      activeMenu: rawMeta?.activeMenu as string | undefined,
      fixedIndexInTab: rawMeta?.fixedIndexInTab as number | undefined,
      multiTab: rawMeta?.multiTab as boolean | undefined,
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

export function defineAdminApp(options: DefineAdminAppOptions): DefineAdminAppResult {
  const hideSet = new Set((options.hideModules ?? []).map(normalizeName))
  const showOnlySet = options.showOnlyModules
    ? new Set(options.showOnlyModules.map(normalizeName))
    : null

  const hideRoutesSet = new Set(options.hideRoutes ?? [])
  const basePath = normalizeBasePath(options.basePath)

  // Internal transforms below match on the original `/admin` path. Apply
  // them first against the preset, then rewrite top-level paths via
  // applyBasePath as the final step — this keeps the helpers single-purpose
  // and lets us avoid threading basePath through every walker.
  let routes = [...defaultAdminRoutes]
  routes = filterModules(routes, hideSet, showOnlySet)
  if (hideRoutesSet.size > 0) {
    routes = applyHideRoutes(routes, hideRoutesSet)
  }
  if (options.routeOrders && Object.keys(options.routeOrders).length > 0) {
    routes = applyRouteOrders(routes, options.routeOrders)
  }
  if (options.overridePages) {
    routes = applyOverrides(routes, options.overridePages)
  }
  if (options.addModules && options.addModules.length > 0) {
    routes = appendUnderAdmin(routes, options.addModules)
  }
  routes = applyPlaceholders(routes, options.loginComponent, options.forbiddenComponent)
  routes = applyBasePath(routes, basePath)

  function install(app: App, pinia?: Pinia, router?: Router): TnziUiAdminInstance {
    const instance = createTnziUiAdmin(app, {
      ...(options.pluginOptions ?? {}),
      client: options.client,
      pinia,
      login: options.login,
      workbench: options.workbench,
    })

    // Attach the soybean-style route progress bar if a router is provided.
    // Idempotent — safe if the consumer already called useRouteProgress.
    if (router) {
      useRouteProgress(router)
    }

    // Seed the route store so TAdminSidebar can render the menu. The store
    // must be active for this — consumer must `app.use(pinia)` before calling
    // install().
    const routeStore = useAdminRouteStore()
    // Look up the admin root by name — its path now matches basePath, not
    // necessarily '/admin'. Using name keeps the lookup stable regardless
    // of which prefix the consumer asked for.
    const adminRoot = routes.find((r) => r.name === 'admin-root')
    if (adminRoot?.children && adminRoot.children.length > 0) {
      // Pass the basePath as the parent path so toAdminRouteRecords prepends
      // it to every descendant route's `.path`. Without this prefix the menu
      // builder produces "/identity/users" — silently unrouteable.
      // basePath === '/' falls back to '' so children land at "/identity/...".
      const childPrefix = basePath === '/' ? '' : basePath
      routeStore.setAuthRoutes(
        toAdminRouteRecords(adminRoot.children, childPrefix),
        [],
      )
    }

    return instance
  }

  return { routes, install }
}
