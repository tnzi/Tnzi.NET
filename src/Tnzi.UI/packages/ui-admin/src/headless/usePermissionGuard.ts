import { useAdminAuthStore } from '../stores/useAdminAuthStore'

/**
 * Permission guard composable - thin wrapper over `useAdminAuthStore`'s
 * permission helpers that adds the super-user bypass and the framework-wide
 * FAIL-OPEN rule while the user isn't loaded yet (`userInfo === null`):
 * mirrors the sidebar filter / `useCrudPage` action gating / widget filter,
 * so bare test mounts and the pre-permission-load window never hide UI the
 * user is actually entitled to. The backend `[ApiAuthorize]` remains the
 * real enforcement.
 *
 * Returns stable function references; callers can destructure freely.
 */
export function usePermissionGuard() {
  const auth = useAdminAuthStore()

  function can(permission: string): boolean {
    if (auth.isSuperUser || auth.userInfo === null) return true
    return auth.hasPermission(permission)
  }

  function canAny(permissions: string[]): boolean {
    if (auth.isSuperUser || auth.userInfo === null) return true
    return auth.hasAnyPermission(permissions)
  }

  function canAll(permissions: string[]): boolean {
    if (auth.isSuperUser || auth.userInfo === null) return true
    return auth.hasAllPermissions(permissions)
  }

  /**
   * Reachability for the Settings Center, which spans many modules and has no
   * single gating code: the user may enter if they hold ANY per-group settings
   * view code (`{group}.settings.{slug}.view`) OR `system.parameter.view` (the
   * Advanced parameters/dictionaries section). Backend `SettingsCenterService`
   * still filters the returned groups per-code, so this only governs whether the
   * entry/route is shown (super-user / not-yet-loaded fail-open, as everywhere).
   */
  function canAnySettings(): boolean {
    if (auth.isSuperUser || auth.userInfo === null) return true
    const held = auth.userInfo.permissions ?? []
    return held.some((p) => p === 'system.parameter.view' || SETTINGS_VIEW_CODE.test(p))
  }

  return { can, canAny, canAll, canAnySettings }
}

/** Matches per-group settings view codes: `x.settings.y.view`. */
const SETTINGS_VIEW_CODE = /^[^.]+\.settings\.[^.]+\.view$/
