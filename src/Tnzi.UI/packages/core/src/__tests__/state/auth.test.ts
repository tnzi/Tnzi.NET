import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AuthStateManager, createInitialAuthState } from '../../state/auth';
import type { StateDeps } from '../../state/types';
import type { HttpClient } from '../../http/http';
import type { StorageAdapter } from '../../adapters/storage';

// Mock service imports
vi.mock('../../services/identity/index', () => ({
  useAuthApi: () => ({
    loginWithRefreshToken: vi.fn(),
    refreshToken: vi.fn(),
    logout: vi.fn(),
  }),
  useProfileApi: () => ({
    get: vi.fn(),
    update: vi.fn(),
    changePassword: vi.fn(),
  }),
}));

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

describe('AuthStateManager', () => {
  let auth: AuthStateManager;
  let deps: StateDeps;

  beforeEach(() => {
    deps = createDeps();
    auth = new AuthStateManager(deps);
  });

  // ------------------------------------------
  // Initial state
  // ------------------------------------------

  describe('initial state', () => {
    it('should start unauthenticated', () => {
      expect(auth.isAuthenticated).toBe(false);
      expect(auth.accessToken).toBeNull();
      expect(auth.refreshToken).toBeNull();
      expect(auth.user).toBeNull();
    });

    it('should have no error', () => {
      expect(auth.error).toBeNull();
    });

    it('should not be refreshing', () => {
      expect(auth.isRefreshing).toBe(false);
    });
  });

  // ------------------------------------------
  // Computed getters
  // ------------------------------------------

  describe('computed getters', () => {
    it('isLoggedIn should be false when not authenticated', () => {
      expect(auth.isLoggedIn).toBe(false);
    });

    it('isLoggedIn should be true when authenticated with token', () => {
      auth.isAuthenticated = true;
      auth.accessToken = 'token';
      expect(auth.isLoggedIn).toBe(true);
    });

    it('userName should return empty string when no user', () => {
      expect(auth.userName).toBe('');
    });

    it('displayName should return Guest when no user', () => {
      expect(auth.displayName).toBe('Guest');
    });

    it('displayName should prefer nickname', () => {
      auth.user = {
        id: '1',
        userName: 'john',
        nickname: 'John Doe',
        email: null,
        phoneNumber: null,
        avatar: null,
        roles: [],
        permissions: [],
      };
      expect(auth.displayName).toBe('John Doe');
    });

    it('isTokenExpired should return true when no expiry', () => {
      expect(auth.isTokenExpired).toBe(true);
    });

    it('isTokenExpired should return false for future expiry', () => {
      auth.tokenExpiry = new Date(Date.now() + 3600000);
      expect(auth.isTokenExpired).toBe(false);
    });

    it('isTokenExpired should return true for past expiry', () => {
      auth.tokenExpiry = new Date(Date.now() - 1000);
      expect(auth.isTokenExpired).toBe(true);
    });

    it('tokenExpiresIn should return 0 when no expiry', () => {
      expect(auth.tokenExpiresIn).toBe(0);
    });

    it('tokenExpiresIn should return seconds until expiry', () => {
      auth.tokenExpiry = new Date(Date.now() + 60000);
      expect(auth.tokenExpiresIn).toBeGreaterThan(55);
      expect(auth.tokenExpiresIn).toBeLessThanOrEqual(60);
    });
  });

  // ------------------------------------------
  // Permission checking
  // ------------------------------------------

  describe('permission checking', () => {
    beforeEach(() => {
      auth.roles = ['admin', 'editor'];
      auth.permissions = ['read', 'write', 'delete'];
    });

    it('hasRole should return true for existing role', () => {
      expect(auth.hasRole('admin')).toBe(true);
    });

    it('hasRole should return false for non-existing role', () => {
      expect(auth.hasRole('superadmin')).toBe(false);
    });

    it('hasPermission should return true for existing permission', () => {
      expect(auth.hasPermission('write')).toBe(true);
    });

    it('hasPermission should return false for non-existing permission', () => {
      expect(auth.hasPermission('execute')).toBe(false);
    });

    it('hasAnyRole should return true if any role matches', () => {
      expect(auth.hasAnyRole(['superadmin', 'editor'])).toBe(true);
    });

    it('hasAnyRole should return false if no role matches', () => {
      expect(auth.hasAnyRole(['superadmin', 'viewer'])).toBe(false);
    });

    it('hasAnyPermission should return true if any permission matches', () => {
      expect(auth.hasAnyPermission(['execute', 'delete'])).toBe(true);
    });

    it('hasAnyPermission should return false if no permission matches', () => {
      expect(auth.hasAnyPermission(['execute', 'deploy'])).toBe(false);
    });
  });

  // ------------------------------------------
  // setAuth / clearAuth
  // ------------------------------------------

  describe('setAuth / clearAuth', () => {
    const loginResult = {
      accessToken: 'access-123',
      refreshToken: 'refresh-456',
      expiresIn: 3600,
      user: {
        id: '1',
        userName: 'test',
        nickname: 'Test User',
        email: 'test@example.com',
        phoneNumber: null,
        avatar: null,
        roles: ['admin'],
        permissions: ['read', 'write'],
      },
    };

    it('should set authentication state', () => {
      auth.setAuth(loginResult);
      expect(auth.isAuthenticated).toBe(true);
      expect(auth.accessToken).toBe('access-123');
      expect(auth.refreshToken).toBe('refresh-456');
      expect(auth.user).toBeDefined();
      expect(auth.roles).toEqual(['admin']);
      expect(auth.permissions).toEqual(['read', 'write']);
      expect(auth.error).toBeNull();
    });

    it('should sync token to HTTP client', () => {
      auth.setAuth(loginResult);
      expect(deps.httpClient.setAccessToken).toHaveBeenCalledWith('access-123');
    });

    it('should set tokenExpiry from expiresIn', () => {
      auth.setAuth(loginResult);
      expect(auth.tokenExpiry).toBeInstanceOf(Date);
      expect(auth.isTokenExpired).toBe(false);
    });

    it('clearAuth should reset to initial state', () => {
      auth.setAuth(loginResult);
      auth.clearAuth();
      expect(auth.isAuthenticated).toBe(false);
      expect(auth.accessToken).toBeNull();
      expect(auth.refreshToken).toBeNull();
      expect(auth.user).toBeNull();
      expect(auth.roles).toEqual([]);
      expect(auth.permissions).toEqual([]);
    });

    it('clearAuth should clear HTTP client token', () => {
      auth.setAuth(loginResult);
      auth.clearAuth();
      expect(deps.httpClient.setAccessToken).toHaveBeenLastCalledWith(null);
    });
  });

  // ------------------------------------------
  // setError
  // ------------------------------------------

  describe('setError', () => {
    it('should set error message', () => {
      auth.setError('Something went wrong');
      expect(auth.error).toBe('Something went wrong');
    });

    it('should clear error with null', () => {
      auth.setError('error');
      auth.setError(null);
      expect(auth.error).toBeNull();
    });
  });

  // ------------------------------------------
  // createInitialAuthState
  // ------------------------------------------

  describe('createInitialAuthState', () => {
    it('should return clean initial state', () => {
      const state = createInitialAuthState();
      expect(state.isAuthenticated).toBe(false);
      expect(state.accessToken).toBeNull();
      expect(state.refreshToken).toBeNull();
      expect(state.tokenExpiry).toBeNull();
      expect(state.user).toBeNull();
      expect(state.permissions).toEqual([]);
      expect(state.roles).toEqual([]);
      expect(state.isRefreshing).toBe(false);
      expect(state.error).toBeNull();
    });
  });

  // ------------------------------------------
  // User getters fallback to user object
  // ------------------------------------------

  describe('refreshAccessToken', () => {
    it('throws when no refresh token is set', async () => {
      // Previously this returned silently, which caused HttpClient's
      // refreshTokenFn wrapper to "succeed" with the stale access token
      // and skip the onUnauthorized callback. Throwing instead lets
      // callers distinguish "refresh worked" from "no refresh available"
      // so the consumer's session-expired handler actually fires.
      expect(auth.refreshToken).toBeNull();
      await expect(auth.refreshAccessToken()).rejects.toThrow(/no refresh token/i);
    });
  });

  describe('roles/permissions fallback', () => {
    it('userRoles should fallback to user.roles when roles is empty', () => {
      auth.user = {
        id: '1',
        userName: 'test',
        nickname: null,
        email: null,
        phoneNumber: null,
        avatar: null,
        roles: ['user'],
        permissions: [],
      };
      auth.roles = [];
      expect(auth.userRoles).toEqual(['user']);
    });

    it('userPermissions should fallback to user.permissions when permissions is empty', () => {
      auth.user = {
        id: '1',
        userName: 'test',
        nickname: null,
        email: null,
        phoneNumber: null,
        avatar: null,
        roles: [],
        permissions: ['view'],
      };
      auth.permissions = [];
      expect(auth.userPermissions).toEqual(['view']);
    });
  });
});
