import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import 'pinia-plugin-persistedstate'

/**
 * Which codes the backend's super-admin bypass covers (mirrors
 * `AccessProfileDto.superAdminBypassScope`; absent on the wire = `'All'`).
 */
export type AdminSuperAdminBypassScope = 'All' | 'Platform'

export interface AdminUserInfo {
  id: string
  username: string
  displayName?: string
  /**
   * Short, first-name-only form for personal greetings, the header status bar
   * and the chat "me" label - never the surname (`Hi, John`, not `Hi, John
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
 * Admin auth store - authentication state and permission checks.
 *
 * Integrates with @tnzi/core AuthStateManager and the application's
 * identity service. The login/refresh/logout actions are left as injection
 * points: consumer apps provide them via `createTnziUiAdmin({ authProvider })`.
 */
export const useAdminAuthStore = defineStore('admin-auth', () => {
  // State
  /** In-memory mirror of the access token. Never persisted (see `persist`). */
  const token = ref('')
  const refreshToken = ref('')
  /**
   * Whether a session was established in this browser and not ended since.
   * The persisted half of `isLogin`: it lets a reload start from "signed in,
   * permissions loading" instead of "signed out" while the core client
   * restores its own session, without putting the credential in web storage.
   */
  const signedIn = ref(false)
  const userInfo = ref<AdminUserInfo | null>(null)
  const isSuperUser = ref(false)
  /**
   * The backend's `Authorization:SuperAdminBypassScope` as of the last
   * permission load. `'Platform'` means a super admin's own permission list is
   * already the whole truth (platform codes plus explicit grants), so nothing
   * may bypass it.
   */
  const superAdminBypassScope = ref<AdminSuperAdminBypassScope>('All')

  // Computed
  const isLogin = computed(() => signedIn.value || token.value !== '')
  const userRoles = computed(() => userInfo.value?.roles ?? [])
  const userPermissions = computed(() => userInfo.value?.permissions ?? [])
  const currentTenantId = computed(() => userInfo.value?.tenantId)
  /**
   * Whether permission gates (menus, route guard, `v-permission`, CRUD gates,
   * widgets) may skip the permission list for this user. True only for a super
   * admin whose bypass covers every code. Under a narrowed bypass the access
   * profile already lists exactly what the backend allows, so gating on the
   * list keeps the front end and the backend in agreement (no visible-but-403,
   * no hidden-but-allowed). `isSuperUser` stays the identity flag for the
   * places that ask who the user is (built-in menu toggle, grantable filter).
   */
  const bypassesPermissionChecks = computed(
    () => isSuperUser.value && superAdminBypassScope.value === 'All',
  )

  // Actions
  function setToken(t: string, rt?: string): void {
    token.value = t
    signedIn.value = t !== ''
    if (rt !== undefined) refreshToken.value = rt
  }

  function setUserInfo(info: AdminUserInfo): void {
    userInfo.value = info
  }

  function logout(): void {
    token.value = ''
    refreshToken.value = ''
    signedIn.value = false
    userInfo.value = null
    isSuperUser.value = false
    superAdminBypassScope.value = 'All'
  }

  function setSuperUser(value: boolean): void {
    isSuperUser.value = value
  }

  function setSuperAdminBypassScope(value: AdminSuperAdminBypassScope): void {
    superAdminBypassScope.value = value
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
    // gate (`meta.roles`) - otherwise a route visible in the sidebar (store
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
    signedIn,
    userInfo,
    isSuperUser,
    superAdminBypassScope,
    bypassesPermissionChecks,
    isLogin,
    userRoles,
    userPermissions,
    currentTenantId,
    setToken,
    setUserInfo,
    setSuperUser,
    setSuperAdminBypassScope,
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
    // Neither token is persisted. The core `AuthStateManager` owns the
    // credential pair and decides where it lives: in bearer mode under its own
    // key, in cookie mode (`tokenDelivery: 'cookie'`) in memory only, with
    // nothing auth-related in web storage. A copy here undid cookie mode - the
    // access token sat in localStorage where any script or local process could
    // read it - and in bearer mode it was a stale duplicate that silent rotation
    // never updated. What a reload needs from this store is only "a session was
    // established", which `signedIn` records without the credential.
    pick: ['signedIn', 'userInfo', 'isSuperUser', 'superAdminBypassScope'],
    serializer: {
      serialize: (data) => JSON.stringify(data),
      deserialize: (raw) => fromStoredAuth(raw),
    },
    // Write back what this version persists, so a token stored by an earlier
    // version leaves web storage on the first page load instead of lingering
    // until the next state change happens to rewrite the key.
    afterHydrate: (ctx) => {
      ctx.store.$persist()
    },
  },
})

/**
 * Parse the persisted payload. Payloads written before `signedIn` existed
 * carry the access token instead; its presence is carried over as the marker
 * (so upgrading does not read as a sign-out) and the token itself is dropped.
 */
function fromStoredAuth(raw: string): Record<string, unknown> {
  const data: unknown = JSON.parse(raw)
  if (data === null || typeof data !== 'object') return {}
  const record = data as Record<string, unknown>
  if ('signedIn' in record || !('token' in record)) return record
  const { token: legacyToken, ...rest } = record
  return { ...rest, signedIn: typeof legacyToken === 'string' && legacyToken !== '' }
}
