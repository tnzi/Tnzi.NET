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
import { inject } from 'vue'
import type { Pinia } from 'pinia'
import type { RouteRecordRaw, Router } from 'vue-router'
import type { HttpClient } from '@tnzi/core/http'
import { THEME_CONTEXT_KEY, type ThemeContext } from '@tnzi/ui'
import { useAdminFunctionAuthorizationApi } from '@tnzi/core/services/authorization'
import { useAdminMenuApi } from '@tnzi/core/services/system'
import { createIdentityBridge } from '../services/bridges/identity-bridge'
import type { MenuSeedResultDto } from '@tnzi/core/services/system'
import { exportRouteMenuSeed } from '../headless/menuSeed'
import { defaultAdminRoutes } from '../router/routes'
import { createAuthGuard, createModuleGuard, createPermissionGuard } from '../router/guards'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { useAdminTabStore } from '../stores/useAdminTabStore'
import {
  createTnziUiAdmin,
  type TnziUiAdminInstance,
  type TnziUiAdminOptions,
} from './index'
import type { AdminLoginConfig } from './loginConfig'
import type { AdminDashboardConfig } from './dashboardConfig'
import type { AdminSettingsConfig } from './settingsConfig'
import { ADMIN_DEEP_LINK_KEY, resolveDeepLinkConfig, type AdminDeepLinkConfig } from './deepLinkConfig'
import type { AdminThemeConfig } from './themeConfig'
import {
  useAdminRouteStore,
  type AdminRouteRecord,
} from '../stores/useAdminRouteStore'
import { useRouteProgress } from '../headless/useRouteProgress'
import { waitForClientToken } from '../headless/waitForClientToken'
import { useGlobalTheme } from '../headless/useGlobalTheme'
import { fetchAdminShellModules } from '../services/admin-shell-modules'

export interface DefineAdminAppOptions {
  /** Backend HttpClient that admin bridges use to talk to the API. */
  client: HttpClient

  /**
   * LEGACY FALLBACK - normally unnecessary. `loadPermissions` resolves the
   * super-admin flag from the backend's `GET /admin/function-authorization/
   * access-profile` (authoritative, mirrors `Authorization:SuperAdminRoles`),
   * so no front-end mirror of role names is needed. This option only kicks in
   * when that endpoint is unavailable (older backend): the shell then infers
   * `isSuperUser` from the signed-in user's role names (case-insensitive).
   *
   * Everyone who is not a super admin is deny-by-default: the backend returns
   * only explicitly granted permission codes, and the sidebar / guards filter
   * naturally. See docs/modules/authorization.md.
   */
  superAdminRoles?: string[]

  /**
   * Menu source — where the sidebar's structure comes from.
   *  • `'route'` (default): purely derived from the front-end route table.
   *  • `'merge'`: the route table stays the source of truth for WHICH pages
   *    exist, but backend `Sys_Menu` rows (keyed by route name via `menuKey`)
   *    override an entry's title / icon / order / visibility without a redeploy.
   *    Call `loadMenus(userId)` after login to fetch them.
   */
  menu?: { source?: 'route' | 'merge' }

  /**
   * Backend module-availability gating (default ON). When enabled, the shell
   * fetches `GET /admin/shell/modules` — which framework `TnziApplicationModule`s
   * the backend host actually loaded — and HIDES the menu + makes UNREACHABLE
   * every top-level module the backend didn't load (each framework module route
   * carries `meta.moduleGate`). So a host that doesn't `DependsOn` Finance /
   * Payment / AI never surfaces their dead menus that 404 on click, with ZERO
   * per-consumer configuration — and, because the gate is orthogonal to the
   * permission system, it holds for super-admins and permission-exempt paths
   * too (which the permission filter alone can't cover).
   *
   * INDEPENDENT of `hideModules` / `showOnlyModules`: those physically strip
   * routes for product-level trimming (and can hide a module the backend DID
   * load); module gating auto-hides only modules the backend DIDN'T load. The
   * two combine freely — e.g. hide a loaded `chat` from the sidebar while
   * keeping module gating on for the rest.
   *
   * Fails OPEN: when the endpoint is unavailable (older backend / network
   * failure) nothing is gated, so the sidebar is never blanked. Set `false`
   * (or `{ enabled: false }`) to opt out entirely and fall back to the historic
   * "show every route, filter by permission only" behaviour.
   *
   * A consumer module registered via `addModules` can opt into the same gating
   * by setting `meta.moduleGate: '<its-TnziApplicationModule-short-name>'` on
   * its top-level route — it then also appears in `/admin/shell/modules` (being
   * a `TnziApplicationModule`) and auto-hides when the host omits it.
   */
  moduleGating?: boolean | { enabled?: boolean }

  /**
   * Root path the admin SPA is deployed under inside vue-router. Rewrites every
   * top-level framework route so the router's internal path matches the
   * browser URL the consumer actually sees.
   *
   * Defaults to `'/admin'` — the historical behaviour. Pass `'/console'`,
   * `'/portal'`, ... to deploy under a different prefix, or `'/'` to deploy
   * at the domain root (no prefix).
   *
   * Affects every top-level route in the preset table — login/403 included,
   * so ALL routes share the single basePath prefix (since 0.2.71 this holds
   * for the default `'/admin'` too; login/403 previously stayed at the
   * domain root, which made auth redirects escape sub-path deployments):
   *   - `admin-root` (`/admin`) → `basePath`
   *   - `login` (`/login/:module(...)?`) → `${basePath}/login/:module(...)?`
   *     (no prefix when `basePath === '/'`)
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
   * base — that argument controls the browser URL prefix and is the
   * consumer's responsibility (see the recipes below). Framework-issued
   * redirects (auth guard, session-expired, login module switches) resolve
   * routes by NAME, so they follow whatever prefix combination you pick;
   * never hardcode `'/login'`-style paths in consumer code either — use
   * `{ name: 'login' }`.
   *
   * ## Deployment recipes
   *
   * **Sub-path deployment (IIS sub-application / nginx location), the
   * recommended shape**: let Vite's `base` be the single source of truth
   * for the deployment prefix and keep the route table prefix-free:
   * ```ts
   * // vite.config.ts        →  base: '/admin/'
   * // index.html + assets served from https://host/admin/
   * const router = createRouter({
   *   history: createWebHistory(import.meta.env.BASE_URL), // '/admin/'
   *   routes,
   * })
   * defineAdminApp({ client, basePath: '/' })
   * // URLs: https://host/admin/dashboard, https://host/admin/login
   * ```
   * The IIS sub-application needs the standard SPA fallback (URL Rewrite →
   * index.html) so deep links refresh correctly.
   *
   * **Lazy sub-path shape (also fully supported)**: keep the default
   * `basePath: '/admin'` as the in-router prefix and serve the SPA from a
   * sub-application with the SAME name, with `createWebHistory()` left at
   * `'/'`: every URL (login/403 included) now stays inside `/admin/*`, so
   * this shape is consistent too. Note the prefix is then written in two
   * places (IIS app name and basePath) that must match.
   *
   * **Renaming the IIS sub-application later** (e.g. `admin` → `console`):
   * under the recommended shape you change ONE line (Vite
   * `base: '/console/'`) and rebuild; application code is untouched
   * because framework redirects resolve by route name. Under the lazy
   * shape you must change BOTH `basePath: '/console'` and the Vite `base`,
   * then rebuild; a mismatch makes every URL miss the route table. A
   * rebuild is always required: history-mode SPAs bake the absolute
   * prefix into the bundle (deep-link refreshes serve index.html from
   * arbitrary paths, so relative asset URLs would resolve wrongly). If a
   * single build must serve arbitrary prefixes, use
   * `createWebHashHistory()` (hash URLs, prefix-free) or have the server
   * inject `<base href>`; usually not worth it for internal admins.
   *
   * **Domain-root deployment** (e.g. `admin.example.com`): the default
   * options just work; URLs live under `/admin/*` with login at
   * `/admin/login`.
   *
   * @example
   * ```ts
   * // Default — internal '/admin' prefix on every route
   * defineAdminApp({ client })
   *
   * // Custom base path
   * defineAdminApp({ client, basePath: '/console' })
   *
   * // Prefix-free route table (deployment prefix comes from the router
   * // history base / Vite base instead)
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
   * exact vue-router route `name` (case-sensitive — `"dashboard"` matches,
   * `"Dashboard"` does not). Walks `/admin` children + grandchildren and
   * writes `meta.order = routeOrders[route.name]` on every match.
   *
   * Useful when the default ordering ships the modules in a position you
   * don't want — e.g. lift `authorization` ahead of `identity`, or push
   * `dashboard` down a notch.
   *
   * Framework defaults (step 10, leaving room for consumer entries):
   *   - `dashboard` = 0
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
   * `1..99` slot between Dashboard and the first framework module, and
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
   *     dashboard: 5,        // shift Dashboard from 0
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
   * Configuration for the built-in Dashboard landing page. Declare your
   * widget deck inline; omit to use the bundled
   * `defaultWorkbenchWidgets()` set (HeaderBanner + KPIs + business
   * stats + activity timeline + tips).
   */
  dashboard?: AdminDashboardConfig

  /**
   * Configuration for the built-in Settings Center page — register custom
   * sections / hide built-in groups. See `AdminSettingsConfig`.
   */
  settings?: AdminSettingsConfig

  /**
   * Configuration for the built-in chat feature (TChatHost shell-level widget).
   * When omitted the chat launcher is enabled by default.
   */
  chat?: {
    /**
     * When false, disables the chat launcher in the header. Default: true.
     */
    enabled?: boolean
    /**
     * Override the SignalR chat hub URL. Default '/hubs/chat'. Set e.g.
     * '/api/hubs/chat' when the API is hosted under a sub-path.
     */
    hubUrl?: string
  }

  /**
   * Global admin theme configuration.
   *
   * `globalSync` (default true): the shell loads the backend global theme
   * snapshot (`GET /appearance/admin-theme`) after sign-in and applies it
   * to every user. Privileged users (`system.appearance.update` - super
   * admins by default) get the full theme drawer with a "save for all
   * users" action; everyone else gets a preset color-scheme picker whose
   * visibility the admin controls from the drawer (General → Global).
   * Set `globalSync: false` for the legacy local-only theme behaviour.
   *
   * `presets` replaces the built-in 12-color palette offered in the
   * drawer's Preset tab and the users' picker.
   */
  theme?: AdminThemeConfig

  /**
   * App-wide deep-link switch for URL-synced UI state (`?detail=` overlay
   * open-states, `?section=` active sections). `false` disables both channels
   * everywhere — built-in pages included — so no UI state ever enters the URL;
   * `{ detail?: boolean; section?: boolean }` disables one channel. Omitted /
   * `true` keeps the default behaviour (CRUD overlays deep-link out of the
   * box, everything else opt-in per page). A disabled channel overrides
   * per-page options — it is a kill switch, not a default.
   */
  deepLink?: AdminDeepLinkConfig

  /** Replace the placeholder `/403` forbidden component. */
  forbiddenComponent?: Component

  /**
   * Extra options forwarded to `createTnziUiAdmin()`. Mostly: pass a custom
   * `globalSearchShortcut`, or set `installPersistedstate: false` if you
   * already installed the pinia plugin yourself.
   */
  pluginOptions?: Omit<TnziUiAdminOptions, 'client' | 'pinia'>

  /**
   * Opt-in route guards. When `enabled`, `install()` registers the built-in
   * auth guard (redirects unauthenticated users to `loginPath`) and
   * permission guard (redirects to `forbiddenPath` when a route's
   * `meta.permission` isn't held) on the router passed to `install()`.
   *
   * Both guards read `useAdminAuthStore`, which the consumer populates after
   * login (via `setToken` / `setUserInfo`). Without this option **no guard is
   * installed** — the admin is open by default — so consumers that wire their
   * own `router.beforeEach` (e.g. an app with its own auth store) are not
   * double-guarded. Routes opt out per-route with `meta.requiresAuth = false`.
   *
   * @example
   * ```ts
   * defineAdminApp({ client, auth: { enabled: true } })
   * // custom targets (only when you replaced the built-in login/403 routes
   * // with differently-named ones — by default redirects resolve by name):
   * defineAdminApp({ client, auth: { enabled: true, loginPath: '/signin', forbiddenPath: '/no-access' } })
   * ```
   */
  auth?: {
    enabled?: boolean
    /**
     * The **permission** navigation guard — redirects to the `forbidden` route
     * when a route's `meta.permission` isn't held. Installed by DEFAULT (true),
     * INDEPENDENTLY of `enabled` (which only gates the *authentication* guard),
     * because it mirrors the always-on sidebar permission filter: a page hidden
     * from the menu should also be unreachable by URL / deep-link / a persisted
     * tab, instead of mounting into a broken "Failed to load data" (403) view.
     *
     * Fails OPEN while the permission list hasn't loaded (`userInfo === null`)
     * and for super users, so consumers that never wire `loadPermissions` are
     * unaffected; the backend `[ApiAuthorize]` stays the real enforcement. Set
     * `false` only if you enforce route permissions with your own guard.
     */
    permissionGuard?: boolean
    /**
     * EXPLICIT auth-redirect target path. Leave unset (recommended): the
     * guard and the session-expired handler then redirect by route NAME
     * (`{ name: 'login' }`), which resolves to the login route wherever the
     * table put it (any `basePath`, any router history base) and is
     * therefore deployment-agnostic. Only set this when you replaced the
     * built-in login route with a differently-named one.
     */
    loginPath?: string
    /** Explicit 403 target path; defaults to the named `forbidden` route. */
    forbiddenPath?: string
    /**
     * Built-in session-expired handling (default **true** — independent of
     * `enabled`). When the HttpClient reports an unrecoverable 401 (no
     * refresh token, or the refresh itself failed), the framework clears
     * `useAdminAuthStore` and redirects to the login route (by name, or
     * `loginPath` when set) with a `?next=<current fullPath>` deep-link so
     * the user lands back where they were after re-authenticating.
     * Requires `install(app, pinia, router)` to receive the router. Set
     * `false` when the app wires its own session-expired handler.
     */
    sessionExpiredRedirect?: boolean
  }
}

/**
 * The signed-in user passed to `loadPermissions`. Only `id` is required — it is
 * used to fetch the permission code list from the backend. The rest populate the
 * admin auth store's `userInfo` (drives the header avatar, breadcrumb, etc.).
 */
export interface AdminCurrentUser {
  /** Backend user id — used to fetch `GET /admin/function-authorization/user/{id}/permissions`. */
  id: string
  username?: string
  displayName?: string
  email?: string
  avatar?: string
  /** Local-upload avatar file id (UserDetail.AvatarId). When omitted,
   *  `loadPermissions` auto-fetches the current profile so the header avatar
   *  reflects an uploaded picture, not just an external `avatar` link. */
  avatarId?: string | null
  roles?: string[]
  tenantId?: string
  /**
   * Pre-fetched permission codes. When provided, `loadPermissions` SKIPS the
   * backend round-trip — pass these when the caller already has them (e.g. the
   * core `AuthStateManager` fetched them via its `permissionsFetchFn`). Omit to
   * have `loadPermissions` fetch from `/admin/function-authorization/...`.
   */
  permissions?: string[]
  /** Optional access token mirrored into the admin auth store (enables the built-in auth guard). */
  token?: string
  refreshToken?: string
  /**
   * Force super-user (sees every menu). Usually unnecessary — the backend
   * already returns the full enabled-function catalogue for super-admins, so a
   * super-admin naturally receives every code and sees every menu.
   */
  superUser?: boolean
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

  /**
   * Load the current user's permission codes and populate the admin auth store
   * — call this right after a successful login (and on app boot when restoring a
   * session). THIS is what wires permission-filtered menus + route guards to
   * real data: until it runs, the sidebar fails OPEN (shows everything).
   *
   * Fetches `GET /admin/function-authorization/user/{id}/permissions`, then
   * `setUserInfo({ …, permissions })`. A legitimate empty list (user has no
   * grants) sets `userInfo` and the sidebar shows only public entries; a network
   * failure THROWS without touching `userInfo`, so the menu stays fail-open and
   * the caller can retry. Must run after `install()` (needs an active pinia).
   *
   * @returns the loaded permission codes.
   *
   * `user` is optional: when omitted (or `{ id: '' }`), the framework self-fetches
   * the current profile (`GET /users/profile`) to resolve the id / display name /
   * avatar — so a consumer can simply call `loadPermissions()` after login or
   * session restore without threading the user through.
   */
  loadPermissions(user?: AdminCurrentUser): Promise<string[]>

  /**
   * Refresh the backend module-availability signal (`GET /admin/shell/modules`)
   * and feed it to the route store, so the sidebar + guards reflect which
   * framework modules the host loaded. Called automatically by `install()`
   * (once a token is present) and after a wrapped login, so consumers rarely
   * need it — expose it for a manual refresh (e.g. after a runtime module
   * enable/disable). Fail-open on failure (keeps the prior signal). Safe to
   * ignore the promise. No-op when `moduleGating` is disabled.
   */
  loadAvailableModules(): Promise<void>

  /**
   * Load backend `Sys_Menu` overrides for the 'merge' menu source and feed them
   * to the route store (the sidebar then reflects operator retitle / reorder /
   * hide without a redeploy). No-op unless `menu.source === 'merge'`. Call after
   * `loadPermissions` (needs the user id + an active pinia). Safe to ignore the
   * promise — failures leave the menu as the plain route-derived tree.
   */
  loadMenus(userId: string): Promise<void>

  /**
   * Mirror the front-end route-derived menu into editable `Sys_Menu` rows
   * (`POST /admin/menus/seed`, upsert by menuKey — inserts missing keys, skips
   * existing ones so operator edits survive). Gives operators an editable
   * starting point when first enabling the `'merge'` source. Returns the
   * insert/skip counts (null if nothing to seed or the request fails). Call
   * after `install()` (needs an active pinia).
   */
  seedMenus(): Promise<MenuSeedResultDto | null>
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
 * This applies to the DEFAULT `/admin` basePath too (since 0.2.71): every
 * route in the table, login and 403 included, lives under the single
 * basePath prefix. Before that, login/403 stayed at the domain root under
 * the default: 99% of URLs happened to work when the SPA was mounted as
 * an IIS sub-application (the internal `/admin` prefix coincided with the
 * deployment prefix), but any login/403 redirect escaped the
 * sub-application (e.g. `https://host/login` instead of
 * `https://host/admin/login`) and 404'd.
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

  // Module-availability gating is ON by default; `false` or `{ enabled: false }`
  // opts out (fall back to permission-only filtering, the historic behaviour).
  const moduleGatingEnabled =
    options.moduleGating === false
      ? false
      : typeof options.moduleGating === 'object'
        ? options.moduleGating.enabled !== false
        : true

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

  /**
   * Wrap the consumer's auth callbacks so the framework populates the admin auth
   * store automatically after a successful login — the consumer no longer has to
   * call `loadPermissions` itself. The consumer callback runs first (it sets the
   * token on the HttpClient via its own auth manager); then we self-fetch the
   * profile + permission codes so the header name/avatar and the chat window's
   * own `myId` are correct the moment the user lands on the shell. This is what
   * makes the login flow "框架自洽" — see `loadPermissions`.
   */
  function wrapLoginCallbacks(login?: AdminLoginConfig): AdminLoginConfig | undefined {
    if (!login) return login

    // Clear the admin auth store on deliberate sign-out. The consumer's
    // `user.onLogout` typically clears only the core AuthStateManager (a separate
    // store) and redirects to the login page; without this, `isSuperUser` /
    // `userInfo` persisted by useAdminAuthStore survive into the next sign-in,
    // letting a role inherit a prior super-admin's "see everything" bypass. Only
    // wrapped when the consumer actually supplies `onLogout`, so whether a logout
    // affordance exists is unchanged.
    //
    // ORDER MATTERS: run the consumer's logout FIRST (it does the backend
    // sign-out — a slow remote round-trip — then redirects to the login route),
    // and clear the admin store in `finally` AFTER. Clearing it FIRST used to
    // null `userInfo` while the shell was still mounted, so the sidebar
    // re-rendered every menu (fail-open) for the 1-2s the backend logout took
    // before the redirect. Deferring the clear keeps the user's real, correctly
    // filtered menu on screen until the redirect unmounts the shell, then wipes
    // the store — no flash, and the next sign-in still starts clean. `finally`
    // guarantees the clear even if the consumer's logout throws.
    let wrapped: AdminLoginConfig = login
    const userCfg = login.user
    const consumerOnLogout = userCfg?.onLogout
    if (userCfg && consumerOnLogout) {
      wrapped = {
        ...login,
        user: {
          ...userCfg,
          onLogout: async () => {
            try {
              await consumerOnLogout()
            } finally {
              useAdminAuthStore().logout()
            }
          },
        },
      }
    }

    const cbs = wrapped.callbacks
    if (!cbs) return wrapped
    const after = async () => {
      await loadPermissions().catch(() => undefined)
      await loadAvailableModules().catch(() => undefined)
    }
    const pwd = cbs.pwdLogin
    const code = cbs.codeLogin
    const callbacks: NonNullable<AdminLoginConfig['callbacks']> = {
      ...cbs,
      pwdLogin: pwd
        ? async (payload, helpers) => {
            await pwd(payload, helpers)
            await after()
          }
        : undefined,
      codeLogin: code
        ? async (payload, helpers) => {
            await code(payload, helpers)
            await after()
          }
        : undefined,
    }
    return { ...wrapped, callbacks }
  }

  function install(app: App, pinia?: Pinia, router?: Router): TnziUiAdminInstance {
    const instance = createTnziUiAdmin(app, {
      ...(options.pluginOptions ?? {}),
      client: options.client,
      pinia,
      login: wrapLoginCallbacks(options.login),
      dashboard: options.dashboard,
      settings: options.settings,
      chat: options.chat,
      theme: options.theme,
    })

    // Apply the GLOBAL admin theme snapshot app-wide, at bootstrap — BEFORE
    // and independent of login. `GET /appearance/admin-theme` is anonymous
    // (deployment-level public appearance), so the login page and the top-level
    // exception pages (403/404/500) — which render OUTSIDE the authenticated
    // shell — pick up the super-admin-configured theme too, instead of snapping
    // back to the built-in palette on every refresh. AdminShellRoot keeps its
    // own controller for the privileged edit / save / dirty flow; this early
    // apply is read-only and idempotent (a no-op when nothing differs). The
    // theme context was just installed by createTnziUiAdmin above (either the
    // consumer's own or the fallback), so it always resolves. Fire-and-forget:
    // never blocks app start; degrades silently on old backends / hosts without
    // Tnzi.System.
    const bootThemeCtx = app.runWithContext(() =>
      inject<ThemeContext | null>(THEME_CONTEXT_KEY, null),
    )
    if (bootThemeCtx) {
      void useGlobalTheme({
        client: options.client,
        themeContext: bootThemeCtx,
        enabled: options.theme?.globalSync !== false,
      }).load()
    }

    // App-wide deep-link switch — read by useDetail (and thus useCrudPage)
    // via tryInjectDeepLinkConfig(). Provided unconditionally so per-page
    // engines never need to guard against a missing key.
    app.provide(ADMIN_DEEP_LINK_KEY, resolveDeepLinkConfig(options.deepLink))

    // Attach the soybean-style route progress bar if a router is provided.
    // Idempotent — safe if the consumer already called useRouteProgress.
    if (router) {
      useRouteProgress(router)
    }

    // All framework-issued auth redirects resolve the login/403 routes by
    // NAME, never by a hardcoded path: names are stable across any basePath
    // and any router history base, so the redirect always lands wherever
    // the route table actually put the route (deployment-agnostic). An
    // explicit `auth.loginPath` / `auth.forbiddenPath` still wins when the
    // consumer replaced the built-in routes with differently-named ones.
    const explicitLoginPath = options.auth?.loginPath
    const explicitForbiddenPath = options.auth?.forbiddenPath

    // Route guards. Two independent layers:
    //
    //  • AUTHENTICATION guard (opt-in via `auth.enabled`): redirects
    //    unauthenticated users to login. Left opt-in so apps that wire their own
    //    `router.beforeEach` for auth (e.g. Acme, which restores the session
    //    itself) aren't double-guarded.
    //
    //  • PERMISSION guard (on by DEFAULT — opt out with `auth.permissionGuard:
    //    false`): redirects to `forbidden` when a route's `meta.permission`
    //    isn't held. This is the navigation-layer twin of the always-on sidebar
    //    filter: without it a page hidden from the menu was still reachable by
    //    URL / deep-link / a persisted tab and mounted into a broken 403 "Failed
    //    to load data" view. Safe by construction — the guard fails OPEN while
    //    `userInfo === null` and for super users, so consumers that never wire
    //    permissions keep the historical open behaviour, and the backend
    //    `[ApiAuthorize]` remains the real enforcement.
    if (router) {
      if (options.auth?.enabled) {
        router.beforeEach(createAuthGuard({ loginPath: explicitLoginPath }))
      }
      // Module-availability guard BEFORE the permission guard: a route into an
      // unloaded framework module should bounce to /403 (graceful) without the
      // permission guard first recording it as a tab. Orthogonal to auth, holds
      // for super users; a no-op (empty denied set) until the signal loads, and
      // skipped entirely when module gating is disabled.
      if (moduleGatingEnabled) {
        router.beforeEach(createModuleGuard({ forbiddenPath: explicitForbiddenPath }))
      }
      if (options.auth?.permissionGuard !== false) {
        router.beforeEach(createPermissionGuard({ forbiddenPath: explicitForbiddenPath }))
      }
    }

    // Built-in session-expired handling (default ON, independent of the
    // opt-in guards). The route guards above only fire on NAVIGATION; when
    // the user idles on a page until both tokens die, every widget request
    // just 401s in place and nothing redirects. Subscribing to the client's
    // unauthorized signal (fired once per auth cycle, only after refresh is
    // impossible or failed) closes that gap for every consumer without the
    // hand-rolled `watch(isLoggedIn)` boilerplate each app used to need.
    // The consumer's own `onUnauthorized` config keeps running first (it
    // typically clears the core AuthStateManager); this listener clears the
    // admin store (so persisted `isLogin` doesn't resurrect the session on
    // reload) and redirects with a `next` deep-link back to the current page.
    if (router && options.auth?.sessionExpiredRedirect !== false) {
      // Optional call: tolerates HttpClient builds predating addUnauthorizedListener.
      options.client.addUnauthorizedListener?.(() => {
        useAdminAuthStore().logout()
        const current = router.currentRoute.value
        const onLogin =
          current.name === 'login' ||
          (explicitLoginPath !== undefined &&
            (current.path === explicitLoginPath ||
              current.path.startsWith(`${explicitLoginPath}/`)))
        if (onLogin) return
        const query = { next: current.fullPath }
        void router.replace(
          explicitLoginPath !== undefined
            ? { path: explicitLoginPath, query }
            : { name: 'login', query },
        )
      })
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

    // Session-restore self-refresh: when the auth store rehydrated a signed-in
    // session from persistence, consumers historically had to re-call
    // `loadPermissions` themselves - and forgetting it left the persisted
    // permission list AND `isSuperUser` frozen at their last-login values (a
    // stale super flag shows menus the backend will 403; a stale permission
    // list hides freshly granted ones). Refreshing in the background on every
    // install keeps the store aligned with the backend with zero consumer
    // wiring. Fire-and-forget: never blocks app start, never throws.
    //
    // ★The probe must WAIT for the HttpClient to carry an access token:
    // consumers typically restore the core session asynchronously (e.g. in a
    // router guard), which lands AFTER install() - firing immediately sent an
    // unauthenticated GET /users/profile on every reload of a signed-in
    // session (guaranteed 401 console noise; the write-side was already
    // discarded by loadPermissions' failed-resolution guard). Poll the cheap
    // in-memory token accessor and give up quietly when no session ever
    // materialises (the auth guard then redirects to login anyway).
    const authStore = useAdminAuthStore()
    if (authStore.isLogin && authStore.userInfo !== null) {
      const refreshWhenClientReady = async (): Promise<void> => {
        if (!(await waitForClientToken(options.client))) return
        await loadPermissions()
      }
      void refreshWhenClientReady().catch(() => undefined)
    }

    // Module-availability signal — fetch once a token is present, regardless of
    // fresh login vs session restore. Independent of the session-restore probe
    // above (that one only runs for an already-signed-in reload and only
    // refreshes permissions). Fail-open on timeout / failure. Skipped when
    // module gating is disabled.
    //
    // While the probe is in flight `moduleSignalPending` is raised so
    // SIDE-EFFECTFUL module surfaces (built-in chat host, dashboard data
    // widgets — anything whose mount fires requests / opens sockets) defer
    // mounting instead of racing a signal that may be about to rule their
    // module out. Settles on success, failure, AND the no-token timeout, so
    // deferred surfaces are never wedged (fail-open once settled).
    if (moduleGatingEnabled) {
      routeStore.setModuleSignalPending(true)
      const loadModulesWhenReady = async (): Promise<void> => {
        try {
          if (await waitForClientToken(options.client)) {
            await loadAvailableModules()
          }
        } finally {
          routeStore.setModuleSignalPending(false)
        }
      }
      void loadModulesWhenReady().catch(() => undefined)
    }

    return instance
  }

  async function loadPermissions(user: AdminCurrentUser = { id: '' }): Promise<string[]> {
    const authStore = useAdminAuthStore()

    // Framework self-wiring: fetch the signed-in user's profile so the admin
    // store is populated (header name + avatar, and the chat window's own
    // `myId`) even when the consumer's login flow doesn't thread the user
    // through `loadPermissions`. `GET /users/profile` (UserDto) is the source of
    // truth for id / display name / avatar — and `avatarId` only lives there
    // (Identity UserDetail), never on the login/permission response. Best-effort:
    // a failure falls back to whatever the caller supplied.
    let profile: Awaited<ReturnType<ReturnType<typeof createIdentityBridge>['me']['getProfile']>> | null = null
    try {
      const fetched = await createIdentityBridge({ client: options.client }).me.getProfile()
      // Failure envelopes RESOLVE here instead of throwing, in two shapes:
      // `data: undefined` unwraps to undefined, and an envelope WITHOUT a
      // data field unwraps to the envelope object itself (truthy!). Only a
      // payload carrying a real user id counts as a resolved profile -
      // anything else must read as "identity NOT resolved" so the
      // failed-resolution guard below can hold.
      const fetchedId = (fetched as { id?: unknown } | null | undefined)?.id
      profile = typeof fetchedId === 'string' && fetchedId !== '' ? fetched : null
    } catch {
      profile = null
    }

    const userId = user.id || profile?.id || ''
    const username = user.username || profile?.userName || ''
    const fullName = [profile?.firstName, profile?.lastName].filter(Boolean).join(' ').trim()
    // Display-name precedence (mirrors backend ChatContactService.ResolveDisplayName):
    // nickname → real name (FirstName/LastName) → username.
    const displayName =
      user.displayName || profile?.nickname || (fullName || undefined) || username || undefined
    // Short, first-name-only label for personal greetings / the header status
    // bar / the chat "me" name — the surname is intentionally dropped so these
    // read "Hi, John" not "Hi, John Doe". Keeps nickname first (that IS how the
    // user wants to be addressed), then the given name, then the username.
    const shortName = profile?.nickname || profile?.firstName || username || undefined
    const avatarId: string | null = user.avatarId ?? profile?.avatarId ?? null
    const avatar: string | undefined = user.avatar ?? profile?.avatar ?? undefined
    const roles = user.roles ?? profile?.roles ?? []

    // NB: the admin store token (→ `isLogin`) is set LATER, atomically with
    // `setUserInfo` below — NOT here. Setting it early opened a window where
    // `isLogin === true` but `userInfo === null` during the access-profile
    // fetch, which the sidebar's `isLogin`-gated fail-open reads as "logged in,
    // permissions loading" → it flashes EVERY menu. It also matters nothing for
    // request auth: the permission/profile fetches below go through
    // `options.client` (core HttpClient), which already carries the token from
    // the consumer's login. Deferring keeps the login state atomic: either both
    // token + userInfo land, or (on the failed-resolution guard) neither.

    // Resolve permissions (BEST-EFFORT). The identity `setUserInfo` below MUST run
    // even when this fails: a regular (non-admin) user gets 403 from the admin
    // permission endpoint, yet still needs their name / avatar / id — the chat
    // window's own `myId` and the header name both read `userInfo`. A failure just
    // leaves the permission list empty (the sidebar then shows only public
    // entries); it must NEVER throw and block the identity.
    //
    // (This was the "bob still shows Admin + no green bubbles" bug: bob's 403 on
    //  /admin/function-authorization/user/{id}/permissions threw BEFORE setUserInfo
    //  ran, so `userInfo` stayed null → header fell back to the static 'Admin' and
    //  `myId` was undefined → own messages never matched as mine.)
    let permissions = user.permissions ?? []
    // Backend-authoritative super-admin flag. `null` = unresolved (endpoint
    // unavailable / caller pre-supplied permissions) → fall back to the legacy
    // client-side inference from `superAdminRoles` below.
    let backendIsSuper: boolean | null = null
    if (user.permissions === undefined) {
      try {
        const api = useAdminFunctionAuthorizationApi(options.client)
        // Preferred: single self-service call, no userId needed, and the
        // super-admin flag comes from the backend instead of a front-end
        // mirror of `Authorization:SuperAdminRoles` (which could drift).
        const profileRes = await api.getAccessProfile()
        if (profileRes.success && profileRes.data) {
          permissions = profileRes.data.permissions ?? []
          backendIsSuper = profileRes.data.isSuperAdmin === true
        } else if (userId) {
          // Older backend without the access-profile endpoint.
          const legacy = await api.getUserPermissionNames(userId)
          if (legacy.success) permissions = legacy.data ?? []
        }
      } catch {
        // best-effort — keep the identity, leave permissions empty
      }
    }

    // FAILED-RESOLUTION GUARD: when the caller supplied nothing and both the
    // profile and the permission fetches came back empty-handed (expired
    // token during the install-time background refresh, transient network
    // failure), this call learned NOTHING about the session. Writing
    // `{ id: '', permissions: [] }` would POISON the store: `userInfo` flips
    // non-null with zero permissions, so the permission guard stops failing
    // open and the very next navigation bounces to /403 - even while a real
    // login is completing in parallel. It would also clobber a previously
    // valid persisted identity on a flaky refresh. Leave the store untouched.
    if (!userId && !username && profile === null && user.permissions === undefined && backendIsSuper === null) {
      return []
    }

    // Flip the token (→ `isLogin`) and the identity together so the sidebar
    // never sees `isLogin === true` with a still-null / still-previous
    // `userInfo` (see the note above where the early setToken used to live).
    if (user.token) authStore.setToken(user.token, user.refreshToken)

    authStore.setUserInfo({
      id: userId,
      username,
      displayName,
      shortName,
      email: user.email ?? profile?.email ?? undefined,
      avatar,
      avatarId,
      roles,
      permissions,
      tenantId: user.tenantId,
    })
    const superRoleSet = new Set((options.superAdminRoles ?? []).map((r) => r.toLowerCase()))
    const isSuper =
      backendIsSuper ??
      (user.superUser === true || roles.some((r) => superRoleSet.has(r.toLowerCase())))
    // Write UNCONDITIONALLY (true OR false). A one-way `if (isSuper) setSuperUser(true)`
    // let a previous super-admin session's `true` — persisted by the auth store — leak
    // into the NEXT sign-in of a non-super user (e.g. a Business admin): `isSuperUser`
    // stayed true, so `useAdminRouteStore.menus` kept bypassing the permission filter
    // and showed every menu. Overwriting with the current user's real tier on every
    // permission load closes that cross-session leak.
    authStore.setSuperUser(isSuper)

    // Drop any persisted tabs the freshly-resolved user can't open — the
    // cross-session tab leak (a prior super-admin's Diagnostics / MCP / Sandbox
    // tabs surviving into a Business admin sign-in and 403'ing on click), the
    // sibling of the `isSuperUser` leak fixed by writing it unconditionally
    // above. `deniedRouteNames` is empty for super users / before this load, so
    // this is a no-op except for a real privilege downgrade. Best-effort: never
    // let tab housekeeping break the identity load.
    try {
      const routeStore = useAdminRouteStore()
      useAdminTabStore().pruneTabs(routeStore.deniedRouteNames)
    } catch {
      // ignore — tabs are cosmetic; the navigation guard still blocks access
    }
    return permissions
  }

  async function loadAvailableModules(): Promise<void> {
    if (!moduleGatingEnabled) return
    const names = await fetchAdminShellModules(options.client)
    // null = endpoint unavailable / failed → fail-open: keep the prior signal
    // (null on the first run = gating off = show everything). A real Set (even
    // empty) turns gating on.
    if (names === null) return
    useAdminRouteStore().setAvailableModules(names)
  }

  async function loadMenus(userId: string): Promise<void> {
    if (options.menu?.source !== 'merge') return
    const routeStore = useAdminRouteStore()
    const res = await useAdminMenuApi(options.client).getUserTree(userId)
    if (res.success && Array.isArray(res.data)) {
      routeStore.setBackendMenus(res.data)
    }
  }

  async function seedMenus(): Promise<MenuSeedResultDto | null> {
    const routeStore = useAdminRouteStore()
    const seed = exportRouteMenuSeed(routeStore.menus)
    if (seed.length === 0) return null
    const res = await useAdminMenuApi(options.client).seed(seed)
    return res.success ? (res.data ?? null) : null
  }

  return { routes, install, loadPermissions, loadAvailableModules, loadMenus, seedMenus }
}
