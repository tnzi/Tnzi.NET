/**
 * @tnzi/core/stores/user
 *
 * User preferences and profile store type definitions.
 * Provides framework-agnostic interface for user state management.
 */

import type { UserDto } from '../../services/identity/types';

// ============================================
// User Preferences Types
// ============================================

/**
 * User interface theme preference.
 */
export type UserTheme = 'light' | 'dark' | 'system';

/**
 * User interface language preference.
 */
export type UserLanguage = 'zh-CN' | 'en-US' | 'zh-TW';

/**
 * User notification preferences.
 */
export interface UserNotificationPreferences {
  /** Enable desktop notifications */
  desktop: boolean;
  /** Enable email notifications */
  email: boolean;
  /** Enable push notifications */
  push: boolean;
  /** Enable in-app notifications */
  inApp: boolean;
  /** Notify on mentions */
  mentions: boolean;
  /** Notify on comments */
  comments: boolean;
  /** Notify on updates */
  updates: boolean;
  /** Quiet hours start time */
  quietHoursStart?: string;
  /** Quiet hours end time */
  quietHoursEnd?: string;
}

/**
 * User interface preferences.
 */
export interface UserPreferences {
  /** UI Theme */
  theme: UserTheme;
  /** Interface language */
  language: UserLanguage;
  /** Date format */
  dateFormat: string;
  /** Time format (12h or 24h) */
  timeFormat: '12h' | '24h';
  /** Number format */
  numberFormat: string;
  /** Pagination default page size */
  pageSize: number;
  /** Enable compact mode */
  compactMode: boolean;
  /** Enable animations */
  animations: boolean;
  /** Notification preferences */
  notifications: UserNotificationPreferences;
}

// ============================================
// Recent Items Types
// ============================================

/**
 * Recent item types.
 */
export type RecentItemType = 'page' | 'file' | 'document' | 'search';

/**
 * Recent item for quick access.
 */
export interface RecentItem {
  /** Unique item ID */
  id: string;
  /** Item type */
  type: RecentItemType;
  /** Display title */
  title: string;
  /** URL or path */
  url: string;
  /** Icon or thumbnail */
  icon?: string;
  /** Access timestamp */
  accessedAt: Date;
  /** Metadata */
  metadata?: Record<string, unknown>;
}

// ============================================
// User State Types
// ============================================

/**
 * User state interface.
 */
export interface UserState {
  /** Current user profile data */
  currentUser: UserDto | null;
  /** User preferences */
  preferences: UserPreferences;
  /** Recently accessed items */
  recentItems: RecentItem[];
  /** User favorites/shortcuts */
  favorites: RecentItem[];
  /** Whether user data is loading */
  isLoading: boolean;
  /** Last error */
  error: string | null;
}

// ============================================
// User Store Actions Interface
// ============================================

/**
 * User store actions interface.
 */
export interface UserStoreActions {
  /**
   * Fetch the current user profile from server.
   */
  fetchCurrentUser(): Promise<void>;

  /**
   * Update user preferences.
   * @param preferences - Partial preferences to update
   */
  updatePreferences(preferences: Partial<UserPreferences>): Promise<void>;

  /**
   * Reset preferences to defaults.
   */
  resetPreferences(): void;

  /**
   * Update user profile data.
   * @param data - Profile update data
   * @returns Updated user profile
   */
  updateProfile(data: unknown): Promise<UserDto>;

  /**
   * Add an item to recent items.
   * @param item - Item to add
   */
  addRecentItem(item: Omit<RecentItem, 'accessedAt'>): void;

  /**
   * Remove an item from recent items.
   * @param id - Item ID to remove
   */
  removeRecentItem(id: string): void;

  /**
   * Clear all recent items.
   */
  clearRecentItems(): void;

  /**
   * Add an item to favorites.
   * @param item - Item to add
   */
  addFavorite(item: Omit<RecentItem, 'accessedAt'>): void;

  /**
   * Remove an item from favorites.
   * @param id - Item ID to remove
   */
  removeFavorite(id: string): void;

  /**
   * Check if an item is in favorites.
   * @param id - Item ID to check
   */
  isFavorite(id: string): boolean;

  /**
   * Load persisted user data.
   */
  loadPersistedData(): Promise<void>;

  /**
   * Persist user data.
   */
  persistData(): Promise<void>;
}

// ============================================
// User Store Getters Interface
// ============================================

/**
 * User store getters interface.
 */
export interface UserStoreGetters {
  /** Whether user is loaded */
  isLoaded: boolean;
  /** Whether user is authenticated */
  isAuthenticated: boolean;
  /** Current user display name */
  displayName: string;
  /** Current user username */
  userName: string;
  /** Current user avatar URL */
  avatar: string | null;
  /** Current user email */
  email: string | null;
  /** Current user roles */
  roles: string[];
  /** Theme preference */
  theme: UserTheme;
  /** Language preference */
  language: UserLanguage;
  /** Recent items count */
  recentItemsCount: number;
  /** Favorites count */
  favoritesCount: number;
}

// ============================================
// Combined User Store Interface
// ============================================

/**
 * Complete user store interface.
 */
export interface UserStore extends UserState, UserStoreGetters, UserStoreActions {
  /** Store identifier */
  readonly $id: 'user';
}

// ============================================
// User Store Options
// ============================================

/**
 * Default user preferences.
 */
export const defaultUserPreferences: UserPreferences = {
  theme: 'system',
  language: 'zh-CN',
  dateFormat: 'YYYY-MM-DD',
  timeFormat: '24h',
  numberFormat: 'decimal',
  pageSize: 10,
  compactMode: false,
  animations: true,
  notifications: {
    desktop: true,
    email: true,
    push: false,
    inApp: true,
    mentions: true,
    comments: true,
    updates: false,
  },
};

/**
 * User store factory options.
 */
export interface UserStoreOptions {
  /** User state factory */
  state: () => UserState;
  /** User getters */
  getters: UserStoreGetters;
  /** User actions */
  actions: UserStoreActions;
}
