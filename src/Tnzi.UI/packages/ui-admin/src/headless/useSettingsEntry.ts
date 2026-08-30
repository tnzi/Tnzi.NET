import { computed, getCurrentInstance, type ComputedRef } from 'vue'
import { useRoute, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'
import { useAdminAppStore } from '../stores/useAdminAppStore'
import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { useAdminRouteStore } from '../stores/useAdminRouteStore'
import { routeToWindowInput, useAdminDesktopStore } from '../stores/useAdminDesktopStore'
import { useAdminThemeStore } from '../stores/useAdminThemeStore'
import { usePermissionGuard } from './usePermissionGuard'
import { translatePageKey } from '../i18n/translate'

/**
 * The two shell-level affordances that used to exist ONLY in the sidebar
 * footer: the Settings entry and the super-admin "built-in menus" toggle.
 *
 * Extracted because the `desktop` layout has no sidebar, so both were simply
 * unreachable there - and because the Settings gate is not a one-liner. It has
 * to agree with the navigation guard about three separate things (the route
 * exists, its module is loaded, the user holds one of several possible
 * permission shapes), and a second hand-written copy of that would drift into
 * showing a gear that only ever lands on /403.
 */
export interface SettingsEntry {
  /** Whether to render the Settings affordance at all. */
  available: ComputedRef<boolean>
  /** Translated label. */
  label: ComputedRef<string>
  /** Whether the current route IS settings (for an active state). */
  isActive: ComputedRef<boolean>
  /** Resolved path, for hosts that need to report the selection. */
  path: ComputedRef<string>
  /** Go there - as a window in the desktop layout, a navigation otherwise. */
  open: () => void
  /** Whether to render the built-in-menus toggle (super admin only). */
  canToggleBuiltIn: ComputedRef<boolean>
  builtInEnabled: ComputedRef<boolean>
  builtInTip: ComputedRef<string>
  toggleBuiltIn: () => void
}

function resolveLabel(label: string): string {
  if (!label) return ''
  if (label.startsWith('admin.') || label.startsWith('tnzi.')) return translatePageKey('', label)
  return label
}

/**
 * The router off `globalProperties`, NOT `useRouter()`.
 *
 * `useRouter` reads an injection that only a real `app.use(router)` provides;
 * a component mounted with a stub router in `global.config.globalProperties`
 * (which is how the sidebar's own tests drive this) would get null, and the
 * whole entry would silently disappear. Reading the property covers both.
 */
function safeRouter(): Router | null {
  const instance = getCurrentInstance()
  return (instance?.appContext.config.globalProperties.$router as Router | undefined) ?? null
}

/** Same story for the current route, which only exists with a real router. */
function safeRoute(): RouteLocationNormalizedLoaded | null {
  const instance = getCurrentInstance()
  if (!instance?.appContext.config.globalProperties.$router) return null
  try {
    return useRoute()
  } catch {
    return null
  }
}

export function useSettingsEntry(options?: { enabled?: () => boolean }): SettingsEntry {
  const routeStore = useAdminRouteStore()
  const appStore = useAdminAppStore()
  const authStore = useAdminAuthStore()
  const themeStore = useAdminThemeStore()
  const desktop = useAdminDesktopStore()
  const router = safeRouter()
  const route = safeRoute()
  const { can, canAny, canAnySettings } = usePermissionGuard()

  /**
   * Mirrors `AdminShellRoot`'s rule rather than importing `useAdminShellLayout`,
   * which needs the whole shell prop bag. Below `md` the desktop metaphor is
   * dropped, so navigation is ordinary again.
   */
  const isDesktopLayout = computed(
    () => themeStore.layoutMode === 'desktop' && !appStore.isMobile,
  )

  const settingsMeta = computed(() => {
    if (!router || typeof router.resolve !== 'function') return null
    try {
      return (router.resolve({ name: 'settings' }).meta ?? {}) as {
        permission?: unknown
        permissions?: unknown
        anySettingsPermission?: unknown
        title?: unknown
        icon?: unknown
      }
    } catch {
      return null
    }
  })

  /**
   * Same reachability the guard enforces, with the same fail-open semantics as
   * the menu filter: the bundled route uses `meta.anySettingsPermission`, older
   * custom routes a plain `permission` / `permissions`, and a route that
   * declares none is public.
   */
  const available = computed<boolean>(() => {
    if (options?.enabled && !options.enabled()) return false
    if (typeof router?.hasRoute !== 'function' || !router.hasRoute('settings')) return false
    if (routeStore.unavailableRouteNames.has('settings')) return false
    const meta = settingsMeta.value
    if (!meta) return true
    if (meta.anySettingsPermission === true) return canAnySettings()
    if (typeof meta.permission === 'string' && meta.permission) return can(meta.permission)
    if (Array.isArray(meta.permissions)) {
      const plural = meta.permissions.filter((p): p is string => typeof p === 'string' && p !== '')
      if (plural.length > 0) return canAny(plural)
    }
    return true
  })

  const label = computed(() => resolveLabel('admin.common.settings'))
  const isActive = computed(() => route?.name === 'settings')
  const path = computed(() => {
    if (!router || typeof router.resolve !== 'function') return '/settings'
    try {
      return router.resolve({ name: 'settings' }).path
    } catch {
      return '/settings'
    }
  })

  function open(): void {
    if (!router) return
    if (!isDesktopLayout.value) {
      void router.push({ name: 'settings' })
      return
    }
    // A push in the desktop layout navigates a router whose outlet the window
    // manager replaced - the address bar moves and the screen does not.
    desktop.openOrFocusRoute(
      routeToWindowInput({
        name: 'settings',
        path: path.value,
        meta: settingsMeta.value as Record<string, unknown> | undefined,
      }),
    )
  }

  // Show/hide the framework's preset admin menus. Super admin only, mirroring
  // the menu store's own gate - a persisted "off" from a super-admin session
  // must not leak into the next user's shell.
  const canToggleBuiltIn = computed(() => authStore.isSuperUser)
  const builtInEnabled = computed(() => appStore.showBuiltInMenus)
  const builtInTip = computed(() => resolveLabel('admin.common.builtInMenusTip'))

  function toggleBuiltIn(): void {
    appStore.toggleBuiltInMenus()
  }

  return {
    available,
    label,
    isActive,
    path,
    open,
    canToggleBuiltIn,
    builtInEnabled,
    builtInTip,
    toggleBuiltIn,
  }
}
