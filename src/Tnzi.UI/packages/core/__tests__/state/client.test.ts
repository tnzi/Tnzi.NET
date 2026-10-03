import { describe, it, expect, vi, afterEach } from 'vitest';
import { createTnziClient } from '../../src/state/client';
import { HttpClient } from '../../src/http/http';
import { AuthStateManager } from '../../src/state/auth';
import type { StorageAdapter } from '../../src/adapters/storage';

/** Minimal in-memory StorageAdapter (core tests run in the node env - no localStorage). */
function memStorage(): StorageAdapter {
  const m = new Map<string, string>();
  return {
    getItem: (k) => (m.has(k) ? m.get(k)! : null),
    setItem: (k, v) => void m.set(k, v),
    removeItem: (k) => void m.delete(k),
    clear: () => m.clear(),
    get: <T>(k: string) => (m.has(k) ? (m.get(k) as unknown as T) : null),
    set: (k, v) => void m.set(k, v as unknown as string),
    remove: (k) => void m.delete(k),
    keys: () => [...m.keys()],
    has: (k) => m.has(k),
  };
}

describe('createTnziClient', () => {
  it('wires an HttpClient + AuthStateManager + authApi in one call', () => {
    const { http, auth, authApi } = createTnziClient({ baseUrl: '/api', storage: memStorage() });
    expect(http).toBeInstanceOf(HttpClient);
    expect(auth).toBeInstanceOf(AuthStateManager);
    expect(typeof authApi.codeLogin).toBe('function');
    expect(typeof authApi.sendCodeLoginCode).toBe('function');
    expect(typeof authApi.resetPasswordByCode).toBe('function');
    expect(typeof authApi.quickRegister).toBe('function');
  });

  it('binds the auth manager to THIS http client (clearAuth nulls the client token)', () => {
    const { http, auth } = createTnziClient({ storage: memStorage() });
    http.setAccessToken('preset');
    expect(http.getAccessToken()).toBe('preset');
    // clearAuth() is synchronous and calls `this.deps.httpClient.setAccessToken(null)`
    // - so this only nulls the client token if the manager was built with `http`.
    auth.clearAuth();
    expect(http.getAccessToken()).toBeNull();
  });

  it('syncs + persists tokens under the default prefix', async () => {
    const storage = memStorage();
    const { http, auth } = createTnziClient({ storage });
    // The token sync + persist run synchronously BEFORE applyTokenSession's first
    // await (the profile/permission fetch), so both are observable immediately.
    const p = auth.applyTokenSession({ accessToken: 'aaa', refreshToken: 'bbb', expiresIn: 10 });
    expect(http.getAccessToken()).toBe('aaa');
    expect(storage.get('tnzi:auth:token')).toBe('aaa');
    expect(storage.get('tnzi:auth:refresh')).toBe('bbb');
    await p.catch(() => undefined); // let the (backend-less) profile fetch settle
  });

  it('isolates persisted tokens via storagePrefix', async () => {
    const storage = memStorage();
    const { auth } = createTnziClient({ storage, storagePrefix: 'app:auth' });
    const p = auth.applyTokenSession({ accessToken: 'x', refreshToken: 'y', expiresIn: 10 });
    expect(storage.get('app:auth:token')).toBe('x');
    expect(storage.get('tnzi:auth:token')).toBeNull();
    await p.catch(() => undefined);
  });

  /**
   * Permissions used to be left unwired here, so `hasPermission()` answered
   * `false` to everything for every app that did not also run
   * `@tnzi/ui-admin` (which loads them separately). Nothing failed and nothing
   * logged - a privileged surface simply never appeared. These lock the fix.
   */
  describe('permission loading', () => {
    function stubGet(result: unknown) {
      const get = vi.fn(async () => result);
      return { get, patch: (http: HttpClient) => Object.assign(http, { get }) };
    }

    it('loads permissions from the access profile by default', async () => {
      const { http, auth } = createTnziClient({ storage: memStorage() });
      const { get, patch } = stubGet({ data: { permissions: ['system.appearance.update'] } });
      patch(http);

      await auth.applyTokenSession({ accessToken: 'a' }).catch(() => undefined);

      expect(get).toHaveBeenCalledWith(
        '/admin/function-authorization/access-profile',
        expect.objectContaining({ skipAuthRefresh: true }),
      );
      expect(auth.hasPermission('system.appearance.update')).toBe(true);
    });

    /**
     * The permission fetch runs INSIDE the auth cycle - including from within
     * `refreshAccessToken` itself. A 401 without this flag asks the HttpClient
     * to refresh while a refresh is already in flight, and that wait can only
     * be broken by the 30s mutex timeout.
     */
    it('marks the permission fetch as skipAuthRefresh', async () => {
      const { http, auth } = createTnziClient({ storage: memStorage() });
      const { get, patch } = stubGet({ data: { permissions: [] } });
      patch(http);

      await auth.applyTokenSession({ accessToken: 'a' }).catch(() => undefined);

      const call = get.mock.calls.find(
        (c) => String(c[0]).includes('access-profile'),
      ) as [string, { skipAuthRefresh?: boolean } | undefined] | undefined;
      expect(call?.[1]?.skipAuthRefresh).toBe(true);
    });

    it('treats an unusable response as "holds nothing" rather than throwing', async () => {
      const { http, auth } = createTnziClient({ storage: memStorage() });
      patchWith(http, async () => ({ data: { permissions: null } }));

      await auth.applyTokenSession({ accessToken: 'a' }).catch(() => undefined);

      expect(auth.hasPermission('anything')).toBe(false);
    });

    it('lets a consumer supply its own source', async () => {
      const permissionsFetchFn = vi.fn(async () => ['custom.code']);
      const { http, auth } = createTnziClient({ storage: memStorage(), permissionsFetchFn });
      const { get, patch } = stubGet({ data: { permissions: ['from.endpoint'] } });
      patch(http);

      await auth.applyTokenSession({ accessToken: 'a' }).catch(() => undefined);

      expect(permissionsFetchFn).toHaveBeenCalled();
      // `get` IS called - `applyTokenSession` also fetches the profile. What
      // must not happen is a call to the permission endpoint.
      expect(get.mock.calls.flat()).not.toContain('/admin/function-authorization/access-profile');
      expect(auth.hasPermission('custom.code')).toBe(true);
    });

    it('skips the call entirely on null', async () => {
      const { http, auth } = createTnziClient({ storage: memStorage(), permissionsFetchFn: null });
      const { get, patch } = stubGet({ data: { permissions: ['x'] } });
      patch(http);

      await auth.applyTokenSession({ accessToken: 'a' }).catch(() => undefined);

      // `get` IS called - `applyTokenSession` also fetches the profile. What
      // must not happen is a call to the permission endpoint.
      expect(get.mock.calls.flat()).not.toContain('/admin/function-authorization/access-profile');
    });
  });
});

/** Replace `http.get` for the duration of a test. */
function patchWith(http: HttpClient, fn: () => Promise<unknown>): void {
  Object.assign(http, { get: fn });
}

/**
 * The canonical wiring end to end: a real HttpClient driving a real
 * AuthStateManager through `refreshTokenFn` and `onUnauthorized`. The
 * manager-only tests cannot see what this layer does to the state the manager
 * leaves behind - which is exactly where the security message used to vanish.
 */
describe('createTnziClient session-expiry chain', () => {
  function jsonResponse(body: Record<string, unknown>, status: number) {
    return {
      ok: status >= 200 && status < 300,
      status,
      statusText: 'x',
      json: () => Promise.resolve(body),
      headers: new Headers(),
    };
  }

  function stubFetch(routes: Record<string, () => unknown>) {
    const fetchMock = vi.fn(async (url: string) => {
      const key = Object.keys(routes).find((k) => String(url).includes(k));
      if (!key) throw new Error(`unexpected fetch ${url}`);
      return routes[key]();
    });
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  }

  function seededClient(storage: StorageAdapter) {
    storage.set('tnzi:auth:token', 'stale-access');
    storage.set('tnzi:auth:refresh', 'r1');
    const client = createTnziClient({ baseUrl: '/api', storage, permissionsFetchFn: null });
    client.auth.isAuthenticated = true;
    client.auth.accessToken = 'stale-access';
    client.auth.refreshToken = 'r1';
    client.http.setAccessToken('stale-access');
    return client;
  }

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('★ keeps the "ended for security reasons" message after onUnauthorized runs', async () => {
    const storage = memStorage();
    const { http, auth } = seededClient(storage);
    stubFetch({
      '/auth/refresh-token': () =>
        jsonResponse(
          { succeeded: false, code: 401, errorCode: 'IDENTITY_REFRESH_TOKEN_REUSED', message: 'Invalid or expired refresh token' },
          401,
        ),
      '/protected': () => jsonResponse({ succeeded: false, code: 401, errorCode: 'UNAUTHORIZED' }, 401),
    });

    const result = await http.get('/protected');

    expect(result.code).toBe(401);
    expect(auth.isAuthenticated).toBe(false);
    expect(http.getAccessToken()).toBeNull();
    expect(auth.error).toContain('security');
    expect(storage.get('tnzi:auth:refresh')).toBeNull();
  });

  /**
   * ★ The bearer-mode BOOT path, which is how a replayed token is usually met
   * (the user reloads the page). `restoreAuth()` fetches the profile first; the
   * expired access token makes that a 401, the HttpClient runs the refresh through
   * `refreshTokenFn`, the backend answers REUSED, `_doRefreshToken` clears the
   * session and sets the security message - and then `restoreAuth`'s own catch
   * retried the refresh, got the plain "No refresh token available" error, and
   * `_clearAfterFailedRestore` keyed the message off THAT error: erased. The
   * manager-only tests mock the profile/auth apis and the http client, so the
   * HttpClient-driven refresh never ran there and the erasure was invisible.
   */
  it('★ bearer boot keeps the "ended for security reasons" message when the profile 401 drives a rejected refresh', async () => {
    const storage = memStorage();
    storage.set('tnzi:auth:token', 'stale-access');
    storage.set('tnzi:auth:refresh', 'r1');
    const { auth, http } = createTnziClient({ baseUrl: '/api', storage, permissionsFetchFn: null });
    const fetchMock = stubFetch({
      '/auth/refresh-token': () =>
        jsonResponse(
          { succeeded: false, code: 401, errorCode: 'IDENTITY_REFRESH_TOKEN_REUSED', message: 'Invalid or expired refresh token' },
          401,
        ),
      '/users/profile': () => jsonResponse({ succeeded: false, code: 401, errorCode: 'UNAUTHORIZED' }, 401),
    });

    await auth.restoreAuth();

    expect(auth.isAuthenticated).toBe(false);
    expect(auth.user).toBeNull();
    expect(http.getAccessToken()).toBeNull();
    expect(storage.get('tnzi:auth:refresh')).toBeNull();
    expect(auth.error).toContain('security');
    // The refresh token was rejected once; the boot must not present it again.
    const refreshCalls = fetchMock.mock.calls.filter(([url]) => String(url).includes('/auth/refresh-token'));
    expect(refreshCalls).toHaveLength(1);
  });

  /**
   * The account's sign-in IP allow-list refused the refresh. The reason has to
   * survive the whole chain (refresh rejection -> onUnauthorized -> the
   * session-expired listener) so the login page can say "sign in from an
   * allowed network" instead of a routine "session expired".
   */
  it('★ a refresh refused by the sign-in IP allow-list leaves "ipNotAllowed" for the login page', async () => {
    const storage = memStorage();
    const { http, auth } = seededClient(storage);
    const unauthorized = vi.fn();
    http.addUnauthorizedListener(unauthorized);
    stubFetch({
      '/auth/refresh-token': () => jsonResponse({ succeeded: false, code: 403, errorCode: 'IDENTITY_SIGN_IN_IP_NOT_ALLOWED', message: 'Your current network is not on the list.' }, 403),
      '/protected': () => jsonResponse({ succeeded: false, code: 401, errorCode: 'UNAUTHORIZED' }, 401),
    });

    await http.get('/protected');

    expect(unauthorized).toHaveBeenCalledTimes(1);
    expect(auth.isAuthenticated).toBe(false);
    expect(storage.get('tnzi:auth:refresh')).toBeNull();
    expect(auth.sessionEndReason).toBe('ipNotAllowed');
  });

  it('bearer boot keeps "ipNotAllowed" when the profile 401 drives a refresh the allow-list refuses', async () => {
    const storage = memStorage();
    storage.set('tnzi:auth:token', 'stale-access');
    storage.set('tnzi:auth:refresh', 'r1');
    const { auth } = createTnziClient({ baseUrl: '/api', storage, permissionsFetchFn: null });
    stubFetch({
      '/auth/refresh-token': () => jsonResponse({ succeeded: false, code: 403, errorCode: 'IDENTITY_SIGN_IN_IP_NOT_ALLOWED', message: 'Your current network is not on the list.' }, 403),
      '/users/profile': () => jsonResponse({ succeeded: false, code: 401, errorCode: 'UNAUTHORIZED' }, 401),
    });

    await auth.restoreAuth();

    expect(auth.isAuthenticated).toBe(false);
    expect(storage.get('tnzi:auth:refresh')).toBeNull();
    expect(auth.sessionEndReason).toBe('ipNotAllowed');
  });

  it('bearer boot stays quiet on an ordinary expiry (rejected refresh without a security code)', async () => {
    const storage = memStorage();
    storage.set('tnzi:auth:token', 'stale-access');
    storage.set('tnzi:auth:refresh', 'r1');
    const { auth } = createTnziClient({ baseUrl: '/api', storage, permissionsFetchFn: null });
    stubFetch({
      '/auth/refresh-token': () =>
        jsonResponse({ succeeded: false, code: 400, message: 'Invalid or expired refresh token' }, 400),
      '/users/profile': () => jsonResponse({ succeeded: false, code: 401, errorCode: 'UNAUTHORIZED' }, 401),
    });

    await auth.restoreAuth();

    expect(auth.isAuthenticated).toBe(false);
    expect(storage.get('tnzi:auth:refresh')).toBeNull();
    expect(auth.error).toBeNull();
  });

  it('a transport failure on refresh signs the tab out but keeps the persisted tokens', async () => {
    const storage = memStorage();
    const { http, auth } = seededClient(storage);
    stubFetch({
      '/auth/refresh-token': () => Promise.reject(new TypeError('Failed to fetch')),
      '/protected': () => jsonResponse({ succeeded: false, code: 401, errorCode: 'UNAUTHORIZED' }, 401),
    });

    const result = await http.get('/protected');

    expect(result.code).toBe(401);
    // This attempt is over: the HttpClient's onUnauthorized cleared the tab.
    expect(auth.isAuthenticated).toBe(false);
    // But the server never rejected the refresh token, so the next boot may retry.
    expect(storage.get('tnzi:auth:refresh')).toBe('r1');
  });

  it('a step-up challenge passes through the whole chain without touching the session', async () => {
    const storage = memStorage();
    const { http, auth } = seededClient(storage);
    const fetchMock = stubFetch({
      '/auth/refresh-token': () => {
        throw new Error('refresh must not be attempted for a step-up challenge');
      },
      '/files/1/original': () =>
        jsonResponse(
          {
            succeeded: false,
            code: 401,
            errorCode: 'IDENTITY_STEP_UP_REQUIRED',
            errorDetails: { scope: 'tip.download' },
          },
          401,
        ),
    });

    const result = await http.post('/files/1/original');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result.errorCode).toBe('IDENTITY_STEP_UP_REQUIRED');
    expect(auth.isAuthenticated).toBe(true);
    expect(http.getAccessToken()).toBe('stale-access');
  });
});
