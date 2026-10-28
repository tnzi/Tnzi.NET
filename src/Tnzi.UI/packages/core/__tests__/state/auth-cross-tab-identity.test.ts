import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AuthStateManager, SESSION_EXPIRED_MESSAGE } from '../../src/state/auth';
import { createMemoryStorageAdapter } from '../../src/adapters/storage';
import type { StorageAdapter } from '../../src/adapters/storage';

/**
 * 多标签页 + 换号登录。
 *
 * 所有标签页共享同一组存储键（cookie 模式下是同一个 cookie）。本标签页刷新时会
 * 采用另一个标签页轮换过的刷新令牌，这对「同一个人」是对的；但另一个标签页如果
 * 登出后换成了另一个账号，存储里就是**别人的**会话。采用它 = 本标签页从此以另一个
 * 用户的身份发请求，而界面上的头像、用户名、菜单仍是原来那个人。
 *
 * 判定看的是访问令牌里的用户 id（`nameid`），不是会话 id：同一个人在别的标签页
 * 重新登录会开一条新会话，采用它无害。
 */

function base64Url(value: unknown): string {
  return btoa(JSON.stringify(value)).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');
}

function jwt(claims: Record<string, unknown>): string {
  return `${base64Url({ alg: 'HS256', typ: 'JWT' })}.${base64Url(claims)}.signature`;
}

const USER_A = '0b9f6d2e-1a52-4c4f-9a0e-00000000000a';
const USER_B = '6c1d7e3f-2b63-4d5a-8b1f-00000000000b';

const accessA1 = jwt({ nameid: USER_A, session_id: 'session-a1' });
const accessA2 = jwt({ nameid: USER_A, session_id: 'session-a2' });
const accessA3 = jwt({ nameid: USER_A, session_id: 'session-a2' , jti: 'next' });
const accessB1 = jwt({ nameid: USER_B, session_id: 'session-b' });
const accessB2 = jwt({ nameid: USER_B, session_id: 'session-b', jti: 'rotated' });

const KEYS = { token: 'tnzi:auth:token', refresh: 'tnzi:auth:refresh', expiry: 'tnzi:auth:expiry' };

function tokenEnvelope(accessToken: string, refreshToken: string) {
  return { succeeded: true, code: 200, data: { accessToken, refreshToken, expiresIn: 3600 } };
}

describe('AuthStateManager - another account signed in from another tab', () => {
  let storage: StorageAdapter;
  let post: ReturnType<typeof vi.fn>;
  let setAccessToken: ReturnType<typeof vi.fn>;
  let onLogout: ReturnType<typeof vi.fn>;
  let push: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    storage = createMemoryStorageAdapter();
    post = vi.fn();
    setAccessToken = vi.fn();
    onLogout = vi.fn();
    push = vi.fn();
  });

  function createAuth(tokenDelivery: 'bearer' | 'cookie' = 'bearer') {
    return new AuthStateManager({
      httpClient: { post, get: vi.fn(), setAccessToken } as any,
      storage,
      tokenDelivery,
      onLogout,
      router: { push } as any,
    });
  }

  /** This tab signed in as A; `expired` makes logout refresh first. */
  function seedTab(auth: AuthStateManager, access: string, refresh: string | null, expired = false) {
    auth.isAuthenticated = true;
    auth.accessToken = access;
    auth.refreshToken = refresh;
    auth.tokenExpiry = new Date(Date.now() + (expired ? -60_000 : 3600_000));
    auth.user = { id: USER_A, userName: 'alice', roles: ['User'], permissions: [] } as any;
  }

  /** What another tab's login / rotation leaves in the shared storage. */
  function persistFromOtherTab(access: string, refresh: string) {
    storage.set(KEYS.token, access);
    storage.set(KEYS.refresh, refresh);
    storage.set(KEYS.expiry, new Date(Date.now() + 3600_000).toISOString());
  }

  function refreshCalls() {
    return post.mock.calls.filter(([url]) => String(url).endsWith('/auth/refresh-token'));
  }

  function logoutCalls() {
    return post.mock.calls.filter(([url]) => String(url).endsWith('/auth/logout'));
  }

  describe('bearer delivery', () => {
    it('never presents a refresh token issued to another user, and ends this tab session', async () => {
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1');
      persistFromOtherTab(accessB1, 'refresh-b1');
      post.mockResolvedValue(tokenEnvelope(accessB2, 'refresh-b2'));

      await expect(auth.refreshAccessToken()).rejects.toThrow();

      expect(refreshCalls()).toHaveLength(0);
      expect(setAccessToken).not.toHaveBeenCalledWith(accessB1);
      expect(setAccessToken).not.toHaveBeenCalledWith(accessB2);
      expect(auth.isAuthenticated).toBe(false);
      expect(auth.accessToken).toBeNull();
      expect(auth.user).toBeNull();
      expect(auth.error).toBe(SESSION_EXPIRED_MESSAGE);
      expect(onLogout).toHaveBeenCalledTimes(1);
      expect(push).toHaveBeenCalledWith('/login');
      // The other tab's session is not ours to clear.
      expect(storage.get(KEYS.refresh)).toBe('refresh-b1');
      expect(storage.get(KEYS.token)).toBe(accessB1);
    });

    it('still adopts a token the same user rotated or re-issued in another tab', async () => {
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1');
      persistFromOtherTab(accessA2, 'refresh-a2');
      post.mockResolvedValue(tokenEnvelope(accessA3, 'refresh-a3'));

      await auth.refreshAccessToken();

      expect(refreshCalls()).toHaveLength(1);
      expect(refreshCalls()[0]![1]).toEqual({ refreshToken: 'refresh-a2' });
      expect(auth.accessToken).toBe(accessA3);
      expect(auth.isAuthenticated).toBe(true);
      expect(storage.get(KEYS.refresh)).toBe('refresh-a3');
    });

    it('matches the user id regardless of letter case', async () => {
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1');
      persistFromOtherTab(jwt({ nameid: USER_A.toUpperCase(), session_id: 'x' }), 'refresh-a2');
      post.mockResolvedValue(tokenEnvelope(accessA3, 'refresh-a3'));

      await auth.refreshAccessToken();

      expect(refreshCalls()[0]![1]).toEqual({ refreshToken: 'refresh-a2' });
    });

    it('presents its own token when it cannot tell whose the stored one is', async () => {
      // Opaque tokens carry no user id. Adopting an unidentifiable credential
      // is how a tab ends up acting as somebody else, so it keeps its own.
      const auth = createAuth();
      seedTab(auth, 'opaque-access-a', 'refresh-a1');
      persistFromOtherTab('opaque-access-other', 'refresh-other');
      post.mockResolvedValue(tokenEnvelope('opaque-access-a2', 'refresh-a2'));

      await auth.refreshAccessToken();

      expect(refreshCalls()[0]![1]).toEqual({ refreshToken: 'refresh-a1' });
    });

    it('ends the session and hands the new pair back when the server answers for another user', async () => {
      // Storage looked like ours when it was read, but the refresh token was
      // swapped in between (another tab signing in). The server rotated that
      // user's token; dropping the replacement would leave their tab holding a
      // rotated-away token that the next refresh reports as a replay.
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1');
      persistFromOtherTab(accessA2, 'refresh-a2');
      post.mockResolvedValue(tokenEnvelope(accessB2, 'refresh-b2'));

      await expect(auth.refreshAccessToken()).rejects.toThrow();

      expect(setAccessToken).not.toHaveBeenCalledWith(accessB2);
      expect(auth.isAuthenticated).toBe(false);
      expect(auth.accessToken).toBeNull();
      expect(auth.error).toBe(SESSION_EXPIRED_MESSAGE);
      expect(storage.get(KEYS.refresh)).toBe('refresh-b2');
      expect(storage.get(KEYS.token)).toBe(accessB2);
    });

    it('signing out of a stale tab never refreshes or revokes with the other user\'s token', async () => {
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1', true);
      persistFromOtherTab(accessB1, 'refresh-b1');
      post.mockImplementation(async (url: string) =>
        String(url).endsWith('/auth/refresh-token')
          ? tokenEnvelope(accessB2, 'refresh-b2')
          : { succeeded: false, code: 401 },
      );

      await auth.logout();

      expect(refreshCalls().some(([, body]) => (body as { refreshToken?: string })?.refreshToken === 'refresh-b1')).toBe(false);
      expect(setAccessToken).not.toHaveBeenCalledWith(accessB1);
      expect(setAccessToken).not.toHaveBeenCalledWith(accessB2);
      expect(auth.isAuthenticated).toBe(false);
      // Signing A out of this tab must not sign B out of theirs on reload.
      expect(storage.get(KEYS.refresh)).toBe('refresh-b1');
      expect(storage.get(KEYS.token)).toBe(accessB1);
    });

    it('signing out still clears the persisted session when it is ours', async () => {
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1');
      persistFromOtherTab(accessA1, 'refresh-a1');
      post.mockResolvedValue({ succeeded: true, code: 200 });

      await auth.logout();

      expect(logoutCalls()).toHaveLength(1);
      expect(storage.get(KEYS.refresh)).toBeNull();
      expect(storage.get(KEYS.token)).toBeNull();
    });

    it('a rejected refresh does not clear another user\'s persisted session', async () => {
      // This tab presented its own token (storage was ours when read); while
      // the request was out, another tab signed in as B. The rejection ends
      // A's session here, and only here.
      const auth = createAuth();
      seedTab(auth, accessA1, 'refresh-a1');
      persistFromOtherTab(accessA1, 'refresh-a1');
      post.mockImplementation(async () => {
        persistFromOtherTab(accessB1, 'refresh-b1');
        return { succeeded: false, code: 401, message: 'Invalid refresh token' };
      });

      await expect(auth.refreshAccessToken()).rejects.toThrow();

      expect(auth.isAuthenticated).toBe(false);
      expect(storage.get(KEYS.refresh)).toBe('refresh-b1');
    });
  });

  describe('cookie delivery', () => {
    it('ends this tab session when the shared cookie now belongs to another user', async () => {
      const auth = createAuth('cookie');
      seedTab(auth, accessA1, null);
      post.mockResolvedValue(tokenEnvelope(accessB2, ''));

      await expect(auth.refreshAccessToken()).rejects.toThrow();

      expect(setAccessToken).not.toHaveBeenCalledWith(accessB2);
      expect(auth.isAuthenticated).toBe(false);
      expect(auth.accessToken).toBeNull();
      expect(auth.user).toBeNull();
      expect(auth.error).toBe(SESSION_EXPIRED_MESSAGE);
      expect(onLogout).toHaveBeenCalledTimes(1);
    });

    it('keeps the session when the refresh answers for the same user', async () => {
      const auth = createAuth('cookie');
      seedTab(auth, accessA1, null);
      post.mockResolvedValue(tokenEnvelope(accessA2, ''));

      await auth.refreshAccessToken();

      expect(auth.accessToken).toBe(accessA2);
      expect(auth.isAuthenticated).toBe(true);
    });

    it('a cold boot adopts whoever the cookie belongs to', async () => {
      // No identity yet: nothing to compare against, nothing to protect.
      const auth = createAuth('cookie');
      post.mockResolvedValue(tokenEnvelope(accessB1, ''));

      await auth.refreshAccessToken();

      expect(auth.accessToken).toBe(accessB1);
    });

    it('signing out of a stale tab never revokes with the other user\'s token', async () => {
      const auth = createAuth('cookie');
      seedTab(auth, accessA1, null, true);
      post.mockImplementation(async (url: string) =>
        String(url).endsWith('/auth/refresh-token')
          ? tokenEnvelope(accessB2, '')
          : { succeeded: false, code: 401 },
      );

      await auth.logout();

      expect(setAccessToken).not.toHaveBeenCalledWith(accessB2);
      expect(auth.isAuthenticated).toBe(false);
    });
  });
});
