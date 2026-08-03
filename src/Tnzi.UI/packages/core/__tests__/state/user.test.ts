import { describe, it, expect, beforeEach, vi } from 'vitest';
import { UserStateManager, createInitialUserState } from '../../src/state/user';
import { defaultUserPreferences } from '../../src/state/types/user';
import type { StateDeps } from '../../src/state/types';
import type { HttpClient } from '../../src/http/http';
import type { StorageAdapter } from '../../src/adapters/storage';

// Mock service imports
vi.mock('../../src/services/identity/index', () => ({
  useProfileApi: vi.fn(() => ({
    get: vi.fn(),
    update: vi.fn(),
  })),
}));

import { useProfileApi } from '../../src/services/identity/index';

function createMockStorage(): StorageAdapter {
  const store = new Map<string, unknown>();
  return {
    getItem: (key: string) => (store.get(key) as string) ?? null,
    setItem: (key: string, value: string) => store.set(key, value),
    removeItem: (key: string) => store.delete(key),
    clear: () => store.clear(),
    get: <T>(key: string) => (store.get(key) ?? null) as T | null,
    set: <T>(key: string, value: T) => store.set(key, value),
    remove: (key: string) => store.delete(key),
    keys: () => Array.from(store.keys()),
    has: (key: string) => store.has(key),
  };
}

function createMockHttpClient(): HttpClient {
  return {
    setAccessToken: vi.fn(),
    getAccessToken: vi.fn().mockReturnValue(null),
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
    upload: vi.fn(),
    uploadFormData: vi.fn(),
    download: vi.fn(),
    resolveUrl: vi.fn(),
  } as unknown as HttpClient;
}

function createDeps(overrides?: Partial<StateDeps>): StateDeps {
  return {
    httpClient: createMockHttpClient(),
    storage: createMockStorage(),
    ...overrides,
  };
}

const mockUser = {
  id: '1',
  userName: 'john',
  nickname: 'John Doe',
  email: 'john@example.com',
  phoneNumber: '+1234567890',
  avatar: 'https://example.com/avatar.jpg',
  roles: ['admin'],
  isActive: true,
  creationTime: new Date().toISOString(),
};

describe('UserStateManager', () => {
  let user: UserStateManager;
  let deps: StateDeps;

  beforeEach(() => {
    vi.clearAllMocks();
    deps = createDeps();
    user = new UserStateManager(deps);
  });

  // ------------------------------------------
  // Initial state
  // ------------------------------------------

  describe('initial state', () => {
    it('should have no current user', () => {
      expect(user.currentUser).toBeNull();
    });

    it('should have default preferences', () => {
      expect(user.preferences).toBeDefined();
      expect(user.preferences.theme).toBe('auto');
      expect(user.preferences.language).toBeDefined();
    });

    it('should have empty recent items', () => {
      expect(user.recentItems).toEqual([]);
    });

    it('should have empty favorites', () => {
      expect(user.favorites).toEqual([]);
    });

    it('should not be loading', () => {
      expect(user.isLoading).toBe(false);
    });

    it('should have no error', () => {
      expect(user.error).toBeNull();
    });
  });

  // ------------------------------------------
  // Computed getters
  // ------------------------------------------

  describe('computed getters', () => {
    it('isLoaded should be false when no user', () => {
      expect(user.isLoaded).toBe(false);
    });

    it('isLoaded should be true when user is set', () => {
      user.currentUser = mockUser as any;
      expect(user.isLoaded).toBe(true);
    });

    it('isAuthenticated mirrors presence of currentUser', () => {
      expect(user.isAuthenticated).toBe(false);
      user.currentUser = mockUser as any;
      expect(user.isAuthenticated).toBe(true);
    });

    it('roles returns currentUser roles or empty array', () => {
      expect(user.roles).toEqual([]);
      user.currentUser = mockUser as any;
      expect(user.roles).toEqual(['admin']);
    });

    it('displayName should be empty when no user', () => {
      expect(user.displayName).toBe('');
    });

    it('displayName should prefer nickname', () => {
      user.currentUser = mockUser as any;
      expect(user.displayName).toBe('John Doe');
    });

    it('displayName should fallback to userName', () => {
      user.currentUser = { ...mockUser, nickname: undefined } as any;
      expect(user.displayName).toBe('john');
    });

    it('userName should return empty when no user', () => {
      expect(user.userName).toBe('');
    });

    it('userName should return current user name', () => {
      user.currentUser = mockUser as any;
      expect(user.userName).toBe('john');
    });

    it('avatar should return null when no user', () => {
      expect(user.avatar).toBeNull();
    });

    it('avatar should return user avatar', () => {
      user.currentUser = mockUser as any;
      expect(user.avatar).toBe('https://example.com/avatar.jpg');
    });

    it('email should return null when no user', () => {
      expect(user.email).toBeNull();
    });

    it('email should return user email', () => {
      user.currentUser = mockUser as any;
      expect(user.email).toBe('john@example.com');
    });

    it('theme should return preferences theme', () => {
      expect(user.theme).toBe(user.preferences.theme);
    });

    it('language should return preferences language', () => {
      expect(user.language).toBe(user.preferences.language);
    });

    it('recentItemsCount should return count', () => {
      expect(user.recentItemsCount).toBe(0);
      user.addRecentItem({ id: '1', title: 'Item 1', type: 'page', url: '/page/1' });
      expect(user.recentItemsCount).toBe(1);
    });

    it('favoritesCount should return count', () => {
      expect(user.favoritesCount).toBe(0);
      user.addFavorite({ id: '1', title: 'Fav 1', type: 'page', url: '/page/1' });
      expect(user.favoritesCount).toBe(1);
    });
  });

  // ------------------------------------------
  // fetchCurrentUser
  // ------------------------------------------

  describe('fetchCurrentUser', () => {
    it('should fetch and set current user', async () => {
      const mockApi = {
        get: vi.fn().mockResolvedValue({ succeeded: true, data: mockUser }),
        update: vi.fn(),
      };
      vi.mocked(useProfileApi).mockReturnValue(mockApi as any);

      await user.fetchCurrentUser();
      expect(user.currentUser).toEqual(mockUser);
      expect(user.isLoading).toBe(false);
    });

    it('should set error on failure', async () => {
      const mockApi = {
        get: vi.fn().mockResolvedValue({ succeeded: false, message: 'Not found' }),
        update: vi.fn(),
      };
      vi.mocked(useProfileApi).mockReturnValue(mockApi as any);

      await user.fetchCurrentUser();
      expect(user.error).toBe('Not found');
      expect(user.isLoading).toBe(false);
    });

    it('should handle exception', async () => {
      const mockApi = {
        get: vi.fn().mockRejectedValue(new Error('Network error')),
        update: vi.fn(),
      };
      vi.mocked(useProfileApi).mockReturnValue(mockApi as any);

      await user.fetchCurrentUser();
      expect(user.error).toBe('Network error');
      expect(user.isLoading).toBe(false);
    });

    it('should set isLoading during fetch', async () => {
      let resolvePromise: (value: any) => void;
      const mockApi = {
        get: vi.fn().mockReturnValue(new Promise(r => { resolvePromise = r; })),
        update: vi.fn(),
      };
      vi.mocked(useProfileApi).mockReturnValue(mockApi as any);

      const promise = user.fetchCurrentUser();
      expect(user.isLoading).toBe(true);
      resolvePromise!({ succeeded: true, data: mockUser });
      await promise;
      expect(user.isLoading).toBe(false);
    });
  });

  // ------------------------------------------
  // updateProfile
  // ------------------------------------------

  describe('updateProfile', () => {
    it('should update profile and set current user', async () => {
      const updatedUser = { ...mockUser, nickname: 'Updated Name' };
      const mockApi = {
        get: vi.fn(),
        update: vi.fn().mockResolvedValue({ succeeded: true, data: updatedUser }),
      };
      vi.mocked(useProfileApi).mockReturnValue(mockApi as any);

      const result = await user.updateProfile({ nickname: 'Updated Name' });
      expect(user.currentUser).toEqual(updatedUser);
      expect(result).toEqual(updatedUser);
    });

    it('should set error on failure', async () => {
      const mockApi = {
        get: vi.fn(),
        update: vi.fn().mockResolvedValue({ succeeded: false, message: 'Update failed' }),
      };
      vi.mocked(useProfileApi).mockReturnValue(mockApi as any);

      await expect(user.updateProfile({ nickname: 'X' })).rejects.toThrow('Update failed');
      expect(user.error).toBe('Update failed');
    });
  });

  // ------------------------------------------
  // Preferences
  // ------------------------------------------

  describe('preferences', () => {
    it('updatePreferences should merge partial preferences', () => {
      const originalTheme = user.preferences.theme;
      user.updatePreferences({ language: 'en-US' });
      expect(user.preferences.language).toBe('en-US');
      expect(user.preferences.theme).toBe(originalTheme);
    });

    it('updatePreferences should apply theme if provided', () => {
      const mockTheme = { applyTheme: vi.fn(), getResolvedTheme: vi.fn(), onSystemThemeChange: vi.fn() };
      const depsWithTheme = createDeps({ theme: mockTheme });
      const userWithTheme = new UserStateManager(depsWithTheme);

      userWithTheme.updatePreferences({ theme: 'dark' });
      expect(mockTheme.applyTheme).toHaveBeenCalledWith('dark');
    });

    it('resetPreferences should restore defaults', () => {
      user.updatePreferences({ language: 'en-US' });
      user.resetPreferences();
      expect(user.preferences.theme).toBe('auto');
    });

    it('resetPreferences should re-apply the default theme via theme adapter', () => {
      const mockTheme = { applyTheme: vi.fn(), getResolvedTheme: vi.fn(), onSystemThemeChange: vi.fn() };
      const userWithTheme = new UserStateManager(createDeps({ theme: mockTheme }));
      userWithTheme.resetPreferences();
      expect(mockTheme.applyTheme).toHaveBeenCalledWith(defaultUserPreferences.theme);
    });

    it('setTheme delegates to updatePreferences (applies theme + persists)', () => {
      const mockTheme = { applyTheme: vi.fn(), getResolvedTheme: vi.fn(), onSystemThemeChange: vi.fn() };
      const userWithTheme = new UserStateManager(createDeps({ theme: mockTheme }));
      userWithTheme.setTheme('dark');
      expect(userWithTheme.preferences.theme).toBe('dark');
      expect(mockTheme.applyTheme).toHaveBeenCalledWith('dark');
    });

    it('setLanguage updates the language preference', () => {
      user.setLanguage('en-US');
      expect(user.preferences.language).toBe('en-US');
    });
  });

  // ------------------------------------------
  // Recent items
  // ------------------------------------------

  describe('recent items', () => {
    const item1 = { id: '1', title: 'Page 1', type: 'page' as const, url: '/page/1' };
    const item2 = { id: '2', title: 'Page 2', type: 'page' as const, url: '/page/2' };

    it('addRecentItem should add item with accessedAt', () => {
      user.addRecentItem(item1);
      expect(user.recentItems).toHaveLength(1);
      expect(user.recentItems[0]!.id).toBe('1');
      expect(user.recentItems[0]!.accessedAt).toBeDefined();
    });

    it('addRecentItem should move existing item to front', () => {
      user.addRecentItem(item1);
      user.addRecentItem(item2);
      user.addRecentItem(item1);
      expect(user.recentItems).toHaveLength(2);
      expect(user.recentItems[0]!.id).toBe('1');
      expect(user.recentItems[1]!.id).toBe('2');
    });

    it('addRecentItem should cap at MAX_RECENT_ITEMS', () => {
      for (let i = 0; i < 55; i++) {
        user.addRecentItem({ id: `item-${i}`, title: `Item ${i}`, type: 'page', url: `/page/${i}` });
      }
      expect(user.recentItems.length).toBeLessThanOrEqual(50);
    });

    it('removeRecentItem should remove by id', () => {
      user.addRecentItem(item1);
      user.addRecentItem(item2);
      user.removeRecentItem('1');
      expect(user.recentItems).toHaveLength(1);
      expect(user.recentItems[0]!.id).toBe('2');
    });

    it('clearRecentItems should remove all', () => {
      user.addRecentItem(item1);
      user.addRecentItem(item2);
      user.clearRecentItems();
      expect(user.recentItems).toHaveLength(0);
    });
  });

  // ------------------------------------------
  // Favorites
  // ------------------------------------------

  describe('favorites', () => {
    const fav1 = { id: 'f1', title: 'Fav 1', type: 'page' as const, url: '/fav/1' };
    const fav2 = { id: 'f2', title: 'Fav 2', type: 'page' as const, url: '/fav/2' };

    it('addFavorite should add item', () => {
      user.addFavorite(fav1);
      expect(user.favorites).toHaveLength(1);
    });

    it('addFavorite should not duplicate', () => {
      user.addFavorite(fav1);
      user.addFavorite(fav1);
      expect(user.favorites).toHaveLength(1);
    });

    it('isFavorite should return true for existing favorite', () => {
      user.addFavorite(fav1);
      expect(user.isFavorite('f1')).toBe(true);
      expect(user.isFavorite('nonexistent')).toBe(false);
    });

    it('removeFavorite should remove by id', () => {
      user.addFavorite(fav1);
      user.addFavorite(fav2);
      user.removeFavorite('f1');
      expect(user.favorites).toHaveLength(1);
      expect(user.isFavorite('f1')).toBe(false);
    });
  });

  // ------------------------------------------
  // Persistence
  // ------------------------------------------

  describe('persistence', () => {
    it('persistData should write to storage', () => {
      user.updatePreferences({ language: 'en-US' });
      user.persistData();
      expect(deps.storage.has('tnzi:user:preferences')).toBe(true);
    });

    it('loadPersistedData should restore from storage', () => {
      user.updatePreferences({ language: 'en-US' });
      user.addRecentItem({ id: '1', title: 'Test', type: 'page', url: '/test' });
      user.persistData();

      // Create a new manager with the same storage
      const user2 = new UserStateManager(deps);
      user2.loadPersistedData();
      expect(user2.preferences.language).toBe('en-US');
      expect(user2.recentItems).toHaveLength(1);
    });

    it("loadPersistedData should migrate a legacy 'system' theme preference to 'auto'", () => {
      // Shape written by a pre-unification build.
      deps.storage.set('tnzi:user:preferences', { theme: 'system', language: 'en-US' });

      const user2 = new UserStateManager(deps);
      user2.loadPersistedData();

      expect(user2.preferences.theme).toBe('auto');
      expect(user2.preferences.language).toBe('en-US');
    });

    it('loadPersistedData should fall back to the default theme for a junk value', () => {
      deps.storage.set('tnzi:user:preferences', { theme: 'chartreuse' });

      const user2 = new UserStateManager(deps);
      user2.loadPersistedData();

      expect(user2.preferences.theme).toBe('auto');
    });
  });

  // ------------------------------------------
  // Cleanup
  // ------------------------------------------

  describe('cleanup', () => {
    it('clear should reset all state', () => {
      user.currentUser = mockUser as any;
      user.addRecentItem({ id: '1', title: 'Test', type: 'page', url: '/test' });
      user.addFavorite({ id: '1', title: 'Fav', type: 'page', url: '/fav' });
      user.error = 'some error';

      user.clear();
      expect(user.currentUser).toBeNull();
      expect(user.recentItems).toEqual([]);
      expect(user.favorites).toEqual([]);
      expect(user.error).toBeNull();
    });

    it('clear should remove persisted data', () => {
      user.addRecentItem({ id: '1', title: 'Test', type: 'page', url: '/test' });
      user.persistData();

      user.clear();
      expect(deps.storage.has('tnzi:user:preferences')).toBe(false);
      expect(deps.storage.has('tnzi:user:recents')).toBe(false);
      expect(deps.storage.has('tnzi:user:favorites')).toBe(false);
    });
  });

  // ------------------------------------------
  // createInitialUserState
  // ------------------------------------------

  describe('createInitialUserState', () => {
    it('should return clean initial state', () => {
      const state = createInitialUserState();
      expect(state.currentUser).toBeNull();
      expect(state.recentItems).toEqual([]);
      expect(state.favorites).toEqual([]);
      expect(state.isLoading).toBe(false);
      expect(state.error).toBeNull();
    });
  });
});
