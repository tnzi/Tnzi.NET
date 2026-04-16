import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import 'pinia-plugin-persistedstate'

export interface AdminUserInfo {
  id: string
  username: string
  displayName?: string
  email?: string
  avatar?: string
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
    return userPermissions.value.includes(permission)
  }

  function hasAnyPermission(permissions: string[]): boolean {
    return permissions.some((p) => hasPermission(p))
  }

  function hasAllPermissions(permissions: string[]): boolean {
    return permissions.every((p) => hasPermission(p))
  }

  function hasRole(role: string): boolean {
    return userRoles.value.includes(role)
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
