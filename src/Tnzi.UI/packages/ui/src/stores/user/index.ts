/**
 * @tnzi/ui/stores/user
 *
 * User preferences and profile store — thin Pinia wrapper delegating to core
 * UserStateManager. All business logic (HTTP fetch, persistence, preferences)
 * lives in UserStateManager; this store only proxies reactive state.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
import { UserStateManager } from '@tnzi/core/state';
import type { StateDeps, UserPreferences, UserTheme, RecentItem } from '@tnzi/core/state';
import { createLocalStorageAdapter } from '@tnzi/core/adapters/storage';
import type { UserDto, UpdateUserDto } from '@tnzi/core/services/identity';
import { getStoreHttpClient, getStoreStorage } from '../factory';
import { createThemeAdapter } from '../../adapters/theme';

// ============================================
// UserStateManager Singleton
// ============================================

let _manager: UserStateManager | null = null;

function getManager(): UserStateManager {
  if (!_manager) {
    const deps: StateDeps = {
      httpClient: getStoreHttpClient(),
      storage: getStoreStorage() ?? createLocalStorageAdapter(),
      theme: createThemeAdapter(),
    };
    _manager = new UserStateManager(deps);
  }
  return _manager;
}

// ============================================
// User Store Definition
// ============================================

export const useUserStore = defineStore('user', () => {
  const m = computed(() => getManager());

  // --- Reactive state (proxied from manager) ---
  const currentUser = computed(() => m.value.currentUser);
  const preferences = computed(() => m.value.preferences);
  const recentItems = computed(() => m.value.recentItems);
  const favorites = computed(() => m.value.favorites);
  const isLoading = computed(() => m.value.isLoading);
  const error = computed(() => m.value.error);

  // --- Getters ---
  const isLoaded = computed(() => m.value.isLoaded);
  const isAuthenticated = computed(() => m.value.isAuthenticated);
  // Store keeps the 'Guest' fallback for an empty display name (public API).
  const displayName = computed(() => m.value.displayName || 'Guest');
  const userName = computed(() => m.value.userName);
  const avatar = computed(() => m.value.avatar);
  const email = computed(() => m.value.email);
  const roles = computed(() => m.value.roles);
  const theme = computed(() => m.value.theme);
  const language = computed(() => m.value.language);
  const recentItemsCount = computed(() => m.value.recentItemsCount);
  const favoritesCount = computed(() => m.value.favoritesCount);

  // --- Profile actions (delegate to manager) ---
  async function fetchCurrentUser(): Promise<void> {
    return getManager().fetchCurrentUser();
  }

  async function updateProfile(data: UpdateUserDto): Promise<UserDto> {
    return getManager().updateProfile(data);
  }

  // --- Preferences actions ---
  async function updatePreferences(prefs: Partial<UserPreferences>): Promise<void> {
    getManager().updatePreferences(prefs);
  }

  function resetPreferences(): void {
    getManager().resetPreferences();
  }

  function setTheme(t: UserTheme): void {
    getManager().setTheme(t);
  }

  function setLanguage(lang: string): void {
    getManager().setLanguage(lang as UserPreferences['language']);
  }

  // --- Recent items actions ---
  function addRecentItem(item: Omit<RecentItem, 'accessedAt'>): void {
    getManager().addRecentItem(item);
  }

  function removeRecentItem(id: string): void {
    getManager().removeRecentItem(id);
  }

  function clearRecentItems(): void {
    getManager().clearRecentItems();
  }

  // --- Favorites actions ---
  function addFavorite(item: Omit<RecentItem, 'accessedAt'>): void {
    getManager().addFavorite(item);
  }

  function removeFavorite(id: string): void {
    getManager().removeFavorite(id);
  }

  function isFavorite(id: string): boolean {
    return getManager().isFavorite(id);
  }

  // --- Persistence actions ---
  async function loadPersistedData(): Promise<void> {
    getManager().loadPersistedData();
  }

  function persistData(): void {
    getManager().persistData();
  }

  return {
    // State
    currentUser, preferences, recentItems, favorites, isLoading, error,
    // Getters
    isLoaded, isAuthenticated, displayName, userName, avatar, email, roles,
    theme, language, recentItemsCount, favoritesCount,
    // Profile actions
    fetchCurrentUser, updateProfile,
    // Preferences actions
    updatePreferences, resetPreferences, setTheme, setLanguage,
    // Recent items actions
    addRecentItem, removeRecentItem, clearRecentItems,
    // Favorites actions
    addFavorite, removeFavorite, isFavorite,
    // Persistence actions
    loadPersistedData, persistData,
  };
});

// ============================================
// Runtime Reset
// ============================================

/**
 * Reset user runtime to uninitialized state.
 * Nulls the internal UserStateManager singleton.
 * Useful for SSR isolation or test teardown.
 */
export function resetUserRuntime(): void {
  _manager = null;
}

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
