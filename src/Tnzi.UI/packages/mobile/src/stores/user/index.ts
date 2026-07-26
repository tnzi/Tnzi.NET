/**
 * @tnzi/mobile/stores/user
 *
 * User store - thin Pinia wrapper delegating to core UserStateManager.
 * All business logic lives in UserStateManager; this store only proxies reactive state.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
import { UserStateManager } from '@tnzi/core/state';
import type { StateDeps } from '@tnzi/core/state';
import { getStoreHttpClient, getStoreStorage } from '../factory';

// ============================================
// UserStateManager Singleton
// ============================================

let _manager: UserStateManager | null = null;

function getManager(): UserStateManager {
  if (!_manager) {
    const deps = {
      httpClient: getStoreHttpClient(),
      storage: getStoreStorage(),
    } as unknown as StateDeps;
    _manager = new UserStateManager(deps);
  }
  return _manager;
}

// ============================================
// User Store Definition
// ============================================

export const useUserStore = defineStore('user', () => {
  // --- State (proxied from manager) ---
  const currentUser = computed(() => getManager().currentUser);
  const preferences = computed(() => getManager().preferences);
  const recentItems = computed(() => getManager().recentItems);
  const favorites = computed(() => getManager().favorites);
  const isLoading = computed(() => getManager().isLoading);
  const error = computed(() => getManager().error);

  // --- Computed getters ---
  const isLoaded = computed(() => getManager().isLoaded);
  const displayName = computed(() => getManager().displayName);
  const userName = computed(() => getManager().userName);
  const avatar = computed(() => getManager().avatar);
  const email = computed(() => getManager().email);
  const theme = computed(() => getManager().theme);
  const language = computed(() => getManager().language);
  const recentItemsCount = computed(() => getManager().recentItemsCount);
  const favoritesCount = computed(() => getManager().favoritesCount);

  return {
    // State
    currentUser, preferences, recentItems, favorites, isLoading, error,
    // Getters
    isLoaded, displayName, userName, avatar, email, theme, language,
    recentItemsCount, favoritesCount,
    // Profile
    fetchCurrentUser: () => getManager().fetchCurrentUser(),
    updateProfile: (data: Parameters<UserStateManager['updateProfile']>[0]) => getManager().updateProfile(data),
    // Preferences
    updatePreferences: (prefs: Parameters<UserStateManager['updatePreferences']>[0]) => getManager().updatePreferences(prefs),
    resetPreferences: () => getManager().resetPreferences(),
    // Recent items
    addRecentItem: (item: Parameters<UserStateManager['addRecentItem']>[0]) => getManager().addRecentItem(item),
    removeRecentItem: (id: string) => getManager().removeRecentItem(id),
    clearRecentItems: () => getManager().clearRecentItems(),
    // Favorites
    addFavorite: (item: Parameters<UserStateManager['addFavorite']>[0]) => getManager().addFavorite(item),
    removeFavorite: (id: string) => getManager().removeFavorite(id),
    isFavorite: (id: string) => getManager().isFavorite(id),
    // Persistence
    loadPersistedData: () => getManager().loadPersistedData(),
  };
});

// ============================================
// Composable Wrapper
// ============================================

export function useUser() {
  const store = useUserStore();

  return {
    // State
    currentUser: computed(() => store.currentUser),
    preferences: computed(() => store.preferences),
    recentItems: computed(() => store.recentItems),
    favorites: computed(() => store.favorites),
    isLoading: computed(() => store.isLoading),
    error: computed(() => store.error),
    // Computed
    isLoaded: computed(() => store.isLoaded),
    displayName: computed(() => store.displayName),
    userName: computed(() => store.userName),
    avatar: computed(() => store.avatar),
    email: computed(() => store.email),
    theme: computed(() => store.theme),
    language: computed(() => store.language),
    recentItemsCount: computed(() => store.recentItemsCount),
    favoritesCount: computed(() => store.favoritesCount),
    // Actions
    fetchCurrentUser: store.fetchCurrentUser,
    updateProfile: store.updateProfile,
    updatePreferences: store.updatePreferences,
    resetPreferences: store.resetPreferences,
    addRecentItem: store.addRecentItem,
    removeRecentItem: store.removeRecentItem,
    clearRecentItems: store.clearRecentItems,
    addFavorite: store.addFavorite,
    removeFavorite: store.removeFavorite,
    isFavorite: store.isFavorite,
    loadPersistedData: store.loadPersistedData,
  };
}
