import { createApp, ref, type App, type Plugin } from 'vue'
import { createPinia } from 'pinia'
import type { RouteRecordRaw } from 'vue-router'
import { createAdminRouter } from './router'
import { defaultAdminMenus } from './menu/default-menus'
import type { AdminMenuItem } from './menu/types'
import AdminApp from './App.vue'

export interface AdminAppOptions {
  apiBaseUrl: string
  modules?: string[]
  routes?: RouteRecordRaw[]
  menus?: AdminMenuItem[]
  logo?: string
  title?: string
  isAuthenticated: () => boolean
  hasPermission: (permission: string) => boolean
  loginPath?: string
  forbiddenPath?: string
  onReady?: (app: App) => void
  plugins?: Plugin[]
}

export function createAdminApp(options: AdminAppOptions): App {
  const app = createApp(AdminApp)

  const pinia = createPinia()
  app.use(pinia)

  const router = createAdminRouter({
    isAuthenticated: options.isAuthenticated,
    hasPermission: options.hasPermission,
    routes: options.routes,
    loginPath: options.loginPath,
    forbiddenPath: options.forbiddenPath,
  })
  app.use(router)

  const menuItems = ref<AdminMenuItem[]>(options.menus ?? defaultAdminMenus)
  app.provide('adminMenuItems', menuItems)
  app.provide('adminLogo', options.logo ?? '')
  app.provide('adminTitle', options.title ?? 'Admin')

  options.plugins?.forEach((p) => app.use(p))
  options.onReady?.(app)

  return app
}
