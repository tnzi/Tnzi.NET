import { useAdminAuthStore } from '../stores/useAdminAuthStore'
import { settingsSectionEntryRules } from '../plugin/settings-config'

/**
 * Permission guard composable - thin wrapper over `useAdminAuthStore`'s
 * permission helpers that adds the super-user bypass (`bypassesPermissionChecks`:
 * only when the backend's bypass covers every code) and the framework-wide
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
    if (auth.bypassesPermissionChecks || auth.userInfo === null) return true
    return auth.hasPermission(permission)
  }

  function canAny(permissions: string[]): boolean {
    if (auth.bypassesPermissionChecks || auth.userInfo === null) return true
    return auth.hasAnyPermission(permissions)
  }

  function canAll(permissions: string[]): boolean {
    if (auth.bypassesPermissionChecks || auth.userInfo === null) return true
    return auth.hasAllPermissions(permissions)
  }

  /**
   * Whether the user holds any of these roles (case-insensitive), the role gate
   * of a route's `meta.roles` and of a Settings Center / User Center section's
   * `roles`. An empty list asks for nothing and passes. Same super-user bypass
   * and fail-open as the permission helpers.
   */
  function canAnyRole(roles: readonly string[]): boolean {
    if (auth.bypassesPermissionChecks || auth.userInfo === null) return true
    const wanted = roles.filter((r) => typeof r === 'string' && r !== '')
    return wanted.length === 0 || auth.hasAnyRole(wanted)
  }

  /**
   * Reachability for the Settings Center, which spans many modules and has no
   * single gating code: the user may enter if they hold ANY per-group settings
   * view code (`{group}.settings.{slug}.view`) OR `system.parameter.view` (the
   * Advanced parameters/dictionaries section) OR passes the gate of a custom
   * section the app registered with a `permission` and / or `roles`
   * (`defineAdminApp({ settings: { sections } })`, e.g. the billing sections'
   * `payment.account.view`). Backend `SettingsCenterService`
   * still filters the returned groups per-code, so this only governs whether the
   * entry/route is shown (super-user / not-yet-loaded fail-open, as everywhere).
   */
  function canAnySettings(): boolean {
    if (auth.bypassesPermissionChecks || auth.userInfo === null) return true
    const held = auth.userInfo.permissions ?? []
    if (held.some((p) => p === 'system.parameter.view' || SETTINGS_VIEW_CODE.test(p))) return true
    return settingsSectionEntryRules().some(
      (rule) => (rule.permission ? auth.hasPermission(rule.permission) : true) && canAnyRole(rule.roles),
    )
  }

  /**
   * Whether the permission guard lets the user into a route with this meta:
   * `permission` (single), `permissions` (ANY-of), `anySettingsPermission` and
   * `roles` (ANY-of, case-insensitive) must all hold. Same super-user bypass and
   * fail-open as the helpers above. The router guard decides with it, and a
   * shortcut to a route (a dashboard quick action) hides with it, so the two
   * cannot disagree about who may enter.
   */
  function canReachRoute(meta: Readonly<Record<string, unknown>> | undefined): boolean {
    if (auth.bypassesPermissionChecks || auth.userInfo === null || !meta) return true
    const required = typeof meta.permission === 'string' ? meta.permission : ''
    const requiredAny = stringList(meta.permissions)
    const requiredRoles = stringList(meta.roles)
    return (
      (required ? auth.hasPermission(required) : true) &&
      (requiredAny.length > 0 ? auth.hasAnyPermission(requiredAny) : true) &&
      (meta.anySettingsPermission === true ? canAnySettings() : true) &&
      canAnyRole(requiredRoles)
    )
  }

  return { can, canAny, canAll, canAnyRole, canAnySettings, canReachRoute }
}

function stringList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string' && v !== '') : []
}

/** Matches per-group settings view codes: `x.settings.y.view`. */
const SETTINGS_VIEW_CODE = /^[^.]+\.settings\.[^.]+\.view$/
