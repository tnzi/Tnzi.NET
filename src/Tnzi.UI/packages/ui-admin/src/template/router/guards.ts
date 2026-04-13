import type { NavigationGuard } from 'vue-router'

export function createAuthGuard(options: {
  isAuthenticated: () => boolean
  loginPath?: string
}): NavigationGuard {
  return (to, _from, next) => {
    if (to.meta.requiresAuth && !options.isAuthenticated()) {
      next(options.loginPath ?? '/login')
    } else {
      next()
    }
  }
}

export function createPermissionGuard(options: {
  hasPermission: (permission: string) => boolean
  forbiddenPath?: string
}): NavigationGuard {
  return (to, _from, next) => {
    const permissions = (to.meta.permissions as string[]) ?? []
    if (permissions.length > 0 && !permissions.every((p) => options.hasPermission(p))) {
      next(options.forbiddenPath ?? '/403')
    } else {
      next()
    }
  }
}
