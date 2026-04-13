/**
 * @tnzi/core/state/types/auth
 *
 * Authentication state type definitions.
 */

import type { UserProfile } from '../../services/identity/types';
import type { LoginDto, LoginResultDto } from '../../services/identity/types';

// ============================================
// Auth State Types
// ============================================

/**
 * Authentication state interface.
 */
export interface AuthState {
  /** Whether user is authenticated */
  isAuthenticated: boolean;
  /** Current access token */
  accessToken: string | null;
  /** Current refresh token */
  refreshToken: string | null;
  /** Token expiration timestamp */
  tokenExpiry: Date | null;
  /** Current user profile */
  user: UserProfile | null;
  /** User permissions */
  permissions: string[];
  /** User roles */
  roles: string[];
  /** Whether token is being refreshed */
  isRefreshing: boolean;
  /** Last authentication error */
  error: string | null;
}

/**
 * Authentication tokens for persistence.
 */
export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  tokenExpiry: Date | null;
}

/**
 * Auth preferences for "remember me" functionality.
 */
export interface AuthPreferences {
  rememberMe: boolean;
  savedUsername?: string;
}

// ============================================
// Auth Store Actions Interface
// ============================================

/**
 * Authentication store actions interface.
 */
export interface AuthStoreActions {
  /**
   * Perform user login.
   * @param credentials - Login credentials
   * @returns Login result with tokens and user info
   */
  login(credentials: LoginDto): Promise<LoginResultDto>;

  /**
   * Perform user logout.
   * Clears auth state and optionally calls backend logout.
   */
  logout(): Promise<void>;

  /**
   * Refresh the access token.
   * Uses the refresh token to obtain a new access token.
   */
  refreshAccessToken(): Promise<void>;

  /**
   * Fetch and update the current user profile.
   */
  fetchUserProfile(): Promise<void>;

  /**
   * Update user profile data.
   * @param data - Profile update data
   * @returns Updated user profile
   */
  updateProfile(data: unknown): Promise<UserProfile>;

  /**
   * Change user password.
   * @param currentPassword - Current password
   * @param newPassword - New password
   */
  changePassword(currentPassword: string, newPassword: string): Promise<void>;

  /**
   * Restore authentication state from persisted storage.
   */
  restoreAuth(): Promise<void>;

  /**
   * Clear all authentication state.
   */
  clearAuth(): void;

  /**
   * Set authentication state after successful login.
   * @param result - Login result
   */
  setAuth(result: LoginResultDto): void;

  /**
   * Set an authentication error.
   * @param error - Error message
   */
  setError(error: string | null): void;
}

// ============================================
// Auth Store Getters Interface
// ============================================

/**
 * Authentication store getters interface.
 */
export interface AuthStoreGetters {
  /** Whether user is logged in */
  isLoggedIn: boolean;
  /** Current username */
  userName: string;
  /** Current user display name (nickname or username) */
  displayName: string;
  /** Current user avatar URL */
  avatar: string | null;
  /** Current user roles */
  userRoles: string[];
  /** Current user permissions */
  userPermissions: string[];
  /** Whether token is expired */
  isTokenExpired: boolean;
  /** Time remaining until token expires (in seconds) */
  tokenExpiresIn: number;
  /** Whether user has a specific role */
  hasRole: (role: string) => boolean;
  /** Whether user has a specific permission */
  hasPermission: (permission: string) => boolean;
  /** Whether user has any of the specified roles */
  hasAnyRole: (roles: string[]) => boolean;
  /** Whether user has any of the specified permissions */
  hasAnyPermission: (permissions: string[]) => boolean;
}

// ============================================
// Combined Auth Store Interface
// ============================================

/**
 * Complete authentication store interface.
 */
export interface AuthStore extends AuthState, AuthStoreGetters, AuthStoreActions {
  /** Store identifier */
  readonly $id: 'auth';
}

// ============================================
// Auth Store Options (for factory patterns)
// ============================================

/**
 * Auth store factory options.
 */
export interface AuthStoreOptions {
  /** Auth state factory */
  state: () => AuthState;
  /** Auth getters */
  getters: AuthStoreGetters;
  /** Auth actions */
  actions: AuthStoreActions;
}

// ============================================
// Auth Event Types
// ============================================

/**
 * Authentication events for external subscriptions.
 */
export interface AuthEvents {
  /** Fired after successful login */
  onLogin: (user: UserProfile) => void;
  /** Fired after logout */
  onLogout: () => void;
  /** Fired when token is refreshed */
  onTokenRefreshed: (token: string) => void;
  /** Fired when authentication error occurs */
  onError: (error: string) => void;
  /** Fired when session expires */
  onSessionExpired: () => void;
}

/**
 * Auth event handler type.
 */
export type AuthEventHandler = AuthEvents[keyof AuthEvents];
