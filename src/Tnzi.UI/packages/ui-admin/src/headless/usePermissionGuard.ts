import { useAdminAuthStore } from '../stores/useAdminAuthStore'

/**
 * Permission guard composable — thin wrapper over `useAdminAuthStore`'s
 * permission helpers that adds a super-user bypass.
 *
 * Returns stable function references; callers can destructure freely.
 */
export function usePermissionGuard() {
  const auth = useAdminAuthStore()

  function can(permission: string): boolean {
    if (auth.isSuperUser) return true
    return auth.hasPermission(permission)
  }

  function canAny(permissions: string[]): boolean {
    if (auth.isSuperUser) return true
    return auth.hasAnyPermission(permissions)
  }

  function canAll(permissions: string[]): boolean {
    if (auth.isSuperUser) return true
    return auth.hasAllPermissions(permissions)
  }

  return { can, canAny, canAll }
}
