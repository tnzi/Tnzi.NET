import { describe, it, expect, vi } from 'vitest';
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
    const { auth } = createTnziClient({ storage, storagePrefix: 'acme:auth' });
    const p = auth.applyTokenSession({ accessToken: 'x', refreshToken: 'y', expiresIn: 10 });
    expect(storage.get('acme:auth:token')).toBe('x');
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
