import { createRouter, createWebHistory, type Router, type RouteRecordRaw } from 'vue-router'
import { defaultAdminRoutes } from './routes'
import { createAuthGuard, createPermissionGuard } from './guards'

export interface AdminRouterOptions {
  routes?: RouteRecordRaw[]
  isAuthenticated: () => boolean
  hasPermission: (permission: string) => boolean
  loginPath?: string
  forbiddenPath?: string
}

export function createAdminRouter(options: AdminRouterOptions): Router {
  const router = createRouter({
    history: createWebHistory(),
    routes: [...defaultAdminRoutes, ...(options.routes ?? [])],
  })

  router.beforeEach(
    createAuthGuard({
      isAuthenticated: options.isAuthenticated,
      loginPath: options.loginPath,
    }),
  )

  router.beforeEach(
    createPermissionGuard({
      hasPermission: options.hasPermission,
      forbiddenPath: options.forbiddenPath,
    }),
  )

  return router
}

export { defaultAdminRoutes } from './routes'
export { createAuthGuard, createPermissionGuard } from './guards'
