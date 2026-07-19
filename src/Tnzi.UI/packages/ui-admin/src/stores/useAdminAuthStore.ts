import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import 'pinia-plugin-persistedstate'

export interface AdminUserInfo {
  id: string
  username: string
  displayName?: string
  /**
   * Short, first-name-only form for personal greetings, the header status bar
   * and the chat "me" label — never the surname (`Hi, John`, not `Hi, John
   * Doe`). Precedence: nickname → firstName → username. Formal / accountability
   * surfaces (e.g. the watermark) keep using {@link displayName} (full name).
   */
  shortName?: string
  email?: string
  avatar?: string
  /** Local-upload avatar file id (Identity UserDetail.AvatarId). Drives the
   *  header status-bar avatar through `resolveAvatarUrl` when there is no
   *  external `avatar` link. */
  avatarId?: string | null
  roles: string[]
  permissions: string[]
  tenantId?: string
}

/**
 * Admin auth store — authentication state and permission checks.
 *
 * Integrates with @tnzi/core AuthStateManager and the application's
 * identity service. The login/refresh/logout actions are left as injection
 * points: consumer apps provide them via `createTnziUiAdmin({ authProvider })`.
 */
export const useAdminAuthStore = defineStore('admin-auth', () => {
  // State
  const token = ref('')
  const refreshToken = ref('')
  const userInfo = ref<AdminUserInfo | null>(null)
  const isSuperUser = ref(false)

  // Computed
  const isLogin = computed(() => !!token.value)
  const userRoles = computed(() => userInfo.value?.roles ?? [])
  const userPermissions = computed(() => userInfo.value?.permissions ?? [])
  const currentTenantId = computed(() => userInfo.value?.tenantId)

  // Actions
  function setToken(t: string, rt?: string): void {
    token.value = t
    if (rt !== undefined) refreshToken.value = rt
  }

  function setUserInfo(info: AdminUserInfo): void {
    userInfo.value = info
  }

  function logout(): void {
    token.value = ''
    refreshToken.value = ''
    userInfo.value = null
    isSuperUser.value = false
  }

  function setSuperUser(value: boolean): void {
    isSuperUser.value = value
  }

  function hasPermission(permission: string): boolean {
    // Case-insensitive to mirror the backend (StringComparer.OrdinalIgnoreCase).
    // Route + FrameworkPermissions codes are all lowercase today, but pinning
    // this prevents the silent-filter failure mode if casing ever drifts.
    const target = permission.toLowerCase()
    return userPermissions.value.some((p) => p.toLowerCase() === target)
  }

  function hasAnyPermission(permissions: string[]): boolean {
    return permissions.some((p) => hasPermission(p))
  }

  function hasAllPermissions(permissions: string[]): boolean {
    return permissions.every((p) => hasPermission(p))
  }

  function hasRole(role: string): boolean {
    // Case-insensitive, matching `hasPermission` and the route store's role
    // gate (`meta.roles`) — otherwise a route visible in the sidebar (store
    // lowercases both sides) would bounce to /403 on click when the backend's
    // role casing differs from the declared `meta.roles`.
    const target = role.toLowerCase()
    return userRoles.value.some((r) => r.toLowerCase() === target)
  }

  function hasAnyRole(roles: string[]): boolean {
    return roles.some((r) => hasRole(r))
  }

  return {
    token,
    refreshToken,
    userInfo,
    isSuperUser,
    isLogin,
    userRoles,
    userPermissions,
    currentTenantId,
    setToken,
    setUserInfo,
    setSuperUser,
    logout,
    hasPermission,
    hasAnyPermission,
    hasAllPermissions,
    hasRole,
    hasAnyRole,
  }
}, {
  persist: {
    key: 'tnzi-admin-auth',
    pick: ['token', 'refreshToken', 'userInfo', 'isSuperUser'],
  },
})
