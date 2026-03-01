/**
 * @tnzi/naive-ui/stores/user
 *
 * User preferences and profile store using Pinia defineStore directly.
 * Manages user profile data, UI preferences, recent items, and favorites.
 */

import { defineStore } from 'pinia';
import { computed } from 'vue';
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
// User Store Definition
// ============================================

const defaultState = (): UserState => ({
  currentUser: null,
  preferences: { ...defaultUserPreferences },
  recentItems: [],
  favorites: [],
  isLoading: false,
  error: null,
});

export const useUserStore = defineStore('user', {
  state: defaultState,

  getters: {
    isLoaded: (state): boolean => state.currentUser !== null,

    isAuthenticated: (state): boolean => state.currentUser !== null,

    displayName: (state): string => {
      if (!state.currentUser) return 'Guest';
      return state.currentUser.nickname ?? state.currentUser.userName ?? 'Guest';
    },

    userName: (state): string => state.currentUser?.userName ?? '',

    avatar: (state): string | null => state.currentUser?.avatar ?? null,

    email: (state): string | null => state.currentUser?.email ?? null,

    roles: (state): string[] => state.currentUser?.roles ?? [],

    theme: (state): UserTheme => state.preferences.theme,

    language: (state): string => state.preferences.language,

    recentItemsCount: (state): number => state.recentItems.length,

    favoritesCount: (state): number => state.favorites.length,
  },

  actions: {
    // ---- Profile actions ----

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

    // ---- Preferences actions ----

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

    resetPreferences(): void {
      this.preferences = { ...defaultUserPreferences };
      this.applyTheme(this.preferences.theme);
      this.applyLanguage(this.preferences.language);
      this.persistData();
    },

    setTheme(theme: UserTheme): void {
      this.preferences.theme = theme;
      this.applyTheme(theme);
      this.persistData();
    },

    setLanguage(language: string): void {
      this.preferences.language = language as UserPreferences['language'];
      this.applyLanguage(language);
      this.persistData();
    },

    // ---- Recent items actions ----

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

    removeRecentItem(id: string): void {
      this.recentItems = this.recentItems.filter((i: RecentItem) => i.id !== id);
      this.persistData();
    },

    clearRecentItems(): void {
      this.recentItems = [];
      this.persistData();
    },

    // ---- Favorites actions ----

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

    removeFavorite(id: string): void {
      this.favorites = this.favorites.filter((i: RecentItem) => i.id !== id);
      this.persistData();
    },

    isFavorite(id: string): boolean {
      return this.favorites.some((f: RecentItem) => f.id === id);
    },

    // ---- Persistence actions ----

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

      this.applyTheme(this.preferences.theme);
      this.applyLanguage(this.preferences.language);
    },

    persistData(): void {
      const storage = getStoreStorage();
      if (!storage) return;

      storage.set(STORAGE_KEY, {
        preferences: this.preferences,
        recentItems: this.recentItems,
        favorites: this.favorites,
      });
    },

    // ---- Internal helpers ----

    applyTheme(theme: UserTheme): void {
      if (typeof document === 'undefined') return;

      const root = document.documentElement;
      const isDark =
        theme === 'dark' ||
        (theme === 'system' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      root.classList.toggle('dark', isDark);
    },

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
 * Vue composable for using the user store.
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
    persistData: store.persistData.bind(store),
  };
}
