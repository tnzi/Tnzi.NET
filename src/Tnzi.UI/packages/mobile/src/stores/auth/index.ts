/**
 * @tnzi/mobile/stores/auth
 *
 * Authentication store - thin Pinia wrapper delegating to core AuthStateManager.
 * All business logic lives in AuthStateManager; this store only proxies reactive state.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
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
// Auth Store Definition
// ============================================

export const useAuthStore = defineStore('auth', () => {
  // --- Reactive state (proxied from manager) ---
  const isAuthenticated = computed(() => getManager().isAuthenticated);
  const accessToken = computed(() => getManager().accessToken);
  const refreshToken = computed(() => getManager().refreshToken);
  const tokenExpiry = computed(() => getManager().tokenExpiry);
  const user = computed(() => getManager().user);
  const permissions = computed(() => getManager().permissions);
  const roles = computed(() => getManager().roles);
  const isRefreshing = computed(() => getManager().isRefreshing);
  const error = computed(() => getManager().error);

  // --- Getters ---
  const isLoggedIn = computed(() => getManager().isLoggedIn);
  const userName = computed(() => getManager().userName);
  const displayName = computed(() => getManager().displayName);
  const avatar = computed(() => getManager().avatar);
  const userRoles = computed(() => getManager().userRoles);
  const userPermissions = computed(() => getManager().userPermissions);
  const isTokenExpired = computed(() => getManager().isTokenExpired);
  const tokenExpiresIn = computed(() => getManager().tokenExpiresIn);

  // --- Permission checks ---
  function hasRole(role: string): boolean { return getManager().hasRole(role); }
  function hasPermission(permission: string): boolean { return getManager().hasPermission(permission); }
  function hasAnyRole(roles: string[]): boolean { return getManager().hasAnyRole(roles); }
  function hasAnyPermission(permissions: string[]): boolean { return getManager().hasAnyPermission(permissions); }

  // --- Actions (delegate to manager) ---
  async function login(credentials: LoginDto): Promise<LoginResultDto> {
    return getManager().login(credentials);
  }

  async function logout(): Promise<void> {
    return getManager().logout();
  }

  async function refreshAccessToken(): Promise<void> {
    return getManager().refreshAccessToken();
  }

  async function fetchUserProfile(): Promise<void> {
    return getManager().fetchUserProfile();
  }

  async function updateProfile(data: UpdateUserDto): Promise<UserProfile> {
    return getManager().updateProfile(data);
  }

  async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
    return getManager().changePassword(currentPassword, newPassword);
  }

  function setAuth(result: LoginResultDto): void {
    getManager().setAuth(result);
  }

  function setError(err: string | null): void {
    getManager().setError(err);
  }

  function clearAuth(): void {
    getManager().clearAuth();
  }

  async function restoreAuth(): Promise<void> {
    return getManager().restoreAuth();
  }

  return {
    // State
    isAuthenticated, accessToken, refreshToken, tokenExpiry,
    user, permissions, roles, isRefreshing, error,
    // Getters
    isLoggedIn, userName, displayName, avatar,
    userRoles, userPermissions, isTokenExpired, tokenExpiresIn,
    // Permission checks
    hasRole, hasPermission, hasAnyRole, hasAnyPermission,
    // Actions
    login, logout, refreshAccessToken, fetchUserProfile,
    updateProfile, changePassword,
    setAuth, setError, clearAuth, restoreAuth,
  };
});

// ============================================
// Composable Wrapper
// ============================================

/**
 * Vue composable for using the auth store.
 * Provides reactive computed refs and bound action methods.
 */
export function useAuth() {
  const store = useAuthStore();

  return {
    // State (reactive)
    isAuthenticated: computed(() => store.isAuthenticated),
    user: computed(() => store.user),
    accessToken: computed(() => store.accessToken),
    userName: computed(() => store.userName),
    displayName: computed(() => store.displayName),
    avatar: computed(() => store.avatar),
    roles: computed(() => store.userRoles),
    permissions: computed(() => store.userPermissions),
    isLoading: computed(() => store.isRefreshing),
    error: computed(() => store.error),

    // Computed helpers
    isLoggedIn: computed(() => store.isLoggedIn),
    isTokenExpired: computed(() => store.isTokenExpired),
    tokenExpiresIn: computed(() => store.tokenExpiresIn),

    // Actions
    login: store.login.bind(store),
    logout: store.logout.bind(store),
    refreshAccessToken: store.refreshAccessToken.bind(store),
    fetchUserProfile: store.fetchUserProfile.bind(store),
    updateProfile: store.updateProfile.bind(store),
    changePassword: store.changePassword.bind(store),
    setAuth: store.setAuth.bind(store),
    restoreAuth: store.restoreAuth.bind(store),

    // Permission checks
    hasRole: store.hasRole.bind(store),
    hasPermission: store.hasPermission.bind(store),
    hasAnyRole: store.hasAnyRole.bind(store),
    hasAnyPermission: store.hasAnyPermission.bind(store),
  };
}
