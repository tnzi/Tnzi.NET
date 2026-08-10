/**
 * @tnzi/core/state/user
 *
 * User preferences state manager - pure logic layer.
 */

import { reactive } from 'vue';
import type { UserState, UserPreferences, RecentItem } from './types/user';
import { defaultUserPreferences } from './types/user';
import type { UserDto, UpdateUserDto } from '../services/identity/types';
import { useProfileApi } from '../services/identity/index';
import type { StateDeps } from './types/deps';
import { normalizeThemeMode } from '../types/theme';

// ============================================
// Initial state
// ============================================

export function createInitialUserState(): UserState {
  return {
    currentUser: null,
    preferences: { ...defaultUserPreferences },
    recentItems: [],
    favorites: [],
    isLoading: false,
    error: null,
  };
}

// ============================================
// Storage key constants
// ============================================

const STORAGE_KEY_PREFERENCES = 'tnzi:user:preferences';
const STORAGE_KEY_RECENTS = 'tnzi:user:recents';
const STORAGE_KEY_FAVORITES = 'tnzi:user:favorites';

const MAX_RECENT_ITEMS = 50;
const MAX_FAVORITES = 50;

// ============================================
// Debounce utility
// ============================================

function debounce<T extends (...args: any[]) => void>(fn: T, delay: number): (...args: Parameters<T>) => void {
  let timerId: ReturnType<typeof setTimeout> | null = null;
  return (...args: Parameters<T>) => {
    if (timerId !== null) clearTimeout(timerId);
    timerId = setTimeout(() => {
      timerId = null;
      fn(...args);
    }, delay);
  };
}

// ============================================
// UserStateManager
// ============================================

export class UserStateManager {
  // State
  currentUser: UserDto | null = null;
  preferences: UserPreferences = { ...defaultUserPreferences };
  recentItems: RecentItem[] = [];
  favorites: RecentItem[] = [];
  isLoading = false;
  error: string | null = null;

  /** Debounced persist function to avoid frequent storage writes */
  private readonly _debouncedPersist: () => void;

  constructor(private readonly deps: StateDeps) {
    this._debouncedPersist = debounce(() => this._persistDataImmediate(), 300);
    return reactive(this) as this;
  }

  // ============================================
  // Getters
  // ============================================

  get isLoaded(): boolean {
    return this.currentUser !== null;
  }

  get isAuthenticated(): boolean {
    return this.currentUser !== null;
  }

  get roles(): string[] {
    return this.currentUser?.roles ?? [];
  }

  get displayName(): string {
    return this.currentUser?.nickname ?? this.currentUser?.userName ?? '';
  }

  get userName(): string {
    return this.currentUser?.userName ?? '';
  }

  get avatar(): string | null {
    return this.currentUser?.avatar ?? null;
  }

  get email(): string | null {
    return this.currentUser?.email ?? null;
  }

  get theme(): UserPreferences['theme'] {
    return this.preferences.theme;
  }

  get language(): UserPreferences['language'] {
    return this.preferences.language;
  }

  get recentItemsCount(): number {
    return this.recentItems.length;
  }

  get favoritesCount(): number {
    return this.favorites.length;
  }

  // ============================================
  // Actions
  // ============================================

  async fetchCurrentUser(): Promise<void> {
    this.isLoading = true;
    this.error = null;

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.get();
      if (result.succeeded && result.data) {
        this.currentUser = result.data;
      } else {
        this.error = result.message ?? 'Failed to fetch user';
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Failed to fetch user';
    } finally {
      this.isLoading = false;
    }
  }

  updatePreferences(preferences: Partial<UserPreferences>): void {
    this.preferences = { ...this.preferences, ...preferences };

    // Apply theme
    if (preferences.theme && this.deps.theme) {
      this.deps.theme.applyTheme(preferences.theme);
    }

    // Apply language to document root
    if (preferences.language !== undefined) {
      this._applyLanguage(preferences.language);
    }

    this._debouncedPersist();
  }

  resetPreferences(): void {
    this.preferences = { ...defaultUserPreferences };
    this.deps.theme?.applyTheme(this.preferences.theme);
    this._applyLanguage(this.preferences.language);
    this._debouncedPersist();
  }

  /** Set the UI theme preference (applies theme + persists). */
  setTheme(theme: UserPreferences['theme']): void {
    this.updatePreferences({ theme });
  }

  /** Set the language preference (applies language to DOM + persists). */
  setLanguage(language: UserPreferences['language']): void {
    this.updatePreferences({ language });
  }

  /** Apply the language to the document root element's `lang` attribute. */
  private _applyLanguage(language: string): void {
    if (typeof document !== 'undefined') {
      document.documentElement.lang = language;
    }
  }

  async updateProfile(data: UpdateUserDto): Promise<UserDto> {
    this.isLoading = true;

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.update(data);
      if (!result.succeeded || !result.data) {
        throw new Error(result.message ?? 'Update failed');
      }
      this.currentUser = result.data;
      return result.data;
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Update failed';
      throw error;
    } finally {
      this.isLoading = false;
    }
  }

  // ============================================
  // Recent items & favorites
  // ============================================

  addRecentItem(item: Omit<RecentItem, 'accessedAt'>): void {
    // Remove existing item with same ID
    this.recentItems = this.recentItems.filter(i => i.id !== item.id);
    // Add to head
    this.recentItems.unshift({ ...item, accessedAt: new Date() });
    // Limit count
    if (this.recentItems.length > MAX_RECENT_ITEMS) {
      this.recentItems = this.recentItems.slice(0, MAX_RECENT_ITEMS);
    }
    this._debouncedPersist();
  }

  removeRecentItem(id: string): void {
    this.recentItems = this.recentItems.filter(i => i.id !== id);
    this._debouncedPersist();
  }

  clearRecentItems(): void {
    this.recentItems = [];
    this._debouncedPersist();
  }

  addFavorite(item: Omit<RecentItem, 'accessedAt'>): void {
    if (this.isFavorite(item.id)) return;
    this.favorites.push({ ...item, accessedAt: new Date() });
    // Cap to avoid unbounded growth in persisted storage (drops oldest).
    if (this.favorites.length > MAX_FAVORITES) {
      this.favorites = this.favorites.slice(-MAX_FAVORITES);
    }
    this._debouncedPersist();
  }

  removeFavorite(id: string): void {
    this.favorites = this.favorites.filter(i => i.id !== id);
    this._debouncedPersist();
  }

  isFavorite(id: string): boolean {
    return this.favorites.some(i => i.id === id);
  }

  // ============================================
  // Persistence
  // ============================================

  loadPersistedData(): void {
    const prefs = this.deps.storage.get<UserPreferences>(STORAGE_KEY_PREFERENCES);
    const recents = this.deps.storage.get<RecentItem[]>(STORAGE_KEY_RECENTS);
    const favs = this.deps.storage.get<RecentItem[]>(STORAGE_KEY_FAVORITES);

    if (prefs) {
      // Spreading persisted prefs straight in would let a legacy `theme: 'system'`
      // (written before the 'system' → 'auto' unification) escape the declared
      // union and reach `deps.theme.applyTheme`, which would resolve it as
      // "not dark" and quietly strand the user in light mode.
      this.preferences = {
        ...defaultUserPreferences,
        ...prefs,
        theme: normalizeThemeMode(prefs.theme, defaultUserPreferences.theme),
      };
    }
    if (recents) this.recentItems = recents;
    if (favs) this.favorites = favs;
  }

  /** Persist data immediately (use _debouncedPersist for frequent operations) */
  persistData(): void {
    this._persistDataImmediate();
  }

  private _persistDataImmediate(): void {
    this.deps.storage.set(STORAGE_KEY_PREFERENCES, this.preferences);
    this.deps.storage.set(STORAGE_KEY_RECENTS, this.recentItems);
    this.deps.storage.set(STORAGE_KEY_FAVORITES, this.favorites);
  }

  // ============================================
  // Cleanup
  // ============================================

  /**
   * Clear all user data including persisted storage.
   * Should be called on logout to prevent data leakage between users.
   */
  clear(): void {
    Object.assign(this, createInitialUserState());
    this.clearPersistedData();
  }

  /** Remove all persisted user data from storage */
  private clearPersistedData(): void {
    this.deps.storage.remove(STORAGE_KEY_PREFERENCES);
    this.deps.storage.remove(STORAGE_KEY_RECENTS);
    this.deps.storage.remove(STORAGE_KEY_FAVORITES);
  }
}
