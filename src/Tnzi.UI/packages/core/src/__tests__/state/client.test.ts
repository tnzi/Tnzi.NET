import { describe, it, expect } from 'vitest';
import { createTnziClient } from '../../state/client';
import { HttpClient } from '../../http/http';
import { AuthStateManager } from '../../state/auth';
import type { StorageAdapter } from '../../adapters/storage';

/** Minimal in-memory StorageAdapter (core tests run in the node env — no localStorage). */
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
    // — so this only nulls the client token if the manager was built with `http`.
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
});
