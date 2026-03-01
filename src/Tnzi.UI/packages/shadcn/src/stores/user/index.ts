/**
 * @tnzi/shadcn/stores/user
 *
 * User preferences and profile store implementation using store factory.
 * Manages user profile data and UI preferences.
 */

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
import { createStore, getStoreHttpClient } from '../factory';

// ============================================
// Constants
// ============================================

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

export const useUserStore = createStore<UserState>({
  id: 'user',
  state: defaultState,

  getters: {
    isLoaded: (state: UserState): boolean => state.currentUser !== null,

    isAuthenticated: (state: UserState): boolean => state.currentUser !== null,

    displayName: (state: UserState): string => {
      if (!state.currentUser) return 'Guest';
      return state.currentUser.nickname ?? state.currentUser.userName ?? 'Guest';
    },

    userName: (state: UserState): string => state.currentUser?.userName ?? '',

    avatar: (state: UserState): string | null => state.currentUser?.avatar ?? null,

    email: (state: UserState): string | null => state.currentUser?.email ?? null,

    roles: (state: UserState): string[] => state.currentUser?.roles ?? [],

    theme: (state: UserState): UserTheme => state.preferences.theme,

    language: (state: UserState): string => state.preferences.language,

    recentItemsCount: (state: UserState): number => state.recentItems.length,

    favoritesCount: (state: UserState): number => state.favorites.length,
  },

  actions: {
    // Profile actions
    async fetchCurrentUser(): Promise<void> {
      this.$state.isLoading = true;
      this.$state.error = null;

      try {
        const client = getStoreHttpClient();
        const api = useProfileApi(client);
        const result = await api.get();
        if (result.succeeded && result.data) {
          this.$state.currentUser = result.data;
        }
      } catch (error) {
        this.$state.error = error instanceof Error ? error.message : 'Failed to fetch user';
        throw error;
      } finally {
        this.$state.isLoading = false;
      }
    },

    async updateProfile(data: UpdateUserDto): Promise<UserDto> {
      this.$state.isLoading = true;
      this.$state.error = null;

      try {
        const client = getStoreHttpClient();
        const api = useProfileApi(client);
        const result = await api.update(data);
        if (result.succeeded && result.data) {
          this.$state.currentUser = result.data;
          return result.data;
        }
        throw new Error('Update failed');
      } catch (error) {
        this.$state.error = error instanceof Error ? error.message : 'Update failed';
        throw error;
      } finally {
        this.$state.isLoading = false;
      }
    },

    // Preferences actions
    async updatePreferences(preferences: Partial<UserPreferences>): Promise<void> {
      this.$state.preferences = { ...this.$state.preferences, ...preferences };

      if (preferences.theme !== undefined) {
        this.applyTheme(this.$state.preferences.theme);
      }
      if (preferences.language !== undefined) {
        this.applyLanguage(this.$state.preferences.language);
      }
    },

    resetPreferences(): void {
      this.$state.preferences = { ...defaultUserPreferences };
      this.applyTheme(this.$state.preferences.theme);
      this.applyLanguage(this.$state.preferences.language);
    },

    setTheme(theme: UserTheme): void {
      this.$state.preferences.theme = theme;
      this.applyTheme(theme);
    },

    setLanguage(language: string): void {
      this.$state.preferences.language = language as UserPreferences['language'];
      this.applyLanguage(language);
    },

    // Recent items actions
    addRecentItem(item: Omit<RecentItem, 'accessedAt'>): void {
      const newItem: RecentItem = {
        ...item,
        accessedAt: new Date(),
      };

      this.$state.recentItems = this.$state.recentItems.filter((i: RecentItem) => i.id !== item.id);
      this.$state.recentItems = [newItem, ...this.$state.recentItems];

      if (this.$state.recentItems.length > MAX_RECENT_ITEMS) {
        this.$state.recentItems = this.$state.recentItems.slice(0, MAX_RECENT_ITEMS);
      }
    },

    removeRecentItem(id: string): void {
      this.$state.recentItems = this.$state.recentItems.filter((i: RecentItem) => i.id !== id);
    },

    clearRecentItems(): void {
      this.$state.recentItems = [];
    },

    // Favorites actions
    addFavorite(item: Omit<RecentItem, 'accessedAt'>): void {
      if (this.$state.favorites.some((f: RecentItem) => f.id === item.id)) {
        return;
      }

      const newItem: RecentItem = {
        ...item,
        accessedAt: new Date(),
      };

      this.$state.favorites = [newItem, ...this.$state.favorites];

      if (this.$state.favorites.length > MAX_FAVORITES) {
        this.$state.favorites = this.$state.favorites.slice(0, MAX_FAVORITES);
      }
    },

    removeFavorite(id: string): void {
      this.$state.favorites = this.$state.favorites.filter((i: RecentItem) => i.id !== id);
    },

    isFavorite(id: string): boolean {
      return this.$state.favorites.some((f: RecentItem) => f.id === id);
    },

    // Persistence actions (handled by factory's persist plugin)
    async loadPersistedData(): Promise<void> {
      if (this.$state.preferences) {
        this.applyTheme(this.$state.preferences.theme);
        this.applyLanguage(this.$state.preferences.language);
      }
    },

    async persistData(): Promise<void> {
      // Handled automatically by the factory's persist plugin
    },

    // Theme application
    applyTheme(theme: UserTheme): void {
      if (typeof document === 'undefined') return;

      const root = document.documentElement;
      const isDark =
        theme === 'dark' ||
        (theme === 'system' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      root.classList.toggle('dark', isDark);
    },

    // Language application
    applyLanguage(language: string): void {
      if (typeof document === 'undefined') return;
      document.documentElement.lang = language;
    },
  },

  persist: {
    key: 'user_data',
    paths: ['preferences', 'recentItems', 'favorites'],
  },
});

// ============================================
// Export as Composable
// ============================================

/**
 * Vue composable for using user store.
 * The underlying PiniaStoreWrapper Proxy exposes state/getters/actions directly.
 */
export function useUser() {
  const store = useUserStore();

  return {
    // State (typed via FlatStoreInstance<UserState>)
    currentUser: computed(() => store.currentUser),
    preferences: computed(() => store.preferences),
    recentItems: computed(() => store.recentItems),
    favorites: computed(() => store.favorites),
    isLoading: computed(() => store.isLoading),
    error: computed(() => store.error),

    // Computed (getters exposed via Proxy)
    isLoaded: computed(() => store.isLoaded as boolean),
    isAuthenticated: computed(() => store.isAuthenticated as boolean),
    displayName: computed(() => store.displayName as string),
    userName: computed(() => store.userName as string),
    avatar: computed(() => store.avatar as string | null),
    email: computed(() => store.email as string | null),
    roles: computed(() => store.roles as string[]),
    theme: computed(() => store.theme as UserTheme),
    language: computed(() => store.language as string),
    recentItemsCount: computed(() => store.recentItemsCount as number),
    favoritesCount: computed(() => store.favoritesCount as number),

    // Actions (bound to store for proper 'this' context)
    fetchCurrentUser: (store.fetchCurrentUser as () => Promise<void>).bind(store),
    updateProfile: (store.updateProfile as (data: UpdateUserDto) => Promise<UserDto>).bind(store),
    updatePreferences: (store.updatePreferences as (prefs: Partial<UserPreferences>) => Promise<void>).bind(store),
    resetPreferences: (store.resetPreferences as () => void).bind(store),
    setTheme: (store.setTheme as (theme: UserTheme) => void).bind(store),
    setLanguage: (store.setLanguage as (language: string) => void).bind(store),
    addRecentItem: (store.addRecentItem as (item: Omit<RecentItem, 'accessedAt'>) => void).bind(store),
    removeRecentItem: (store.removeRecentItem as (id: string) => void).bind(store),
    clearRecentItems: (store.clearRecentItems as () => void).bind(store),
    addFavorite: (store.addFavorite as (item: Omit<RecentItem, 'accessedAt'>) => void).bind(store),
    removeFavorite: (store.removeFavorite as (id: string) => void).bind(store),
    isFavorite: (store.isFavorite as (id: string) => boolean).bind(store),
    loadPersistedData: (store.loadPersistedData as () => Promise<void>).bind(store),
    persistData: (store.persistData as () => Promise<void>).bind(store),
  };
}
