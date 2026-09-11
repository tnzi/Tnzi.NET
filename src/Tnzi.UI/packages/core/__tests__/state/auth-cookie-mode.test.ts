import { describe, it, expect, vi } from 'vitest';
import { AuthStateManager } from '../../src/state/auth';
import { createMemoryStorageAdapter } from '../../src/adapters/storage';
import type { HttpClient } from '../../src/http/http';
import type { StateDeps } from '../../src/state/types';

/**
 * Two cookie-delivery edge cases the bearer-mode suite cannot see.
 *
 * 1. A deployment that switches from bearer to cookie delivery leaves the old
 *    token pair in web storage: cookie mode never reads those keys, so nothing
 *    would ever clear them. `restoreAuth()` must scrub them - keeping
 *    credentials out of storage is the point of the mode.
 * 2. `logout()` used to call the server only when a profile was loaded. In
 *    cookie mode the HttpOnly refresh cookie is a credential only the server
 *    can revoke, and a failed profile fetch says nothing about whether the
 *    session is alive - so the revoke must run whenever we hold a token.
 */

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
    tokenDelivery: 'cookie',
    ...overrides,
  };
}

describe('AuthStateManager in cookie delivery mode', () => {
  it('restoreAuth scrubs any token pair a bearer-mode deployment left in storage', async () => {
    const deps = createDeps();
    const removeSpy = vi.spyOn(deps.storage, 'remove');
    // No cookie on this "browser": the refresh call fails, which is the normal
    // signed-out boot. The failure path clears storage too, so counting removes
    // at the end cannot tell the scrub apart from the failure cleanup (the first
    // version of this test did exactly that and stayed green with the scrub
    // deleted). Capture how many keys were already gone at the moment the
    // refresh request went out: the scrub must precede the server round-trip.
    let removedBeforeRefresh = -1;
    (deps.httpClient.post as ReturnType<typeof vi.fn>).mockImplementation(async () => {
      removedBeforeRefresh = removeSpy.mock.calls.length;
      return { succeeded: false, success: false, code: 401 };
    });

    const auth = new AuthStateManager(deps);
    await auth.restoreAuth();

    // clearPersistedTokens removes exactly the three persisted keys (access /
    // refresh / expiry).
    expect(removedBeforeRefresh).toBe(3);
    expect(new Set(removeSpy.mock.calls.slice(0, 3).map((c) => c[0])).size).toBe(3);
    expect(auth.isAuthenticated).toBe(false);
  });

  it('logout revokes on the server whenever a token is held, even without a profile', async () => {
    const deps = createDeps();
    (deps.httpClient.post as ReturnType<typeof vi.fn>).mockResolvedValue({ succeeded: true, success: true, code: 200 });
    const auth = new AuthStateManager(deps);
    auth.accessToken = 'live-access-token';
    expect(auth.user).toBeNull();

    await auth.logout();

    const urls = (deps.httpClient.post as ReturnType<typeof vi.fn>).mock.calls.map((c) => String(c[0]));
    expect(urls.some((u) => /logout/.test(u))).toBe(true);
    expect(auth.accessToken).toBeNull();
  });

  it('logout skips the server call when nothing is held at all', async () => {
    const deps = createDeps();
    const auth = new AuthStateManager(deps);
    await auth.logout();
    expect(deps.httpClient.post).not.toHaveBeenCalled();
  });
});
