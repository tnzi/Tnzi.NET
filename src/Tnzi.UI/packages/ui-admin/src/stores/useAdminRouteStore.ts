import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { en } from '../locales/en'
import { zhCn } from '../locales/zh-cn'
import { DEFAULT_ROUTE_ICONS } from '../router/routeIcons'
import { useAdminAppStore } from './useAdminAppStore'

/**
 * Resolve a dotted i18n key against the bundled admin locale pack.
 * Returns the original key unchanged if no entry matches.
 */
/**
 * Last-segment capitalised fallback for i18n keys that haven't been
 * translated yet (e.g. `tnzi.admin.modules.identity.users.title` →
 * "Users"). Matches what `TAdminAutoBreadcrumb` and `TAdminTabs` render
 * so the sidebar / breadcrumb / tabs all share a single humanised
 * surface when a key is missing.
 */
function humanise(key: string): string {
  if (!key) return ''
  const parts = key.split('.')
  let last = parts[parts.length - 1] ?? key
  if (last === 'title' && parts.length > 1) {
    last = parts[parts.length - 2] ?? key
  }
  // CamelCase → spaced (e.g. `loginLogs` → `Login Logs`).
  const spaced = last.replace(/([a-z])([A-Z])/g, '$1 $2')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}

function resolveI18nKey(key: string, locale: 'en' | 'zh-cn'): string {
  if (!key) return key
  // Bare labels (not dotted i18n keys) — return as-is.
  if (!key.startsWith('admin.') && !key.startsWith('tnzi.admin.')) {
    return key
  }
  // Strip optional `tnzi.` prefix — bundled locales are rooted at `admin.*`.
  const normalized = key.startsWith('tnzi.') ? key.slice(5) : key
  const messages = (locale === 'zh-cn' ? zhCn : en) as Record<string, unknown>
  let node: unknown = messages
  for (const part of normalized.split('.')) {
    if (typeof node === 'object' && node !== null && part in (node as Record<string, unknown>)) {
      node = (node as Record<string, unknown>)[part]
    } else {
      // Phase I.7.10: missing-key fallback — humanise the last segment so
      // users never see raw `tnzi.admin.…` strings in the sidebar / tabs /
      // breadcrumb (mirrors the same fallback in `TAdminAutoBreadcrumb` and
      // `TAdminTabs.renderTitle`).
      return humanise(key)
    }
  }
  return typeof node === 'string' ? node : humanise(key)
}

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
    const appStore = useAdminAppStore()
    const locale = appStore.locale
    /**
     * Build absolute paths during the tree walk. vue-router stores child
     * `route.path` as relative ("users") so a naive copy produces unrouteable
     * menu items — AdminShellRoot's `router.push(menu.path)` then resolves
     * relative to the current page and silently fails.
     */
    function joinPath(parent: string, child: string): string {
      if (child.startsWith('/')) return child
      const left = parent.endsWith('/') ? parent.slice(0, -1) : parent
      const right = child.startsWith('/') ? child.slice(1) : child
      return left ? `${left}/${right}` : `/${right}`
    }
    function toMenuItem(route: AdminRouteRecord, parentPath: string): AdminMenuItem | null {
      if (route.meta?.hideInMenu) return null
      const rawTitle = route.meta?.title ?? route.name
      const absolutePath = joinPath(parentPath, route.path)
      // Phase I.7.6: when `meta.icon` is missing, fall back to the curated
      // default map keyed by route name (covers the 42 built-in admin pages).
      // Consumer-supplied routes still control via `meta.icon` if set.
      const resolvedIcon =
        route.meta?.icon ?? DEFAULT_ROUTE_ICONS[route.name]
      const item: AdminMenuItem = {
        key: route.name,
        label: resolveI18nKey(rawTitle, locale),
        i18nKey: route.meta?.i18nKey,
        icon: resolvedIcon,
        path: absolutePath,
        meta: route.meta,
      }
      if (route.children && route.children.length > 0) {
        const children = route.children
          .map((c) => toMenuItem(c, absolutePath))
          .filter(Boolean) as AdminMenuItem[]
        if (children.length > 0) item.children = children
      }
      return item
    }
    const items = allRoutes.value
      .map((r) => toMenuItem(r, ''))
      .filter(Boolean) as AdminMenuItem[]
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
