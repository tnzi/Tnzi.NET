/**
 * @tnzi/vant/stores/user
 *
 * User preferences and profile store implementation using Pinia defineStore.
 * Manages user profile data, preferences, recent items, and favorites.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
import type {
  UserState,
  UserPreferences,
  UserTheme,
  RecentItem,
} from '@tnzi/core/stores/user';
import { defaultUserPreferences } from '@tnzi/core/stores/user';
import type { UserDto, UpdateUserDto } from '@tnzi/core/services/identity';
import { useProfileApi } from '@tnzi/core/services/identity';
import { getStoreHttpClient, getStoreStorage } from '../factory';

// ============================================
// Storage Keys & Constants
// ============================================

const STORAGE_KEY = 'user_data';
const MAX_RECENT_ITEMS = 20;
const MAX_FAVORITES = 10;

// ============================================
// Default State
// ============================================

const defaultState = (): UserState => ({
  currentUser: null,
  preferences: { ...defaultUserPreferences },
  recentItems: [],
  favorites: [],
  isLoading: false,
  error: null,
});

// ============================================
// User Store Definition
// ============================================

export const useUserStore = defineStore('user', {
  state: defaultState,

  getters: {
    /** Whether user data has been loaded */
    isLoaded: (state): boolean => state.currentUser !== null,

    /** Whether a user is authenticated */
    isAuthenticated: (state): boolean => state.currentUser !== null,

    /** Display name (nickname preferred, fallback to username) */
    displayName: (state): string => {
      if (!state.currentUser) return 'Guest';
      return state.currentUser.nickname ?? state.currentUser.userName ?? 'Guest';
    },

    /** Current username */
    userName: (state): string => state.currentUser?.userName ?? '',

    /** User avatar URL */
    avatar: (state): string | null => state.currentUser?.avatar ?? null,

    /** User email */
    email: (state): string | null => state.currentUser?.email ?? null,

    /** User roles */
    roles: (state): string[] => state.currentUser?.roles ?? [],

    /** Current theme preference */
    theme: (state): UserTheme => state.preferences.theme,

    /** Current language preference */
    language: (state): string => state.preferences.language,

    /** Number of recent items */
    recentItemsCount: (state): number => state.recentItems.length,

    /** Number of favorites */
    favoritesCount: (state): number => state.favorites.length,
  },

  actions: {
    // ============================================
    // Profile Actions
    // ============================================

    /**
     * Fetch the current user profile from server.
     */
    async fetchCurrentUser(): Promise<void> {
      this.isLoading = true;
      this.error = null;

      try {
        const client = getStoreHttpClient();
        const api = useProfileApi(client);
        const result = await api.get();
        if (result.succeeded && result.data) {
          this.currentUser = result.data;
        }
      } catch (error) {
        this.error = error instanceof Error ? error.message : 'Failed to fetch user';
        throw error;
      } finally {
        this.isLoading = false;
      }
    },

    /**
     * Update user profile on server.
     * @param data - Profile update data
     * @returns Updated user DTO
     */
    async updateProfile(data: UpdateUserDto): Promise<UserDto> {
      if (!this.isAuthenticated) {
        throw new Error('Not authenticated');
      }

      this.isLoading = true;
      this.error = null;

      try {
        const client = getStoreHttpClient();
        const api = useProfileApi(client);
        const result = await api.update(data);
        if (result.succeeded && result.data) {
          this.currentUser = result.data;
          return result.data;
        }
        throw new Error('Update failed');
      } catch (error) {
        this.error = error instanceof Error ? error.message : 'Update failed';
        throw error;
      } finally {
        this.isLoading = false;
      }
    },

    // ============================================
    // Preferences Actions
    // ============================================

    /**
     * Update user preferences (partial merge).
     * @param preferences - Partial preferences to merge
     */
    async updatePreferences(preferences: Partial<UserPreferences>): Promise<void> {
      this.preferences = { ...this.preferences, ...preferences };

      if (preferences.theme !== undefined) {
        this.applyTheme(this.preferences.theme);
      }
      if (preferences.language !== undefined) {
        this.applyLanguage(this.preferences.language);
      }

      this.persistData();
    },

    /**
     * Reset all preferences to defaults.
     */
    resetPreferences(): void {
      this.preferences = { ...defaultUserPreferences };
      this.applyTheme(this.preferences.theme);
      this.applyLanguage(this.preferences.language);
      this.persistData();
    },

    /**
     * Set theme preference.
     * @param theme - Theme mode
     */
    setTheme(theme: UserTheme): void {
      this.preferences.theme = theme;
      this.applyTheme(theme);
      this.persistData();
    },

    /**
     * Set language preference.
     * @param language - Language code
     */
    setLanguage(language: string): void {
      this.preferences.language = language as UserPreferences['language'];
      this.applyLanguage(language);
      this.persistData();
    },

    // ============================================
    // Recent Items Actions
    // ============================================

    /**
     * Add an item to recent items (moves to front if existing).
     * @param item - Recent item (without accessedAt)
     */
    addRecentItem(item: Omit<RecentItem, 'accessedAt'>): void {
      const newItem: RecentItem = {
        ...item,
        accessedAt: new Date(),
      };

      // Remove existing item with same ID
      this.recentItems = this.recentItems.filter((i: RecentItem) => i.id !== item.id);

      // Add new item at beginning
      this.recentItems = [newItem, ...this.recentItems];

      // Trim to max count
      if (this.recentItems.length > MAX_RECENT_ITEMS) {
        this.recentItems = this.recentItems.slice(0, MAX_RECENT_ITEMS);
      }

      this.persistData();
    },

    /**
     * Remove an item from recent items.
     * @param id - Item ID to remove
     */
    removeRecentItem(id: string): void {
      this.recentItems = this.recentItems.filter((i: RecentItem) => i.id !== id);
      this.persistData();
    },

    /**
     * Clear all recent items.
     */
    clearRecentItems(): void {
      this.recentItems = [];
      this.persistData();
    },

    // ============================================
    // Favorites Actions
    // ============================================

    /**
     * Add an item to favorites.
     * @param item - Favorite item (without accessedAt)
     */
    addFavorite(item: Omit<RecentItem, 'accessedAt'>): void {
      if (this.favorites.some((f: RecentItem) => f.id === item.id)) {
        return;
      }

      const newItem: RecentItem = {
        ...item,
        accessedAt: new Date(),
      };

      this.favorites = [newItem, ...this.favorites];

      if (this.favorites.length > MAX_FAVORITES) {
        this.favorites = this.favorites.slice(0, MAX_FAVORITES);
      }

      this.persistData();
    },

    /**
     * Remove an item from favorites.
     * @param id - Item ID to remove
     */
    removeFavorite(id: string): void {
      this.favorites = this.favorites.filter((i: RecentItem) => i.id !== id);
      this.persistData();
    },

    /**
     * Check if an item is in favorites.
     * @param id - Item ID to check
     * @returns True if item is a favorite
     */
    isFavorite(id: string): boolean {
      return this.favorites.some((f: RecentItem) => f.id === id);
    },

    // ============================================
    // Persistence Actions
    // ============================================

    /**
     * Load persisted user data from storage.
     */
    async loadPersistedData(): Promise<void> {
      const storage = getStoreStorage();
      if (!storage) return;

      const data = storage.get<{
        preferences?: UserPreferences;
        recentItems?: RecentItem[];
        favorites?: RecentItem[];
      }>(STORAGE_KEY);

      if (data) {
        if (data.preferences) {
          this.preferences = { ...defaultUserPreferences, ...data.preferences };
        }
        if (data.recentItems) {
          this.recentItems = data.recentItems;
        }
        if (data.favorites) {
          this.favorites = data.favorites;
        }
      }

      // Apply persisted theme and language
      this.applyTheme(this.preferences.theme);
      this.applyLanguage(this.preferences.language);
    },

    /**
     * Persist user data to storage.
     */
    persistData(): void {
      const storage = getStoreStorage();
      if (!storage) return;

      storage.set(STORAGE_KEY, {
        preferences: this.preferences,
        recentItems: this.recentItems,
        favorites: this.favorites,
      });
    },

    // ============================================
    // Theme & Language Application
    // ============================================

    /**
     * Apply theme to document root element.
     * @param theme - Theme mode to apply
     */
    applyTheme(theme: UserTheme): void {
      if (typeof document === 'undefined') return;

      const root = document.documentElement;
      const isDark =
        theme === 'dark' ||
        (theme === 'system' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      root.classList.toggle('dark', isDark);
    },

    /**
     * Apply language to document lang attribute.
     * @param language - Language code
     */
    applyLanguage(language: string): void {
      if (typeof document === 'undefined') return;
      document.documentElement.lang = language;
    },
  },
});

// ============================================
// Composable Wrapper
// ============================================

/**
 * Vue composable for using user store.
 * Provides reactive computed refs and bound action methods.
 */
export function useUser() {
  const store = useUserStore();

  return {
    // State (reactive)
    currentUser: computed(() => store.currentUser),
    preferences: computed(() => store.preferences),
    recentItems: computed(() => store.recentItems),
    favorites: computed(() => store.favorites),
    isLoading: computed(() => store.isLoading),
    error: computed(() => store.error),

    // Computed
    isLoaded: computed(() => store.isLoaded),
    isAuthenticated: computed(() => store.isAuthenticated),
    displayName: computed(() => store.displayName),
    userName: computed(() => store.userName),
    avatar: computed(() => store.avatar),
    email: computed(() => store.email),
    roles: computed(() => store.roles),
    theme: computed(() => store.theme),
    language: computed(() => store.language),
    recentItemsCount: computed(() => store.recentItemsCount),
    favoritesCount: computed(() => store.favoritesCount),

    // Actions
    fetchCurrentUser: store.fetchCurrentUser.bind(store),
    updateProfile: store.updateProfile.bind(store),
    updatePreferences: store.updatePreferences.bind(store),
    resetPreferences: store.resetPreferences.bind(store),
    setTheme: store.setTheme.bind(store),
    setLanguage: store.setLanguage.bind(store),
    addRecentItem: store.addRecentItem.bind(store),
    removeRecentItem: store.removeRecentItem.bind(store),
    clearRecentItems: store.clearRecentItems.bind(store),
    addFavorite: store.addFavorite.bind(store),
    removeFavorite: store.removeFavorite.bind(store),
    isFavorite: store.isFavorite.bind(store),
    loadPersistedData: store.loadPersistedData.bind(store),
  };
}
