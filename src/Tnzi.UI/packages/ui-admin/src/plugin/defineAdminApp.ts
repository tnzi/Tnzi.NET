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
import {
  useAdminRouteStore,
  type AdminRouteRecord,
} from '../stores/useAdminRouteStore'
import { useRouteProgress } from '../headless/useRouteProgress'

export interface DefineAdminAppOptions {
  /** Backend HttpClient that admin bridges use to talk to the API. */
  client: HttpClient

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

  let routes = [...defaultAdminRoutes]
  routes = filterModules(routes, hideSet, showOnlySet)
  if (options.overridePages) {
    routes = applyOverrides(routes, options.overridePages)
  }
  if (options.addModules && options.addModules.length > 0) {
    routes = appendUnderAdmin(routes, options.addModules)
  }
  routes = applyPlaceholders(routes, options.loginComponent, options.forbiddenComponent)

  function install(app: App, pinia?: Pinia, router?: Router): TnziUiAdminInstance {
    const instance = createTnziUiAdmin(app, {
      ...(options.pluginOptions ?? {}),
      client: options.client,
      pinia,
      login: options.login,
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
    const adminRoot = routes.find((r) => r.path === '/admin')
    if (adminRoot?.children && adminRoot.children.length > 0) {
      // Pass the `/admin` parent path so toAdminRouteRecords prepends it to
      // every descendant route's `.path`. Without this prefix the menu
      // builder produces "/identity/users" — silently unrouteable.
      routeStore.setAuthRoutes(
        toAdminRouteRecords(adminRoot.children, '/admin'),
        [],
      )
    }

    return instance
  }

  return { routes, install }
}
