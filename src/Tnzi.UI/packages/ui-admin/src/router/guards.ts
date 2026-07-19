import type { NavigationGuard, RouteLocationNormalized } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { useAdminTabStore } from '../stores/useAdminTabStore'
import { usePermissionGuard } from '../headless/usePermissionGuard'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'

export interface AuthGuardOptions {
  /**
   * Explicit redirect target for unauthenticated users. When omitted the
   * guard redirects by route NAME (`{ name: 'login' }`) so the redirect
   * lands on the login route wherever the route table put it: default
   * `/login`, `${basePath}/login` under a custom basePath, or a consumer
   * replacement registered under the same name. Never hardcode a path
   * here just to follow a deployment prefix; names are prefix-agnostic.
   */
  loginPath?: string
  /**
   * Optional async session resolver injected by `defineAdminApp.install()`.
   * Invoked when the guard finds no active admin session: it runs the
   * consumer's `auth.restore` hook (rehydrate the core session onto the
   * HttpClient) then `loadPermissions` (which sets the admin store's `isLogin`
   * atomically), and returns whether the user ended up authenticated. This is
   * what lets a consumer delete the hand-rolled `router.beforeEach` that used
   * to restore the session + load permissions on every navigation. Omitted →
   * the guard falls back to a plain synchronous `isLogin` check (no restore).
   */
  resolveSession?: () => Promise<boolean>
}

export interface PermissionGuardOptions {
  /** Explicit redirect target; defaults to the named `forbidden` route. */
  forbiddenPath?: string
}

/**
 * Auth guard — redirects unauthenticated users to the login page.
 * Routes may opt out by setting `meta.requiresAuth = false`.
 */
export function createAuthGuard(options: AuthGuardOptions = {}): NavigationGuard {
  return async (to, _from, next) => {
    if (to.meta?.requiresAuth === false) {
      return next()
    }
    // When a session resolver is injected it is the SINGLE source of truth and
    // runs on every guarded navigation — it checks the live client token first
    // (consumer `auth.restore` rehydrates the core session, then loadPermissions
    // sets `isLogin`). This is deliberately NOT short-circuited by the store's
    // `isLogin`: that flag is persisted, so after a cold reload it can read
    // `true` while the core session isn't restored yet — trusting it would wave
    // the user onto a page whose every request then 401s. The resolver keys off
    // the token instead, so a stale persisted flag can't leak through. It stays
    // cheap when already signed in (token present → no backend call).
    if (options.resolveSession) {
      return (await options.resolveSession())
        ? next()
        : next(options.loginPath ?? { name: 'login' })
    }
    // No resolver (consumer manages restore itself): plain store check.
    const auth = useAdminAuthStore()
    if (auth.isLogin) {
      return next()
    }
    next(options.loginPath ?? { name: 'login' })
  }
}

/**
 * Permission guard - checks `meta.permission` (single) and `meta.permissions`
 * (plural, ANY-of) via `usePermissionGuard`, matching the sidebar filter's
 * reading of both fields - a route only declaring the plural form used to be
 * hidden from the menu yet still pass this guard. On successful navigation,
 * records the route as a tab.
 */
export function createPermissionGuard(
  options: PermissionGuardOptions = {},
): NavigationGuard {
  return (to, _from, next) => {
    const required = (to.meta?.permission ?? '') as string
    const requiredAny = (to.meta?.permissions ?? []) as string[]
    // The Settings Center route sets `anySettingsPermission` instead of a single
    // code: it's reachable with ANY per-group settings view code (see
    // usePermissionGuard.canAnySettings). Backend filters the groups per-code.
    const requiresAnySettings = to.meta?.anySettingsPermission === true
    // Role gate (consumer routes only): a route may declare `meta.roles` instead
    // of / alongside permission codes. Same fail-open + super-user bypass as the
    // permission gate; mirrors the sidebar role filter so a hidden role-gated
    // page can't be reached by direct URL / deep link.
    const requiredRoles = (to.meta?.roles ?? []) as string[]
    if (required || requiredAny.length > 0 || requiresAnySettings || requiredRoles.length > 0) {
      // Fail-open while the permission list hasn't loaded yet (no user info):
      // mirrors the menu layer and avoids bouncing a freshly-logged-in user to
      // 403 before their permissions arrive (or in apps that never wire them).
      // Real enforcement is the backend `[ApiAuthorize]`.
      const auth = useAdminAuthStore()
      if (auth.userInfo !== null) {
        const { can, canAny, canAnySettings } = usePermissionGuard()
        const permAllowed =
          (required ? can(required) : true) &&
          (requiredAny.length > 0 ? canAny(requiredAny) : true) &&
          (requiresAnySettings ? canAnySettings() : true)
        const roleAllowed =
          requiredRoles.length === 0 || auth.isSuperUser || auth.hasAnyRole(requiredRoles)
        if (!permAllowed || !roleAllowed) {
          return next(options.forbiddenPath ?? { name: 'forbidden' })
        }
      }
    }
    addTabFromRoute(to)
    next()
  }
}

export interface ModuleGuardOptions {
  /** Explicit redirect target; defaults to the named `forbidden` route. */
  forbiddenPath?: string
}

/**
 * Module-availability guard — bounces navigation into a framework module the
 * backend host didn't load to the `forbidden` route, so a deep link / bookmark
 * / persisted tab / `router.push({ name })` into an unloaded module (Finance /
 * Payment / AI …) degrades gracefully to /403 instead of mounting a page whose
 * every API call 404s.
 *
 * Reads `useAdminRouteStore.unavailableRouteNames`, which is empty when the
 * loaded-module signal is unavailable (fail-open) and ORTHOGONAL to permissions
 * — so this holds for super users too, unlike the permission guard. A route for
 * a loaded module is never in the set, so deep links / direct URLs / name
 * references to loaded modules keep working untouched.
 */
export function createModuleGuard(
  options: ModuleGuardOptions = {},
): NavigationGuard {
  return (to, _from, next) => {
    const routeStore = useAdminRouteStore()
    const name = typeof to.name === 'string' ? to.name : ''
    if (name && routeStore.unavailableRouteNames.has(name)) {
      return next(options.forbiddenPath ?? { name: 'forbidden' })
    }
    next()
  }
}

function addTabFromRoute(to: RouteLocationNormalized): void {
  const tabStore = useAdminTabStore()
  const name = String(to.name ?? to.path)
  tabStore.addTab({
    name,
    path: to.path,
    fullPath: to.fullPath,
    query: to.query as Record<string, unknown>,
    params: to.params as Record<string, unknown>,
    meta: {
      title: (to.meta?.title as string | undefined) ?? name,
      keepAlive: to.meta?.keepAlive !== false,
      fixedIndexInTab: to.meta?.fixedIndexInTab as number | undefined,
      multiTab: to.meta?.multiTab as boolean | undefined,
      icon: to.meta?.icon as string | undefined,
    },
  })
}
