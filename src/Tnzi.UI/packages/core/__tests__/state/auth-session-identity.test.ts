import { describe, it, expect, beforeEach, vi } from 'vitest';
import { AuthStateManager } from '../../src/state/auth';
import { createMemoryStorageAdapter } from '../../src/adapters/storage';
import type { StateDeps } from '../../src/state/types';
import type { HttpClient } from '../../src/http/http';

/**
 * Who the manager says the user is, across the moments one session ends and
 * another begins, and how long a token refresh makes the page wait.
 */

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
    storage: createMemoryStorageAdapter(),
    ...overrides,
  };
}

function profile(userName: string, roles: string[]) {
  return { succeeded: true, data: { id: userName, userName, roles } };
}

/** A promise the test settles by hand, to hold a request open. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

/** Let every queued promise callback run. */
const flush = () => new Promise<void>((r) => setTimeout(r, 0));

beforeEach(() => {
  authApiMocks.loginWithRefreshToken.mockReset();
  authApiMocks.refreshToken.mockReset();
  authApiMocks.logout.mockReset();
  profileApiMocks.get.mockReset();
});

describe('role and permission checks ignore case, as the server does', () => {
  it('matches a role or a code whatever its casing', () => {
    const auth = new AuthStateManager(createDeps());
    auth.roles = ['Admin'];
    auth.permissions = ['catalog.product.view'];

    expect(auth.hasRole('admin')).toBe(true);
    expect(auth.hasRole('ADMIN')).toBe(true);
    expect(auth.hasPermission('Catalog.Product.View')).toBe(true);
    expect(auth.hasAnyRole(['editor', 'aDmIn'])).toBe(true);
    expect(auth.hasAnyPermission(['x.y', 'CATALOG.PRODUCT.VIEW'])).toBe(true);
  });

  it('still refuses what the user does not hold', () => {
    const auth = new AuthStateManager(createDeps());
    auth.roles = ['Admin'];
    auth.permissions = ['catalog.product.view'];

    expect(auth.hasRole('editor')).toBe(false);
    expect(auth.hasPermission('catalog.product.update')).toBe(false);
    expect(auth.hasAnyRole(['Editor', 'viewer'])).toBe(false);
    expect(auth.hasAnyPermission([])).toBe(false);
  });

  it('ignores case on the profile fallback too', () => {
    const auth = new AuthStateManager(createDeps());
    auth.user = {
      id: '1', userName: 'a', nickname: null, email: null, phoneNumber: null, avatar: null,
      roles: ['Manager'], permissions: ['Orders.Read'],
    };

    expect(auth.hasRole('manager')).toBe(true);
    expect(auth.hasPermission('orders.read')).toBe(true);
  });
});

describe('a new session starts without the previous identity', () => {
  /** Restore user A (role Admin, code a.view) from persisted tokens. */
  async function restoreAdminA(deps: StateDeps, auth: AuthStateManager): Promise<void> {
    deps.storage.set('tnzi:auth:token', 'a-access');
    deps.storage.set('tnzi:auth:refresh', 'a-refresh');
    deps.storage.set('tnzi:auth:expiry', new Date(Date.now() + 3600_000).toISOString());
    profileApiMocks.get.mockResolvedValueOnce(profile('alice', ['Admin']));
    await auth.restoreAuth();
    expect(auth.hasRole('Admin')).toBe(true);
    expect(auth.hasPermission('a.view')).toBe(true);
  }

  it('applyTokenSession drops the restored roles and codes of another user', async () => {
    const permissionsFetchFn = vi.fn().mockResolvedValueOnce(['a.view']).mockResolvedValueOnce([]);
    const deps = createDeps({ permissionsFetchFn });
    const auth = new AuthStateManager(deps);
    await restoreAdminA(deps, auth);

    // B accepts an invitation in the same browser; B holds no role and no code.
    profileApiMocks.get.mockResolvedValueOnce(profile('bob', []));
    await auth.applyTokenSession({ accessToken: 'b-access', refreshToken: 'b-refresh', expiresIn: 3600 });

    expect(auth.userName).toBe('bob');
    expect(auth.userRoles).toEqual([]);
    expect(auth.hasRole('Admin')).toBe(false);
    // The profile answered before the permission call, so it used to snapshot
    // A's codes into B's profile, and the empty list fell back to them.
    expect(auth.userPermissions).toEqual([]);
    expect(auth.hasPermission('a.view')).toBe(false);
  });

  it('login drops them too, and takes the roles from the new profile', async () => {
    // B holds no permission code: the case where A's leaked through the fallback.
    const permissionsFetchFn = vi.fn().mockResolvedValueOnce(['a.view']).mockResolvedValueOnce([]);
    const deps = createDeps({ permissionsFetchFn });
    const auth = new AuthStateManager(deps);
    await restoreAdminA(deps, auth);

    authApiMocks.loginWithRefreshToken.mockResolvedValue({
      succeeded: true,
      data: { accessToken: 'b-access', refreshToken: 'b-refresh', expiresIn: 3600 },
    });
    profileApiMocks.get.mockResolvedValueOnce(profile('bob', ['Editor']));
    await auth.login({ userName: 'bob', password: 'pw' } as never);

    expect(auth.roles).toEqual(['Editor']);
    expect(auth.hasRole('Admin')).toBe(false);
    expect(auth.userPermissions).toEqual([]);
    expect(auth.hasPermission('a.view')).toBe(false);
  });

  it('a token-only session removes the earlier refresh token from storage', async () => {
    const deps = createDeps({ permissionsFetchFn: vi.fn().mockResolvedValue([]) });
    const auth = new AuthStateManager(deps);
    deps.storage.set('tnzi:auth:token', 'a-access');
    deps.storage.set('tnzi:auth:refresh', 'a-refresh');
    deps.storage.set('tnzi:auth:expiry', new Date(Date.now() + 3600_000).toISOString());
    profileApiMocks.get.mockResolvedValue(profile('bob', []));

    await auth.applyTokenSession({ accessToken: 'b-access', refreshToken: null });

    // Left in place, the next reload restored B's access token with A's refresh
    // token, and the first refresh signed the tab in as A.
    expect(deps.storage.get('tnzi:auth:token')).toBe('b-access');
    expect(deps.storage.get('tnzi:auth:refresh')).toBeNull();
    expect(deps.storage.get('tnzi:auth:expiry')).toBeNull();
  });

  it('a permission answer that arrives after sign-out is dropped', async () => {
    const pending = deferred<string[]>();
    const deps = createDeps({ permissionsFetchFn: vi.fn(() => pending.promise) });
    const auth = new AuthStateManager(deps);
    profileApiMocks.get.mockResolvedValue(profile('bob', []));

    const session = auth.applyTokenSession({ accessToken: 'b-access', refreshToken: 'b-refresh', expiresIn: 3600 });
    await flush();
    auth.clearAuth();
    pending.resolve(['b.view']);
    await session;

    expect(auth.permissions).toEqual([]);
    expect(auth.hasPermission('b.view')).toBe(false);
  });
});

describe('a token refresh does not wait for the permission reload', () => {
  it('resolves as soon as the new token is in place', async () => {
    const permissionsFetchFn = vi.fn(() => new Promise<string[]>(() => {}));
    const deps = createDeps({ permissionsFetchFn });
    const auth = new AuthStateManager(deps);
    auth.accessToken = 'old';
    auth.refreshToken = 'r1';
    auth.isAuthenticated = true;
    authApiMocks.refreshToken.mockResolvedValue({
      succeeded: true,
      data: { accessToken: 'new', refreshToken: 'r2', expiresIn: 3600 },
    });

    // Every request queued behind a 401 waits for this call, under the
    // client's request timeout; a slow permission call used to hold it.
    const outcome = await Promise.race([
      auth.refreshAccessToken().then(() => 'refreshed'),
      new Promise((r) => setTimeout(() => r('still waiting'), 50)),
    ]);

    expect(outcome).toBe('refreshed');
    expect(auth.accessToken).toBe('new');
    expect(deps.httpClient.setAccessToken).toHaveBeenLastCalledWith('new');
    expect(permissionsFetchFn).toHaveBeenCalledTimes(1);
  });

  it('still updates the permissions once the reload answers', async () => {
    const pending = deferred<string[]>();
    const deps = createDeps({ permissionsFetchFn: vi.fn(() => pending.promise) });
    const auth = new AuthStateManager(deps);
    auth.accessToken = 'old';
    auth.refreshToken = 'r1';
    auth.isAuthenticated = true;
    auth.permissions = ['old.code'];
    authApiMocks.refreshToken.mockResolvedValue({
      succeeded: true,
      data: { accessToken: 'new', refreshToken: 'r2', expiresIn: 3600 },
    });

    await auth.refreshAccessToken();
    expect(auth.permissions).toEqual(['old.code']);

    pending.resolve(['new.code']);
    await flush();
    expect(auth.permissions).toEqual(['new.code']);
  });

  it('a bearer restore that had to refresh reports the session only with its codes', async () => {
    const pending = deferred<string[]>();
    const permissionsFetchFn = vi.fn(() => pending.promise);
    const deps = createDeps({ permissionsFetchFn });
    deps.storage.set('tnzi:auth:token', 'stale');
    deps.storage.set('tnzi:auth:refresh', 'r1');
    profileApiMocks.get
      .mockResolvedValueOnce({ succeeded: false, code: 401 })
      .mockResolvedValueOnce(profile('alice', ['Admin']));
    authApiMocks.refreshToken.mockResolvedValue({
      succeeded: true,
      data: { accessToken: 'fresh', refreshToken: 'r2', expiresIn: 3600 },
    });
    const auth = new AuthStateManager(deps);

    const restoring = auth.restoreAuth();
    await flush();
    expect(auth.isAuthenticated).toBe(false);

    pending.resolve(['a.view']);
    await restoring;

    expect(auth.isAuthenticated).toBe(true);
    expect(auth.permissions).toEqual(['a.view']);
    // The restore joined the reload the refresh started rather than asking twice.
    expect(permissionsFetchFn).toHaveBeenCalledTimes(1);
  });

  it('a cookie-mode restore reports the session only with its codes', async () => {
    const pending = deferred<string[]>();
    const permissionsFetchFn = vi.fn(() => pending.promise);
    const deps = createDeps({ permissionsFetchFn, tokenDelivery: 'cookie' });
    profileApiMocks.get.mockResolvedValue(profile('alice', ['Admin']));
    authApiMocks.refreshToken.mockResolvedValue({
      succeeded: true,
      data: { accessToken: 'fresh', refreshToken: '', expiresIn: 3600 },
    });
    const auth = new AuthStateManager(deps);

    const restoring = auth.restoreAuth();
    await flush();
    expect(auth.isAuthenticated).toBe(false);

    pending.resolve(['a.view']);
    await restoring;

    expect(auth.isAuthenticated).toBe(true);
    expect(auth.permissions).toEqual(['a.view']);
    expect(permissionsFetchFn).toHaveBeenCalledTimes(1);
  });
});
