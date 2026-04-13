export interface UsePermissionGuardOptions {
  hasPermission: (permission: string) => boolean
  hasRole: (role: string) => boolean
  isAuthenticated: () => boolean
  onUnauthorized?: () => void
  onForbidden?: () => void
}

export interface RouteMeta {
  requiresAuth?: boolean
  permissions?: string[]
  roles?: string[]
}

export interface UsePermissionGuardReturn {
  canAccess: (meta: RouteMeta) => boolean
  guardRoute: (meta: RouteMeta) => boolean
}

export function usePermissionGuard(options: UsePermissionGuardOptions): UsePermissionGuardReturn {
  function canAccess(meta: RouteMeta): boolean {
    if (meta.requiresAuth && !options.isAuthenticated()) {
      return false
    }

    if (meta.permissions && meta.permissions.length > 0) {
      const hasAll = meta.permissions.every((p) => options.hasPermission(p))
      if (!hasAll) return false
    }

    if (meta.roles && meta.roles.length > 0) {
      const hasAny = meta.roles.some((r) => options.hasRole(r))
      if (!hasAny) return false
    }

    return true
  }

  function guardRoute(meta: RouteMeta): boolean {
    if (meta.requiresAuth && !options.isAuthenticated()) {
      options.onUnauthorized?.()
      return false
    }

    if (meta.permissions && meta.permissions.length > 0) {
      const hasAll = meta.permissions.every((p) => options.hasPermission(p))
      if (!hasAll) {
        options.onForbidden?.()
        return false
      }
    }

    if (meta.roles && meta.roles.length > 0) {
      const hasAny = meta.roles.some((r) => options.hasRole(r))
      if (!hasAny) {
        options.onForbidden?.()
        return false
      }
    }

    return true
  }

  return { canAccess, guardRoute }
}
