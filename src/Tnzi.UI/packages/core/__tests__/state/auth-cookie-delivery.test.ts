import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AuthStateManager } from '../../src/state/auth';
import { createMemoryStorageAdapter } from '../../src/adapters/storage';
import type { StorageAdapter } from '../../src/adapters/storage';
import type { HttpClient } from '../../src/http/http';
import { isSessionEndedForSecurity, REFRESH_TOKEN_REUSED } from '../../src/services/identity/session-security';

/**
 * Cookie token delivery.
 *
 * The point of the mode is that the page holds no readable credential: the
 * refresh token lives in an HttpOnly cookie and the access token lives in
 * memory. These tests pin the two ways that can silently regress - writing a
 * token to storage "for convenience", and treating "we hold no refresh token"
 * as a failure when in cookie mode it is the normal state.
 */
describe('AuthStateManager - cookie token delivery', () => {
  let storage: StorageAdapter;
  let post: ReturnType<typeof vi.fn>;
  let get: ReturnType<typeof vi.fn>;
  let httpClient: {
    post: typeof post;
    get: typeof get;
    setAccessToken: ReturnType<typeof vi.fn>;
    delete: ReturnType<typeof vi.fn>;
    put: ReturnType<typeof vi.fn>;
  };

  const tokenEnvelope = {
    succeeded: true,
    code: 200,
    data: { accessToken: 'fresh-access', refreshToken: '', expiresIn: 3600 },
  };

  const profileEnvelope = {
    succeeded: true,
    code: 200,
    data: { id: 'u1', userName: 'alice', roles: ['User'] },
  };

  beforeEach(() => {
    storage = createMemoryStorageAdapter();
    post = vi.fn();
    get = vi.fn();
    httpClient = {
      post,
      get,
      setAccessToken: vi.fn(),
      delete: vi.fn(),
      put: vi.fn(),
    };
  });

  function createAuth(overrides: Record<string, unknown> = {}) {
    return new AuthStateManager({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      httpClient: httpClient as any,
      storage,
      tokenDelivery: 'cookie',
      ...overrides,
    });
  }

  /**
   * Anything outside the manager that issues a session (the invitation
   * acceptance page builds its own `useInvitationApi`) has to know the
   * delivery mode to pass `withCredentials`; the constructor option was
   * otherwise private to the manager.
   */
  it('exposes the delivery mode', () => {
    expect(createAuth().cookieDelivery).toBe(true);
    expect(createAuth({ tokenDelivery: 'bearer' }).cookieDelivery).toBe(false);
    expect(createAuth({ tokenDelivery: undefined }).cookieDelivery).toBe(false);
  });

  it('refreshes without holding a refresh token, and sends an empty body', async () => {
    post.mockResolvedValue(tokenEnvelope);
    const auth = createAuth();

    await auth.refreshAccessToken();

    expect(auth.accessToken).toBe('fresh-access');
    const [url, body] = post.mock.calls[0];
    expect(url).toContain('/auth/refresh-token');
    // The browser carries the token in the cookie; the body carries nothing.
    expect(body).toEqual({});
  });

  it('sends credentials on the refresh call', async () => {
    post.mockResolvedValue(tokenEnvelope);
    const auth = createAuth();

    await auth.refreshAccessToken();

    const options = post.mock.calls[0][2];
    expect(options?.withCredentials).toBe(true);
  });

  it('★ writes nothing to storage - that is the entire point of the mode', async () => {
    post.mockResolvedValue(tokenEnvelope);
    const auth = createAuth();

    await auth.refreshAccessToken();

    expect(storage.keys()).toEqual([]);
    expect(auth.accessToken).toBe('fresh-access');
  });

  it('★ does not overwrite its session flag with the empty body refreshToken', async () => {
    post.mockResolvedValue(tokenEnvelope);
    const auth = createAuth();
    auth.refreshToken = 'legacy-value-from-a-previous-mode';

    await auth.refreshAccessToken();

    expect(auth.refreshToken).toBe('legacy-value-from-a-previous-mode');
  });

  it('restores a session on boot by asking the cookie, not storage', async () => {
    post.mockResolvedValue(tokenEnvelope);
    get.mockResolvedValue(profileEnvelope);
    const auth = createAuth();

    await auth.restoreAuth();

    expect(auth.isAuthenticated).toBe(true);
    expect(auth.user?.userName).toBe('alice');
    expect(post).toHaveBeenCalled();
  });

  it('boot with no cookie is the ordinary signed-out state, not an error', async () => {
    post.mockResolvedValue({ succeeded: false, code: 400, message: 'Invalid or expired refresh token' });
    const auth = createAuth();

    await auth.restoreAuth();

    expect(auth.isAuthenticated).toBe(false);
    expect(auth.accessToken).toBeNull();
  });

  it('★ a session ended for security reasons says so, instead of "expired"', async () => {
    post.mockResolvedValue({
      succeeded: false,
      code: 401,
      errorCode: REFRESH_TOKEN_REUSED,
      message: 'Invalid or expired refresh token',
    });
    const auth = createAuth();

    await expect(auth.refreshAccessToken()).rejects.toThrow();

    expect(auth.error).toContain('security');
  });

  it('an ordinary expiry keeps the ordinary message', async () => {
    post.mockResolvedValue({ succeeded: false, code: 400, message: 'Invalid or expired refresh token' });
    const auth = createAuth();

    await expect(auth.refreshAccessToken()).rejects.toThrow();

    expect(auth.error).toBe('Session expired, please login again');
  });

  it('★ a security-ended session keeps its message across the cookie boot path', async () => {
    // `_restoreFromCookie` clears quietly on the ordinary "no cookie" boot. It
    // must not wipe the one message the user needs to see: that their
    // credentials were replayed from somewhere else.
    post.mockResolvedValue({
      succeeded: false,
      code: 401,
      errorCode: REFRESH_TOKEN_REUSED,
      message: 'Invalid or expired refresh token',
    });
    const auth = createAuth();

    await auth.restoreAuth();

    expect(auth.isAuthenticated).toBe(false);
    expect(auth.error).toContain('security');
  });

  it('boot with no cookie leaves no stale "session expired" message behind', async () => {
    post.mockResolvedValue({ succeeded: false, code: 400, message: 'Invalid or expired refresh token' });
    const auth = createAuth();

    await auth.restoreAuth();

    expect(auth.error).toBeNull();
  });

  it('logout with an expired access token refreshes from the cookie first', async () => {
    const auth = createAuth();
    auth.isAuthenticated = true;
    auth.accessToken = 'expired-access';
    auth.tokenExpiry = new Date(Date.now() - 1000);
    post.mockImplementation(async (url: string) =>
      url.includes('/auth/refresh-token')
        ? tokenEnvelope
        : { succeeded: true, code: 200 },
    );

    await auth.logout();

    const urls = post.mock.calls.map((c) => String(c[0]));
    expect(urls[0]).toContain('/auth/refresh-token');
    expect(post.mock.calls[0][1]).toEqual({});
    expect(urls[1]).toContain('/auth/logout');
    expect(httpClient.setAccessToken).toHaveBeenCalledWith('fresh-access');
    expect(auth.isAuthenticated).toBe(false);
  });
});

/**
 * 每一个能把 `TokenResultDto` 带回来的端点都必须带 credentials：cookie 模式下刷新令牌
 * 走 `Set-Cookie`，跨源 fetch 不带 `include` 浏览器会直接丢掉那枚 cookie ——
 * 登录看起来成功，十几分钟后第一次刷新就「会话过期」。
 */
describe('cookie-aware token-issuing endpoints', () => {
  it('every token-issuing auth endpoint is posted with withCredentials', async () => {
    const post = vi.fn().mockResolvedValue({ succeeded: true, code: 200, data: {} });
    const client = { post, get: vi.fn() } as unknown as HttpClient;
    const { useAuthApi, useInvitationApi } = await import('../../src/services/identity/api');
    const auth = useAuthApi(client, { withCredentials: true });
    const invitation = useInvitationApi(client, { withCredentials: true });

    const calls: Array<[string, () => unknown]> = [
      ['loginWithRefreshToken', () => auth.loginWithRefreshToken({ userName: 'a', password: 'b' })],
      ['refreshToken', () => auth.refreshToken({})],
      ['register', () => auth.register({ userName: 'a', password: 'b' } as never)],
      ['logout', () => auth.logout()],
      ['codeLogin', () => auth.codeLogin({ account: 'a', code: '1' } as never)],
      ['verifyTwoFactor', () => auth.verifyTwoFactor({ tempToken: 't', code: '1' } as never)],
      ['completePasskeyAssertion', () => auth.completePasskeyAssertion({ stateId: 's', credentialJson: '{}' })],
      ['completePendingPasswordChange', () => auth.completePendingPasswordChange({ tempToken: 't', newPassword: 'p' } as never)],
      ['completePendingTotpEnrollment', () => auth.completePendingTotpEnrollment({ tempToken: 't', code: '1' } as never)],
      ['completePendingEmailConfirmation', () => auth.completePendingEmailConfirmation({ tempToken: 't', code: '1' } as never)],
      ['invitation.accept', () => invitation.accept({ token: 't', password: 'p' } as never)],
    ];

    for (const [name, call] of calls) {
      post.mockClear();
      await call();
      const options = post.mock.calls[0]?.[2] as { withCredentials?: boolean; skipAuthRefresh?: boolean } | undefined;
      expect(options?.withCredentials, `${name} must send credentials`).toBe(true);
      expect(options?.skipAuthRefresh, `${name} is an auth-flow request`).toBe(true);
    }
  });

  it('signInWithPasskey forwards the auth api options to the completing call', async () => {
    const publicKeyCredential = {
      parseCreationOptionsFromJSON: vi.fn((json: unknown) => json),
      parseRequestOptionsFromJSON: vi.fn((json: unknown) => json),
    };
    const navigatorStub = {
      credentials: { create: vi.fn(), get: vi.fn(() => Promise.resolve({ toJSON: () => ({ id: 'c' }) })) },
    };
    vi.stubGlobal('PublicKeyCredential', publicKeyCredential);
    vi.stubGlobal('navigator', navigatorStub);
    vi.stubGlobal('window', { PublicKeyCredential: publicKeyCredential, navigator: navigatorStub });
    try {
      const post = vi.fn(async (url: string) =>
        url.includes('assert/begin')
          ? { succeeded: true, code: 200, data: { optionsJson: '{}', stateId: 's' } }
          : { succeeded: true, code: 200, data: { accessToken: 'a', refreshToken: '', expiresIn: 1 } },
      );
      const { signInWithPasskey } = await import('../../src/services/identity/passkey');
      await signInWithPasskey({ post, get: vi.fn() } as unknown as HttpClient, undefined, { withCredentials: true });

      const complete = post.mock.calls.find(([url]) => String(url).includes('assert/complete'));
      expect(complete?.[2]).toEqual(expect.objectContaining({ withCredentials: true, skipAuthRefresh: true }));
    } finally {
      vi.unstubAllGlobals();
    }
  });
});

describe('bearer delivery stays unchanged', () => {
  it('still requires a locally held refresh token', async () => {
    const auth = new AuthStateManager({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      httpClient: { post: vi.fn(), get: vi.fn(), setAccessToken: vi.fn() } as any,
      storage: createMemoryStorageAdapter(),
    });

    await expect(auth.refreshAccessToken()).rejects.toThrow('No refresh token available');
  });

  it('still persists the token pair', async () => {
    const storage = createMemoryStorageAdapter();
    const post = vi.fn().mockResolvedValue({
      succeeded: true,
      code: 200,
      data: { accessToken: 'a', refreshToken: 'r', expiresIn: 3600 },
    });
    const auth = new AuthStateManager({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      httpClient: { post, get: vi.fn(), setAccessToken: vi.fn() } as any,
      storage,
    });
    auth.refreshToken = 'old-r';

    await auth.refreshAccessToken();

    expect(storage.get<string>('tnzi:auth:token')).toBe('a');
    expect(storage.get<string>('tnzi:auth:refresh')).toBe('r');
  });
});

/**
 * 多标签页：另一个标签页轮换掉令牌之后，本标签页不得拿着内存里的旧值去刷新。
 *
 * ★ 后端现在把「已被轮换掉的令牌又出现」判成盗用并撤销**整条会话**。
 * 本标签页的 `refreshToken` 是启动时从 storage 读一次的内存副本 —— 不在使用那一刻
 * 重新读一次，两个标签页开着几小时就会把整个会话打死，而用户什么都没做错。
 */
describe('multi-tab refresh', () => {
  it('presents the token another tab persisted, not its own stale copy', async () => {
    const storage = createMemoryStorageAdapter();
    const post = vi.fn().mockResolvedValue({
      succeeded: true,
      code: 200,
      data: { accessToken: 'a2', refreshToken: 'r3', expiresIn: 3600 },
    });
    const auth = new AuthStateManager({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      httpClient: { post, get: vi.fn(), setAccessToken: vi.fn() } as any,
      storage,
    });

    // 本标签页启动时拿到的那一代
    auth.refreshToken = 'r1';
    // 另一个标签页在此期间轮换过，写进了共享存储
    storage.set('tnzi:auth:refresh', 'r2');

    await auth.refreshAccessToken();

    expect(post.mock.calls[0][1]).toEqual({ refreshToken: 'r2' });
  });

  it('falls back to its own copy when storage has nothing', async () => {
    const storage = createMemoryStorageAdapter();
    const post = vi.fn().mockResolvedValue({
      succeeded: true,
      code: 200,
      data: { accessToken: 'a2', refreshToken: 'r2', expiresIn: 3600 },
    });
    const auth = new AuthStateManager({
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      httpClient: { post, get: vi.fn(), setAccessToken: vi.fn() } as any,
      storage,
    });
    auth.refreshToken = 'only-in-memory';

    await auth.refreshAccessToken();

    expect(post.mock.calls[0][1]).toEqual({ refreshToken: 'only-in-memory' });
  });
});

describe('isSessionEndedForSecurity', () => {
  it('matches both the envelope and the thrown-error shape', () => {
    expect(isSessionEndedForSecurity({ errorCode: REFRESH_TOKEN_REUSED })).toBe(true);
    expect(isSessionEndedForSecurity({ cause: { errorCode: REFRESH_TOKEN_REUSED } })).toBe(true);
  });

  it('does not fire on an ordinary expiry', () => {
    expect(isSessionEndedForSecurity({ errorCode: 'IDENTITY_SESSION_REVOKED' })).toBe(false);
    expect(isSessionEndedForSecurity(null)).toBe(false);
    expect(isSessionEndedForSecurity('nope')).toBe(false);
  });
});
