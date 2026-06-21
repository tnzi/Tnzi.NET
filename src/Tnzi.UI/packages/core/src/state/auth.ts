/**
 * @tnzi/core/state/auth
 *
 * Authentication state manager — pure logic layer.
 * Uses @vue/reactivity for reactive state, UI packages can use directly or wrap as Pinia store.
 */

import { reactive } from '@vue/reactivity';
import type { AuthState } from './types/auth';
import type { LoginDto, LoginResultDto, UserProfile, UserDto, UpdateUserDto } from '../services/identity/types';
import { useAuthApi, useProfileApi } from '../services/identity/index';
import { useLogger } from '../adapters/logger';
import type { StateDeps } from './types/deps';

/**
 * Map UserDto to UserProfile.
 * UserDto doesn't have a permissions field, uses existing permissions or empty array.
 */
function toUserProfile(dto: UserDto, existingPermissions: string[] = []): UserProfile {
  return {
    id: dto.id,
    userName: dto.userName,
    email: dto.email,
    phoneNumber: dto.phoneNumber,
    nickname: dto.nickname,
    avatar: dto.avatar,
    roles: dto.roles,
    permissions: existingPermissions,
  };
}

// ============================================
// Initial state
// ============================================

export function createInitialAuthState(): AuthState {
  return {
    isAuthenticated: false,
    accessToken: null,
    refreshToken: null,
    tokenExpiry: null,
    user: null,
    permissions: [],
    roles: [],
    isRefreshing: false,
    error: null,
  };
}

// ============================================
// AuthStateManager
// ============================================

/**
 * Authentication state manager.
 *
 * Uses `reactive(this)` for Vue reactivity:
 * - All property reads are automatically tracked
 * - All property writes automatically trigger updates
 * - Class getters become computed properties
 *
 * Usage:
 * ```ts
 * const auth = new AuthStateManager(deps);
 * // Use directly in Vue templates
 * auth.isLoggedIn  // reactive
 * await auth.login(credentials);
 * ```
 */
export class AuthStateManager {
  // State
  isAuthenticated = false;
  accessToken: string | null = null;
  refreshToken: string | null = null;
  tokenExpiry: Date | null = null;
  user: UserProfile | null = null;
  permissions: string[] = [];
  roles: string[] = [];
  isRefreshing = false;
  error: string | null = null;

  /** Mutex: pending refresh promise for deduplication */
  private _refreshPromise: Promise<void> | null = null;
  /** Mutex: pending login promise for deduplication */
  private _loginPromise: Promise<LoginResultDto> | null = null;

  /** Persisted-storage keys derived from the configurable storage prefix. */
  private readonly _keys: { token: string; refresh: string; expiry: string };

  constructor(private readonly deps: StateDeps) {
    const prefix = deps.storagePrefix ?? 'tnzi:auth';
    this._keys = {
      token: `${prefix}:token`,
      refresh: `${prefix}:refresh`,
      expiry: `${prefix}:expiry`,
    };
    return reactive(this) as this;
  }

  // ============================================
  // Getters (reactive computed properties)
  // ============================================

  get isLoggedIn(): boolean {
    return this.isAuthenticated && !!this.accessToken;
  }

  get userName(): string {
    return this.user?.userName ?? '';
  }

  get displayName(): string {
    return this.user?.nickname ?? this.user?.userName ?? 'Guest';
  }

  get avatar(): string | null {
    return this.user?.avatar ?? null;
  }

  get userRoles(): string[] {
    return this.roles.length > 0 ? this.roles : (this.user?.roles ?? []);
  }

  get userPermissions(): string[] {
    return this.permissions.length > 0 ? this.permissions : (this.user?.permissions ?? []);
  }

  get isTokenExpired(): boolean {
    if (!this.tokenExpiry) return true;
    return new Date(this.tokenExpiry) <= new Date();
  }

  get tokenExpiresIn(): number {
    if (!this.tokenExpiry) return 0;
    const remaining = new Date(this.tokenExpiry).getTime() - Date.now();
    return Math.max(0, Math.floor(remaining / 1000));
  }

  // ============================================
  // Permission checking
  // ============================================

  hasRole(role: string): boolean {
    return this.userRoles.includes(role);
  }

  hasPermission(permission: string): boolean {
    return this.userPermissions.includes(permission);
  }

  hasAnyRole(roles: string[]): boolean {
    return roles.some(role => this.userRoles.includes(role));
  }

  hasAnyPermission(permissions: string[]): boolean {
    return permissions.some(perm => this.userPermissions.includes(perm));
  }

  // ============================================
  // Actions (async business logic)
  // ============================================

  /**
   * Login with credentials.
   * Uses mutex to prevent concurrent login attempts.
   */
  async login(credentials: LoginDto): Promise<LoginResultDto> {
    // If a login is already in progress, wait for it
    if (this._loginPromise) {
      return this._loginPromise;
    }

    this._loginPromise = this._doLogin(credentials);
    try {
      return await this._loginPromise;
    } finally {
      this._loginPromise = null;
    }
  }

  private async _doLogin(credentials: LoginDto): Promise<LoginResultDto> {
    this.error = null;
    this.isRefreshing = true;

    try {
      const api = useAuthApi(this.deps.httpClient);
      const result = await api.loginWithRefreshToken(credentials);
      if (!result.succeeded || !result.data) {
        throw new Error(result.message ?? 'Login failed');
      }
      const tokenResult = result.data;
      // Set token first so profile fetch is authenticated
      this.accessToken = tokenResult.accessToken;
      this.refreshToken = tokenResult.refreshToken;
      this.tokenExpiry = new Date(Date.now() + tokenResult.expiresIn * 1000);
      this.isAuthenticated = true;
      this.deps.httpClient.setAccessToken(this.accessToken);

      // Fetch profile and permissions in parallel to reduce login latency
      await Promise.all([this.fetchUserProfile(), this._fetchPermissions()]);
      this.persistTokens();

      return {
        accessToken: tokenResult.accessToken,
        refreshToken: tokenResult.refreshToken,
        expiresIn: tokenResult.expiresIn,
        tokenType: 'Bearer',
        user: this.user!,
      };
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Login failed';
      throw error;
    } finally {
      this.isRefreshing = false;
    }
  }

  /**
   * Logout and clean up all auth state.
   * Invokes onLogout callback (e.g., to clear UserStateManager).
   */
  async logout(): Promise<void> {
    try {
      if (this.accessToken && this.user?.id) {
        const api = useAuthApi(this.deps.httpClient);
        await api.logout().catch(() => {});
      }
    } finally {
      this.clearAuth();
      this.clearPersistedTokens();

      // Notify dependent state managers (e.g., UserStateManager)
      try {
        await this.deps.onLogout?.();
      } catch {
        // Ignore errors from logout callback
      }

      this.deps.router?.push('/login');
    }
  }

  /**
   * Refresh access token.
   * Uses mutex to deduplicate concurrent refresh attempts:
   * only the first call executes the actual refresh, subsequent calls wait for the same result.
   */
  async refreshAccessToken(): Promise<void> {
    // Throw (instead of silent return) when no refresh token is available
    // so callers — particularly HttpClient.refreshTokenFn wrappers — can
    // distinguish "refreshed successfully" from "could not refresh". A
    // silent no-op here caused HttpClient to retry the original request
    // with the same stale access token, get another 401, and short-circuit
    // out without firing `onUnauthorized` — so the page would stay mounted
    // while every API call kept failing.
    if (!this.refreshToken) {
      throw new Error('No refresh token available');
    }

    // If a refresh is already in progress, wait for it
    if (this._refreshPromise) {
      return this._refreshPromise;
    }

    this._refreshPromise = this._doRefreshToken();
    try {
      await this._refreshPromise;
    } finally {
      this._refreshPromise = null;
    }
  }

  private async _doRefreshToken(): Promise<void> {
    this.isRefreshing = true;
    this.error = null;

    try {
      const api = useAuthApi(this.deps.httpClient);
      const result = await api.refreshToken({ refreshToken: this.refreshToken! });
      if (!result.succeeded || !result.data) {
        throw new Error(result.message ?? 'Token refresh failed');
      }
      this.accessToken = result.data.accessToken;
      this.refreshToken = result.data.refreshToken;
      this.tokenExpiry = new Date(Date.now() + result.data.expiresIn * 1000);
      this.persistTokens();

      // Sync token to HTTP client
      this.deps.httpClient.setAccessToken(this.accessToken);

      // Fetch updated permissions after token refresh
      await this._fetchPermissions();
    } catch (error) {
      this.error = 'Session expired, please login again';
      // Clear mutex BEFORE logout so concurrent callers don't await a failed promise
      this._refreshPromise = null;
      await this.logout();
      throw error;
    } finally {
      this.isRefreshing = false;
    }
  }

  async fetchUserProfile(): Promise<void> {
    if (!this.isAuthenticated) return;

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.get();
      if (result.succeeded && result.data) {
        this.user = toUserProfile(result.data, this.permissions);
      }
    } catch (error) {
      useLogger().error('Failed to fetch user profile:', error);
    }
  }

  async updateProfile(data: UpdateUserDto): Promise<UserProfile> {
    if (!this.isAuthenticated) {
      throw new Error('Not authenticated');
    }

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.update(data);
      if (!result.succeeded || !result.data) {
        throw new Error(result.message ?? 'Update failed');
      }
      this.user = toUserProfile(result.data, this.permissions);
      return this.user;
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Update failed';
      throw error;
    }
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    if (!this.isAuthenticated) {
      throw new Error('Not authenticated');
    }

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.changePassword({ currentPassword, newPassword });
      if (!result.succeeded) {
        throw new Error(result.message ?? 'Password change failed');
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Password change failed';
      throw error;
    }
  }

  // ============================================
  // State management
  // ============================================

  setAuth(result: LoginResultDto): void {
    this.isAuthenticated = true;
    this.accessToken = result.accessToken;
    this.refreshToken = result.refreshToken;
    this.tokenExpiry = new Date(Date.now() + result.expiresIn * 1000);
    this.user = result.user;
    this.roles = result.user.roles;
    this.permissions = result.user.permissions;
    this.error = null;

    // Sync token to HTTP client
    this.deps.httpClient.setAccessToken(this.accessToken);
  }

  clearAuth(): void {
    Object.assign(this, createInitialAuthState());
    // Clear token from HTTP client
    this.deps.httpClient.setAccessToken(null);
  }

  setError(error: string | null): void {
    this.error = error;
  }

  // ============================================
  // Persistence (restore from / write to storage)
  // ============================================

  async restoreAuth(): Promise<void> {
    const token = this.deps.storage.get<string>(this._keys.token);
    const refresh = this.deps.storage.get<string>(this._keys.refresh);
    const expiry = this.deps.storage.get<string>(this._keys.expiry);

    if (!token || !refresh) return;

    this.accessToken = token;
    this.refreshToken = refresh;
    this.tokenExpiry = expiry ? new Date(expiry) : null;

    // Sync token to HTTP client
    this.deps.httpClient.setAccessToken(this.accessToken);

    // Try to fetch user profile
    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.get();
      if (!result.succeeded || !result.data) {
        throw new Error('Failed to fetch profile');
      }
      this.user = toUserProfile(result.data);
      this.roles = result.data.roles ?? [];

      // Fetch permissions before marking as authenticated,
      // so permission checks don't run against stale/empty permissions
      await this._fetchPermissions();
      this.isAuthenticated = true;
    } catch {
      // Token may be expired, try refresh
      try {
        await this.refreshAccessToken();
        await this.fetchUserProfile();
      } catch {
        // Refresh also failed, clear auth
        this.clearAuth();
        this.clearPersistedTokens();
      }
    }
  }

  // ============================================
  // Internal methods
  // ============================================

  /**
   * Fetch permissions using the configured permissionsFetchFn.
   * Falls back to empty array if not configured.
   */
  private async _fetchPermissions(): Promise<void> {
    if (this.deps.permissionsFetchFn) {
      try {
        this.permissions = await this.deps.permissionsFetchFn();
      } catch {
        // Non-critical: keep existing permissions
        useLogger().warn('Failed to fetch permissions');
      }
    }
  }

  private persistTokens(): void {
    if (this.accessToken) {
      this.deps.storage.set(this._keys.token, this.accessToken);
    }
    if (this.refreshToken) {
      this.deps.storage.set(this._keys.refresh, this.refreshToken);
    }
    if (this.tokenExpiry) {
      this.deps.storage.set(this._keys.expiry, this.tokenExpiry.toISOString());
    }
  }

  private clearPersistedTokens(): void {
    this.deps.storage.remove(this._keys.token);
    this.deps.storage.remove(this._keys.refresh);
    this.deps.storage.remove(this._keys.expiry);
  }
}
