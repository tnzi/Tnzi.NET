import type { NavigationGuard, RouteLocationNormalized } from 'vue-router'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { useAdminTabStore } from '../stores/useAdminTabStore'
import { usePermissionGuard } from '../headless/usePermissionGuard'

export interface AuthGuardOptions {
  loginPath?: string
}

export interface PermissionGuardOptions {
  forbiddenPath?: string
}

/**
 * Auth guard — redirects unauthenticated users to the login page.
 * Routes may opt out by setting `meta.requiresAuth = false`.
 */
export function createAuthGuard(options: AuthGuardOptions = {}): NavigationGuard {
  const loginPath = options.loginPath ?? '/login'
  return (to, _from, next) => {
    if (to.meta?.requiresAuth === false) {
      return next()
    }
    const auth = useAdminAuthStore()
    if (!auth.isLogin) {
      return next(loginPath)
    }
    next()
  }
}

/**
 * Permission guard — checks `meta.permission` via `usePermissionGuard`.
 * On successful navigation, records the route as a tab.
 */
export function createPermissionGuard(
  options: PermissionGuardOptions = {},
): NavigationGuard {
  const forbiddenPath = options.forbiddenPath ?? '/403'
  return (to, _from, next) => {
    const required = (to.meta?.permission ?? '') as string
    if (required) {
      // Fail-open while the permission list hasn't loaded yet (no user info):
      // mirrors the menu layer and avoids bouncing a freshly-logged-in user to
      // 403 before their permissions arrive (or in apps that never wire them).
      // Real enforcement is the backend `[ApiAuthorize]`.
      const auth = useAdminAuthStore()
      if (auth.userInfo !== null) {
        const { can } = usePermissionGuard()
        if (!can(required)) {
          return next(forbiddenPath)
        }
      }
    }
    addTabFromRoute(to)
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
