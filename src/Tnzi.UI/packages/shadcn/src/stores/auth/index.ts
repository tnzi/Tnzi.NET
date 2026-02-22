/**
 * @tnzi/shadcn/stores/auth
 *
 * Authentication store — thin wrapper delegating to core AuthStateManager.
 * All business logic lives in AuthStateManager; this store only proxies reactive state.
 */

import { computed } from 'vue';
import { AuthStateManager } from '@tnzi/core/state';
import type { StateDeps } from '@tnzi/core/state';
import { createLocalStorageAdapter } from '@tnzi/core/adapters/storage';
import type { LoginDto, LoginResultDto, UserProfile, UpdateUserDto } from '@tnzi/core/services/identity';
import { getStoreHttpClient, getStoreStorage } from '../factory';

// ============================================
// AuthStateManager Singleton
// ============================================

let _manager: AuthStateManager | null = null;

function getManager(): AuthStateManager {
  if (!_manager) {
    const deps: StateDeps = {
      httpClient: getStoreHttpClient(),
      storage: getStoreStorage() ?? createLocalStorageAdapter(),
    };
    _manager = new AuthStateManager(deps);
  }
  return _manager;
}

// ============================================
// Auth Store (reactive proxy over AuthStateManager)
// ============================================

/**
 * Auth store composable.
 *
 * Returns a reactive object that proxies AuthStateManager state and actions.
 * Compatible with the previous createStore-based API surface.
 */
export function useAuthStore() {
  const m = getManager();

  return {
    // State (reactive — AuthStateManager uses reactive(this))
    get isAuthenticated() { return m.isAuthenticated; },
    get accessToken() { return m.accessToken; },
    get refreshToken() { return m.refreshToken; },
    get tokenExpiry() { return m.tokenExpiry; },
    get user() { return m.user; },
    get permissions() { return m.permissions; },
    get roles() { return m.roles; },
    get isRefreshing() { return m.isRefreshing; },
    get error() { return m.error; },

    // Getters
    get isLoggedIn() { return m.isLoggedIn; },
    get userName() { return m.userName; },
    get displayName() { return m.displayName; },
    get avatar() { return m.avatar; },
    get userRoles() { return m.userRoles; },
    get userPermissions() { return m.userPermissions; },
    get isTokenExpired() { return m.isTokenExpired; },
    get tokenExpiresIn() { return m.tokenExpiresIn; },

    // Permission checks
    hasRole: (role: string) => m.hasRole(role),
    hasPermission: (permission: string) => m.hasPermission(permission),
    hasAnyRole: (roles: string[]) => m.hasAnyRole(roles),
    hasAnyPermission: (permissions: string[]) => m.hasAnyPermission(permissions),

    // Actions
    login: (credentials: LoginDto) => m.login(credentials),
    logout: () => m.logout(),
    refreshAccessToken: () => m.refreshAccessToken(),
    fetchUserProfile: () => m.fetchUserProfile(),
    updateProfile: (data: UpdateUserDto) => m.updateProfile(data),
    changePassword: (current: string, next: string) => m.changePassword(current, next),
    setAuth: (result: LoginResultDto) => m.setAuth(result),
    setError: (err: string | null) => m.setError(err),
    clearAuth: () => m.clearAuth(),
    restoreAuth: () => m.restoreAuth(),
  };
}

// ============================================
// Composable Wrapper
// ============================================

/**
 * Vue composable for using auth store.
 * Provides reactive computed refs and bound action methods.
 */
export function useAuth() {
  const m = getManager();

  return {
    // State (reactive computed refs)
    isAuthenticated: computed(() => m.isAuthenticated),
    user: computed(() => m.user),
    accessToken: computed(() => m.accessToken),
    userName: computed(() => m.userName),
    displayName: computed(() => m.displayName),
    avatar: computed(() => m.avatar),
    roles: computed(() => m.userRoles),
    permissions: computed(() => m.userPermissions),
    isLoading: computed(() => m.isRefreshing),
    error: computed(() => m.error),

    // Computed helpers
    isLoggedIn: computed(() => m.isLoggedIn),
    isTokenExpired: computed(() => m.isTokenExpired),
    tokenExpiresIn: computed(() => m.tokenExpiresIn),

    // Actions
    login: (credentials: LoginDto) => m.login(credentials),
    logout: () => m.logout(),
    refreshAccessToken: () => m.refreshAccessToken(),
    fetchUserProfile: () => m.fetchUserProfile(),
    updateProfile: (data: UpdateUserDto) => m.updateProfile(data),
    changePassword: (current: string, next: string) => m.changePassword(current, next),
    setAuth: (result: LoginResultDto) => m.setAuth(result),

    // Permission checks
    hasRole: (role: string) => m.hasRole(role),
    hasPermission: (permission: string) => m.hasPermission(permission),
    hasAnyRole: (roles: string[]) => m.hasAnyRole(roles),
    hasAnyPermission: (permissions: string[]) => m.hasAnyPermission(permissions),
  };
}
