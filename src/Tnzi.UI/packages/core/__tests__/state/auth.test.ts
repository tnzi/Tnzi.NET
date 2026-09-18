import { describe, it, expect, beforeEach, vi } from 'vitest';
import {
  AuthStateManager,
  createInitialAuthState,
  sessionEndReasonOf,
  SESSION_ENDED_FOR_SECURITY_MESSAGE,
  SESSION_EXPIRED_MESSAGE,
} from '../../src/state/auth';
import type { StateDeps } from '../../src/state/types';
import type { HttpClient } from '../../src/http/http';
import type { StorageAdapter } from '../../src/adapters/storage';

// Mock service imports. Shared (hoisted) spies so tests can assert on
// which auth endpoints were (not) hit, e.g. "refresh failure must NOT call
// the backend logout endpoint".
const authApiMocks = vi.hoisted(() => ({
  loginWithRefreshToken: vi.fn(),
  refreshToken: vi.fn(),
  logout: vi.fn(),
}));
const profileApiMocks = vi.hoisted(() => ({
  get: vi.fn(),
  update: vi.fn(),
  changePassword: vi.fn(),
}));
vi.mock('../../src/services/identity/index', () => ({
  useAuthApi: () => authApiMocks,
  useProfileApi: () => profileApiMocks,
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
  // applyTokenSession (token-only login: code login / OAuth)
  // ------------------------------------------

  describe('applyTokenSession', () => {
    it('establishes a persisted session AND fetches permissions from tokens only', async () => {
      // permissionsFetchFn is the only thing that populates this.permissions -
      // locks the M1 regression where applyTokenSession skipped it.
      const permissionsFetchFn = vi.fn().mockResolvedValue(['perm.a', 'perm.b']);
      const localDeps = createDeps({ permissionsFetchFn });
      const localAuth = new AuthStateManager(localDeps);

      await localAuth.applyTokenSession({ accessToken: 'code-tok', refreshToken: 'code-refresh', expiresIn: 3600 });

      expect(localAuth.isAuthenticated).toBe(true);
      expect(localAuth.accessToken).toBe('code-tok');
      expect(localAuth.refreshToken).toBe('code-refresh');
      expect(localDeps.httpClient.setAccessToken).toHaveBeenCalledWith('code-tok');
      // Persisted (unlike setAuth) so a hard refresh can restore the session.
      expect(localDeps.storage.get('tnzi:auth:token')).toBe('code-tok');
      // Permissions populated - every hasPermission()/guard depends on this.
      expect(permissionsFetchFn).toHaveBeenCalled();
      expect(localAuth.permissions).toEqual(['perm.a', 'perm.b']);
    });

    it('tolerates a missing refresh token / expiry', async () => {
      const localDeps = createDeps({ permissionsFetchFn: vi.fn().mockResolvedValue([]) });
      const localAuth = new AuthStateManager(localDeps);
      await localAuth.applyTokenSession({ accessToken: 'tok-only' });
      expect(localAuth.isAuthenticated).toBe(true);
      expect(localAuth.refreshToken).toBeNull();
      expect(localAuth.tokenExpiry).toBeNull();
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

  // ------------------------------------------
  // Refresh failure = session expired (local sign-out)
  // ------------------------------------------

  describe('refresh failure (session expired)', () => {
    beforeEach(() => {
      authApiMocks.refreshToken.mockReset();
      authApiMocks.logout.mockReset();
    });

    function seedSession(manager: AuthStateManager): void {
      manager.isAuthenticated = true;
      manager.accessToken = 'stale-access';
      manager.refreshToken = 'dead-refresh';
      manager.user = {
        id: '1',
        userName: 'john',
        nickname: null,
        email: null,
        phoneNumber: null,
        avatar: null,
        roles: [],
        permissions: [],
      };
    }

    it('clears auth locally WITHOUT calling the backend logout endpoint', async () => {
      // Regression: the failure path used to run the full logout(), whose
      // POST /auth/logout carried the expired access token, 401'd, and
      // stalled inside the HttpClient refresh cycle for a whole request
      // timeout before the session-expired signal could reach the app.
      const onLogout = vi.fn();
      const localDeps = createDeps({ onLogout });
      const localAuth = new AuthStateManager(localDeps);
      seedSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({
        succeeded: false,
        message: 'Invalid or expired refresh token',
        code: 400,
      });

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(authApiMocks.logout).not.toHaveBeenCalled();
      expect(localAuth.isAuthenticated).toBe(false);
      expect(localAuth.accessToken).toBeNull();
      expect(localAuth.refreshToken).toBeNull();
      expect(localAuth.error).toMatch(/session expired/i);
      expect(onLogout).toHaveBeenCalledTimes(1);
      expect(localDeps.httpClient.setAccessToken).toHaveBeenLastCalledWith(null);
    });

    it('clears persisted tokens and pushes the router to /login', async () => {
      const storage = createMockStorage();
      storage.set('tnzi:auth:token', 'stale-access');
      storage.set('tnzi:auth:refresh', 'dead-refresh');
      const push = vi.fn();
      const localDeps = createDeps({
        storage,
        router: { push } as unknown as StateDeps['router'],
      });
      const localAuth = new AuthStateManager(localDeps);
      seedSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 400 });

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(storage.get('tnzi:auth:token')).toBeNull();
      expect(storage.get('tnzi:auth:refresh')).toBeNull();
      expect(push).toHaveBeenCalledWith('/login');
    });

    it('honors a custom deps.loginPath (sub-path deployments)', async () => {
      const push = vi.fn();
      const localDeps = createDeps({
        router: { push } as unknown as StateDeps['router'],
        loginPath: '/admin/login',
      });
      const localAuth = new AuthStateManager(localDeps);
      seedSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 400 });

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(push).toHaveBeenCalledWith('/admin/login');
    });
  });

  // ------------------------------------------
  // restoreAuth (session rehydration on hard reload)
  // ------------------------------------------

  describe('restoreAuth', () => {
    const STORED = { token: 'stored-access', refresh: 'stored-refresh' };

    function seedTokens(storage: StorageAdapter): void {
      storage.set('tnzi:auth:token', STORED.token);
      storage.set('tnzi:auth:refresh', STORED.refresh);
      storage.set('tnzi:auth:expiry', new Date(Date.now() + 3600_000).toISOString());
    }

    beforeEach(() => {
      authApiMocks.refreshToken.mockReset();
      profileApiMocks.get.mockReset();
      seedTokens(deps.storage);
    });

    it('restores the session when the stored token still works', async () => {
      profileApiMocks.get.mockResolvedValue({
        succeeded: true,
        data: { id: '1', userName: 'alice', roles: ['admin'] },
      });

      await auth.restoreAuth();

      expect(auth.isAuthenticated).toBe(true);
      expect(auth.isLoggedIn).toBe(true);
      expect(auth.user?.userName).toBe('alice');
      expect(auth.roles).toEqual(['admin']);
      expect(authApiMocks.refreshToken).not.toHaveBeenCalled();
    });

    it('restores the session when the profile call fails but refresh succeeds', async () => {
      // First profile call (stale token) fails, the post-refresh one succeeds.
      profileApiMocks.get
        .mockResolvedValueOnce({ succeeded: false, code: 401 })
        .mockResolvedValueOnce({
          succeeded: true,
          data: { id: '1', userName: 'alice', roles: ['admin'] },
        });
      authApiMocks.refreshToken.mockResolvedValue({
        succeeded: true,
        data: { accessToken: 'fresh', refreshToken: 'fresh-refresh', expiresIn: 3600 },
      });

      await auth.restoreAuth();

      // The regression: refresh succeeded but nothing set isAuthenticated, so
      // fetchUserProfile() short-circuited and the guard bounced the user out.
      expect(auth.isAuthenticated).toBe(true);
      expect(auth.isLoggedIn).toBe(true);
      expect(auth.accessToken).toBe('fresh');
      expect(auth.user?.userName).toBe('alice');
      expect(auth.userRoles).toEqual(['admin']);
    });

    it('clears everything when both the profile call and the refresh fail', async () => {
      profileApiMocks.get.mockResolvedValue({ succeeded: false, code: 401 });
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 400 });

      await auth.restoreAuth();

      expect(auth.isAuthenticated).toBe(false);
      expect(auth.isLoggedIn).toBe(false);
      expect(auth.user).toBeNull();
      expect(deps.storage.get('tnzi:auth:token')).toBeNull();
      expect(deps.storage.get('tnzi:auth:refresh')).toBeNull();
    });

    it('is a no-op when no tokens are persisted', async () => {
      deps.storage.clear();

      await auth.restoreAuth();

      expect(auth.isAuthenticated).toBe(false);
      expect(profileApiMocks.get).not.toHaveBeenCalled();
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

  // ------------------------------------------
  // login(): a challenge is a third answer, not a failure message
  // ------------------------------------------

  describe('login challenge envelopes', () => {
    beforeEach(() => {
      authApiMocks.loginWithRefreshToken.mockReset();
    });

    it.each([
      ['2FA_REQUIRED', { tempToken: 't-2fa', supportedTypes: ['Totp'] }],
      ['IDENTITY_PENDING_ACTIONS_REQUIRED', { tempToken: 't-pending', actions: ['ChangePassword'] }],
      ['IDENTITY_CAPTCHA_REQUIRED', { captchaId: 'c1', imageBase64: 'AAAA' }],
    ])('rejects with an HttpError carrying %s and its details', async (errorCode, errorDetails) => {
      authApiMocks.loginWithRefreshToken.mockResolvedValue({
        succeeded: false,
        code: 403,
        message: 'Challenge',
        errorCode,
        errorDetails,
      });

      await expect(auth.login({ userName: 'alice', password: 'pw' })).rejects.toMatchObject({
        name: 'HttpError',
        statusCode: 403,
        errorCode,
        details: errorDetails,
      });
      // Message-based callers keep working: HttpError is still an Error.
      await expect(auth.login({ userName: 'alice', password: 'pw' })).rejects.toThrow('Challenge');
      expect(auth.error).toBe('Challenge');
      expect(auth.isAuthenticated).toBe(false);
    });
  });

  // ------------------------------------------
  // logout(): revocation must not depend on the access token still being alive
  // ------------------------------------------

  describe('logout with an expired access token', () => {
    const onLogout = vi.fn();

    function seedExpiredSession(manager: AuthStateManager): void {
      manager.isAuthenticated = true;
      manager.accessToken = 'expired-access';
      manager.refreshToken = 'live-refresh';
      manager.tokenExpiry = new Date(Date.now() - 60_000);
    }

    beforeEach(() => {
      authApiMocks.refreshToken.mockReset();
      authApiMocks.logout.mockReset();
      onLogout.mockReset();
    });

    it('refreshes first, then revokes with the new token', async () => {
      const localDeps = createDeps({ onLogout });
      const localAuth = new AuthStateManager(localDeps);
      seedExpiredSession(localAuth);
      const order: string[] = [];
      authApiMocks.refreshToken.mockImplementation(async () => {
        order.push('refresh');
        return { succeeded: true, code: 200, data: { accessToken: 'fresh-access', refreshToken: 'fresh-refresh', expiresIn: 3600 } };
      });
      authApiMocks.logout.mockImplementation(async () => {
        order.push(`logout:${localDeps.httpClient.getAccessToken()}`);
        return { succeeded: true, code: 200 };
      });
      (localDeps.httpClient.setAccessToken as ReturnType<typeof vi.fn>).mockImplementation((t: string | null) => {
        (localDeps.httpClient.getAccessToken as ReturnType<typeof vi.fn>).mockReturnValue(t);
      });

      await localAuth.logout();

      expect(order).toEqual(['refresh', 'logout:fresh-access']);
      expect(authApiMocks.refreshToken).toHaveBeenCalledWith({ refreshToken: 'live-refresh' });
      expect(localAuth.isAuthenticated).toBe(false);
      expect(localAuth.accessToken).toBeNull();
      expect(onLogout).toHaveBeenCalledTimes(1);
    });

    it('still clears locally when the refresh also fails', async () => {
      const localAuth = new AuthStateManager(createDeps({ onLogout }));
      seedExpiredSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 400 });
      authApiMocks.logout.mockResolvedValue({ succeeded: false, code: 401 });

      await localAuth.logout();

      expect(localAuth.isAuthenticated).toBe(false);
      expect(localAuth.accessToken).toBeNull();
      expect(localAuth.error).toBeNull();
      expect(onLogout).toHaveBeenCalledTimes(1);
    });

    it('presents a refused refresh token once, not again after the logout 401', async () => {
      // The reactive retry exists for the case where the client-side expiry was
      // wrong and the proactive refresh never ran. When it DID run and the server
      // refused the token, the logout 401 is the expected consequence - asking
      // again with the same dead token is a wasted round trip, and the backend's
      // replay detection may log a plain sign-out as a copied token.
      const localAuth = new AuthStateManager(createDeps({ onLogout }));
      seedExpiredSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 400 });
      authApiMocks.logout.mockResolvedValue({ succeeded: false, code: 401 });

      await localAuth.logout();

      expect(authApiMocks.refreshToken).toHaveBeenCalledTimes(1);
      expect(authApiMocks.logout).toHaveBeenCalledTimes(1);
      expect(localAuth.isAuthenticated).toBe(false);
    });

    it('does not refresh when the token is still live', async () => {
      const localAuth = new AuthStateManager(createDeps({ onLogout }));
      localAuth.isAuthenticated = true;
      localAuth.accessToken = 'live-access';
      localAuth.refreshToken = 'live-refresh';
      localAuth.tokenExpiry = new Date(Date.now() + 3600_000);
      authApiMocks.logout.mockResolvedValue({ succeeded: true, code: 200 });

      await localAuth.logout();

      expect(authApiMocks.refreshToken).not.toHaveBeenCalled();
      expect(authApiMocks.logout).toHaveBeenCalledTimes(1);
    });

    it('recovers from a 401 the client-side expiry did not predict (clock skew)', async () => {
      const localAuth = new AuthStateManager(createDeps({ onLogout }));
      localAuth.isAuthenticated = true;
      localAuth.accessToken = 'server-says-expired';
      localAuth.refreshToken = 'live-refresh';
      localAuth.tokenExpiry = new Date(Date.now() + 3600_000);
      authApiMocks.logout
        .mockResolvedValueOnce({ succeeded: false, code: 401 })
        .mockResolvedValueOnce({ succeeded: true, code: 200 });
      authApiMocks.refreshToken.mockResolvedValue({
        succeeded: true,
        code: 200,
        data: { accessToken: 'fresh-access', refreshToken: 'fresh-refresh', expiresIn: 3600 },
      });

      await localAuth.logout();

      expect(authApiMocks.refreshToken).toHaveBeenCalledTimes(1);
      expect(authApiMocks.logout).toHaveBeenCalledTimes(2);
    });
  });

  // ------------------------------------------
  // refresh failure classification: rejected token vs could-not-ask
  // ------------------------------------------

  describe('refresh transport failures keep the persisted tokens', () => {
    function seedPersisted(manager: AuthStateManager, storage: StorageAdapter): void {
      manager.isAuthenticated = true;
      manager.accessToken = 'stale-access';
      manager.refreshToken = 'maybe-alive-refresh';
      storage.set('tnzi:auth:token', 'stale-access');
      storage.set('tnzi:auth:refresh', 'maybe-alive-refresh');
    }

    beforeEach(() => {
      authApiMocks.refreshToken.mockReset();
    });

    it.each([
      ['client timeout', { succeeded: false, code: 408, errorCode: 'REQUEST_TIMEOUT', message: 'timed out' }],
      ['rate limited', { succeeded: false, code: 429, message: 'Too many requests' }],
      ['proxy 503', { succeeded: false, code: 503, message: 'HTTP 503 Service Unavailable' }],
      ['network error', { succeeded: false, code: 500, message: 'Failed to fetch' }],
    ])('%s: rejects but leaves storage, error and router untouched', async (_label, envelope) => {
      const storage = createMockStorage();
      const push = vi.fn();
      const onLogout = vi.fn();
      const localAuth = new AuthStateManager(
        createDeps({ storage, onLogout, router: { push } as unknown as StateDeps['router'] }),
      );
      seedPersisted(localAuth, storage);
      authApiMocks.refreshToken.mockResolvedValue(envelope);

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(storage.get('tnzi:auth:refresh')).toBe('maybe-alive-refresh');
      expect(storage.get('tnzi:auth:token')).toBe('stale-access');
      expect(localAuth.error).toBeNull();
      expect(push).not.toHaveBeenCalled();
      expect(onLogout).not.toHaveBeenCalled();
    });

    it.each([
      ['expired / invalid', { succeeded: false, code: 400, message: 'Invalid or expired refresh token' }],
      ['replayed (security)', { succeeded: false, code: 401, errorCode: 'IDENTITY_REFRESH_TOKEN_REUSED' }],
    ])('%s: the server rejected the token, so everything is cleared', async (_label, envelope) => {
      const storage = createMockStorage();
      const push = vi.fn();
      const localAuth = new AuthStateManager(
        createDeps({ storage, router: { push } as unknown as StateDeps['router'] }),
      );
      seedPersisted(localAuth, storage);
      authApiMocks.refreshToken.mockResolvedValue(envelope);

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(storage.get('tnzi:auth:refresh')).toBeNull();
      expect(localAuth.isAuthenticated).toBe(false);
      expect(localAuth.error).toMatch(/session/i);
      expect(push).toHaveBeenCalledWith('/login');
    });

    it('restoreAuth while offline keeps the persisted tokens for the next boot', async () => {
      const storage = createMockStorage();
      storage.set('tnzi:auth:token', 'stored-access');
      storage.set('tnzi:auth:refresh', 'stored-refresh');
      const localAuth = new AuthStateManager(createDeps({ storage }));
      profileApiMocks.get.mockReset();
      profileApiMocks.get.mockResolvedValue({ succeeded: false, code: 500, message: 'Failed to fetch' });
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 500, message: 'Failed to fetch' });

      await localAuth.restoreAuth();

      expect(localAuth.isAuthenticated).toBe(false);
      expect(localAuth.accessToken).toBeNull();
      expect(storage.get('tnzi:auth:token')).toBe('stored-access');
      expect(storage.get('tnzi:auth:refresh')).toBe('stored-refresh');
    });
  });

  // ------------------------------------------
  // sessionEndReason - the login page's typed view of `error`
  // ------------------------------------------

  describe('sessionEndReason', () => {
    function seedSession(manager: AuthStateManager): void {
      manager.isAuthenticated = true;
      manager.accessToken = 'stale-access';
      manager.refreshToken = 'dead-refresh';
    }

    beforeEach(() => {
      authApiMocks.refreshToken.mockReset();
    });

    it('is null on a fresh manager and after an unrelated error', () => {
      expect(auth.sessionEndReason).toBeNull();
      auth.setError('Invalid user name or password');
      expect(auth.sessionEndReason).toBeNull();
    });

    it('is "expired" after the server rejected the refresh token', async () => {
      const localAuth = new AuthStateManager(createDeps());
      seedSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({
        succeeded: false,
        code: 400,
        message: 'Invalid or expired refresh token',
      });

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(localAuth.sessionEndReason).toBe('expired');
    });

    it('★ is "security" when the session was ended for a security reason', async () => {
      // This is the one moment the legitimate user can learn that their
      // credentials are in use elsewhere; a login page keyed on the raw string
      // would have to know core's English copy to tell the two apart.
      const localAuth = new AuthStateManager(createDeps());
      seedSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({
        succeeded: false,
        code: 401,
        errorCode: 'IDENTITY_REFRESH_TOKEN_REUSED',
        message: 'Invalid or expired refresh token',
      });

      await expect(localAuth.refreshAccessToken()).rejects.toThrow();

      expect(localAuth.sessionEndReason).toBe('security');
    });

    it('clears once a login attempt starts', async () => {
      const localAuth = new AuthStateManager(createDeps());
      seedSession(localAuth);
      authApiMocks.refreshToken.mockResolvedValue({ succeeded: false, code: 400, message: 'dead' });
      await expect(localAuth.refreshAccessToken()).rejects.toThrow();
      expect(localAuth.sessionEndReason).toBe('expired');

      authApiMocks.loginWithRefreshToken.mockResolvedValue({
        succeeded: false,
        code: 400,
        message: 'Invalid user name or password',
      });
      await expect(localAuth.login({ userName: 'a', password: 'b' })).rejects.toThrow();

      expect(localAuth.sessionEndReason).toBeNull();
    });

    it('sessionEndReasonOf classifies only the two session messages', () => {
      expect(sessionEndReasonOf(SESSION_ENDED_FOR_SECURITY_MESSAGE)).toBe('security');
      expect(sessionEndReasonOf(SESSION_EXPIRED_MESSAGE)).toBe('expired');
      expect(sessionEndReasonOf('Session expired')).toBeNull();
      expect(sessionEndReasonOf(null)).toBeNull();
      expect(sessionEndReasonOf(undefined)).toBeNull();
    });
  });
});
