import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export interface AdminRouteMeta {
  title: string
  i18nKey?: string
  icon?: string
  order?: number
  constant?: boolean
  keepAlive?: boolean
  hideInMenu?: boolean
  permissions?: string[]
  roles?: string[]
  activeMenu?: string
  fixedIndexInTab?: number
  multiTab?: boolean
}

export interface AdminRouteRecord {
  name: string
  path: string
  component?: () => Promise<unknown>
  meta?: AdminRouteMeta
  children?: AdminRouteRecord[]
}

export interface AdminMenuItem {
  key: string
  label: string
  i18nKey?: string
  icon?: string
  path: string
  meta?: AdminRouteMeta
  children?: AdminMenuItem[]
}

/**
 * Admin route store — manages constant routes (always available), auth routes
 * (filtered by permissions), the derived menu tree, and the keepAlive cache list.
 */
export const useAdminRouteStore = defineStore('admin-route', () => {
  const constantRoutes = ref<AdminRouteRecord[]>([])
  const authRoutes = ref<AdminRouteRecord[]>([])
  const routesLoaded = ref(false)

  const allRoutes = computed<AdminRouteRecord[]>(() => [
    ...constantRoutes.value,
    ...authRoutes.value,
  ])

  /** Cache-eligible route names (meta.keepAlive === true). Feeds `<KeepAlive :include>`. */
  const cacheRoutes = ref<string[]>([])

  /** Derived menu tree from allRoutes, excluding hideInMenu entries, sorted by meta.order. */
  const menus = computed<AdminMenuItem[]>(() => {
    function toMenuItem(route: AdminRouteRecord): AdminMenuItem | null {
      if (route.meta?.hideInMenu) return null
      const item: AdminMenuItem = {
        key: route.name,
        label: route.meta?.title ?? route.name,
        i18nKey: route.meta?.i18nKey,
        icon: route.meta?.icon,
        path: route.path,
        meta: route.meta,
      }
      if (route.children && route.children.length > 0) {
        const children = route.children.map(toMenuItem).filter(Boolean) as AdminMenuItem[]
        if (children.length > 0) item.children = children
      }
      return item
    }
    const items = allRoutes.value.map(toMenuItem).filter(Boolean) as AdminMenuItem[]
    items.sort((a, b) => (a.meta?.order ?? 999) - (b.meta?.order ?? 999))
    return items
  })

  function filterRoutesByPermissions(
    routes: AdminRouteRecord[],
    userPermissions: string[],
  ): AdminRouteRecord[] {
    const result: AdminRouteRecord[] = []
    for (const route of routes) {
      const requiredPerms = route.meta?.permissions ?? []
      const hasPerm =
        requiredPerms.length === 0 || requiredPerms.some((p) => userPermissions.includes(p))
      if (!hasPerm) continue

      const filtered: AdminRouteRecord = { ...route }
      if (route.children) {
        filtered.children = filterRoutesByPermissions(route.children, userPermissions)
        if (filtered.children.length === 0 && !route.component) continue
      }
      result.push(filtered)
    }
    return result
  }

  function collectCacheRouteNames(routes: AdminRouteRecord[]): string[] {
    const names: string[] = []
    for (const route of routes) {
      if (route.meta?.keepAlive) names.push(route.name)
      if (route.children) {
        names.push(...collectCacheRouteNames(route.children))
      }
    }
    return names
  }

  function setConstantRoutes(routes: AdminRouteRecord[]): void {
    constantRoutes.value = routes
    cacheRoutes.value = collectCacheRouteNames([...routes, ...authRoutes.value])
  }

  function setAuthRoutes(routes: AdminRouteRecord[], userPermissions: string[]): void {
    authRoutes.value = filterRoutesByPermissions(routes, userPermissions)
    cacheRoutes.value = collectCacheRouteNames([...constantRoutes.value, ...authRoutes.value])
    routesLoaded.value = true
  }

  function resetRouteCache(routeName: string): void {
    cacheRoutes.value = cacheRoutes.value.filter((n) => n !== routeName)
  }

  function clearRoutes(): void {
    constantRoutes.value = []
    authRoutes.value = []
    cacheRoutes.value = []
    routesLoaded.value = false
  }

  return {
    constantRoutes,
    authRoutes,
    allRoutes,
    routesLoaded,
    menus,
    cacheRoutes,
    setConstantRoutes,
    setAuthRoutes,
    resetRouteCache,
    clearRoutes,
  }
})
