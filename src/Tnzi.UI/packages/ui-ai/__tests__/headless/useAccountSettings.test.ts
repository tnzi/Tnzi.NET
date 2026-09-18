import { describe, it, expect, vi } from 'vitest';
import { useAccountSettings } from '../../src/headless/useAccountSettings';

const USER = { id: 'u1', userName: 'ada', nickname: 'Ada', email: 'a@b.c', phoneNumber: '123' };
const TWO_FACTOR = { isEnabled: false, supportedTypes: [], isTotpEnabled: false, methods: [] };

function makeClient(over: Record<string, unknown> = {}) {
  return {
    get: vi.fn(async (url: string) => {
      if (url.endsWith('/two-factor/status')) return { succeeded: true, data: TWO_FACTOR };
      if (url.endsWith('/sessions')) return { succeeded: true, data: [] };
      return { succeeded: true, data: USER };
    }),
    put: vi.fn(async () => ({ succeeded: true, data: USER })),
    post: vi.fn(async () => ({ succeeded: true, data: null })),
    delete: vi.fn(async () => ({ succeeded: true, data: null })),
    ...over,
  } as never;
}

describe('useAccountSettings', () => {
  it('is unavailable and inert without a client', async () => {
    const a = useAccountSettings();
    expect(a.available.value).toBe(false);
    await a.load();
    expect(await a.saveProfile()).toBe(false);
    expect(await a.changePassword('a', 'b')).toBe(false);
    expect(a.profile.value).toBeNull();
  });

  it('loads profile and two-factor status together', async () => {
    const a = useAccountSettings({ client: makeClient() });
    await a.load();

    expect(a.profile.value?.nickname).toBe('Ada');
    expect(a.draft.value.email).toBe('a@b.c');
    expect(a.twoFactor.value?.isTotpEnabled).toBe(false);
    expect(a.loading.value).toBe(false);
  });

  it('a failing two-factor probe still shows the profile', async () => {
    /* A deployment with 2FA off must not blank the account page. */
    const client = makeClient({
      get: vi.fn(async (url: string) => {
        if (url.endsWith('/two-factor/status')) throw new Error('404');
        return { succeeded: true, data: USER };
      }),
    });
    const a = useAccountSettings({ client });
    await a.load();

    expect(a.profile.value?.nickname).toBe('Ada');
    expect(a.twoFactor.value).toBeNull();
  });

  it('saves only the nickname', async () => {
    /* Email and phone are verify-code flows, not field edits. Sending them
       here would let a user type a new address and believe it took effect. */
    const put = vi.fn(async () => ({ succeeded: true, data: USER }));
    const a = useAccountSettings({ client: makeClient({ put }) });
    await a.load();

    a.draft.value = { nickname: '  Ada L.  ', email: 'evil@x.y', phoneNumber: '999' };
    await a.saveProfile();

    expect(put).toHaveBeenCalledWith('/users/profile', { nickname: 'Ada L.' });
  });

  it('sends the TOTP field name the API declares', async () => {
    /* `EnableTotpDto.verificationCode` - a cast here once hid the wrong name,
       which fails only against a real backend. */
    const post = vi.fn(async () => ({ succeeded: true, data: null }));
    const a = useAccountSettings({ client: makeClient({ post }) });

    await a.confirmTotp('123456');

    expect(post).toHaveBeenCalledWith('/users/profile/two-factor/totp/enable', {
      verificationCode: '123456',
    });
  });

  it('drops the TOTP secret once it is confirmed', async () => {
    const client = makeClient({
      post: vi.fn(async (url: string) => {
        if (url.endsWith('/totp/setup')) {
          return { succeeded: true, data: { sharedKey: 'S3CRET', authenticatorUri: 'otpauth://x' } };
        }
        return { succeeded: true, data: null };
      }),
    });
    const a = useAccountSettings({ client });

    await a.beginTotp();
    expect(a.totpSetup.value?.sharedKey).toBe('S3CRET');

    await a.confirmTotp('123456');
    expect(a.totpSetup.value).toBeNull();
  });

  it('a failed setup does not leave a stale secret on screen', async () => {
    const client = makeClient({
      post: vi.fn(async () => {
        throw new Error('rate limited');
      }),
    });
    const a = useAccountSettings({ client });

    expect(await a.beginTotp()).toBe(false);
    expect(a.totpSetup.value).toBeNull();
    expect(a.error.value).toBe('rate limited');
  });

  it('security writes surface failure instead of reporting success', async () => {
    const a = useAccountSettings({
      client: makeClient({ post: vi.fn(async () => ({ succeeded: false, message: 'Wrong password' })) }),
    });

    expect(await a.changePassword('bad', 'new')).toBe(false);
    expect(a.error.value).toBe('Wrong password');
    expect(a.busy.value).toBe(false);
  });

  it('suspend keeps methods configured, refreshing status afterwards', async () => {
    const get = vi.fn(async (url: string) => {
      if (url.endsWith('/two-factor/status')) {
        return { succeeded: true, data: { ...TWO_FACTOR, isEnabled: false, isTotpEnabled: true } };
      }
      return { succeeded: true, data: USER };
    });
    const post = vi.fn(async () => ({ succeeded: true, data: null }));
    const a = useAccountSettings({ client: makeClient({ get, post }) });

    expect(await a.suspendTwoFactor()).toBe(true);
    expect(post).toHaveBeenCalledWith('/users/profile/two-factor/suspend');
    /* TOTP stays configured - that is the whole difference from `disable`. */
    expect(a.twoFactor.value?.isTotpEnabled).toBe(true);
    expect(a.twoFactor.value?.isEnabled).toBe(false);
  });

  it('tracks dirty against loaded state and resets to it', async () => {
    const a = useAccountSettings({ client: makeClient() });
    await a.load();
    expect(a.dirty.value).toBe(false);

    a.draft.value = { ...a.draft.value, nickname: 'Other' };
    expect(a.dirty.value).toBe(true);

    a.resetDraft();
    expect(a.draft.value.nickname).toBe('Ada');
    expect(a.dirty.value).toBe(false);
  });

  it('revoking a session reloads the list', async () => {
    const del = vi.fn(async () => ({ succeeded: true, data: null }));
    const get = vi.fn(async (url: string) => {
      if (url.endsWith('/sessions')) return { succeeded: true, data: [{ id: 's1' }] };
      if (url.endsWith('/two-factor/status')) return { succeeded: true, data: TWO_FACTOR };
      return { succeeded: true, data: USER };
    });
    const a = useAccountSettings({ client: makeClient({ delete: del, get }) });

    expect(await a.revokeSession('s1')).toBe(true);
    expect(del).toHaveBeenCalledWith('/users/profile/sessions/s1');
    expect(a.sessions.value).toHaveLength(1);
  });

  /**
   * Since 2026-08-31 the backend excludes the caller's own session unless asked
   * (`?includeCurrent=true`). The hook's default therefore means "sign out the
   * OTHER devices"; the UI copy must say exactly that, and the flag has to be
   * passed through when a true sign-out-everywhere is wanted.
   */
  describe('revokeAllSessions', () => {
    it('keeps the current session by default', async () => {
      const del = vi.fn(async () => ({ succeeded: true, data: null }));
      const a = useAccountSettings({ client: makeClient({ delete: del }) });

      expect(await a.revokeAllSessions()).toBe(true);
      expect(del).toHaveBeenCalledWith('/users/profile/sessions');
    });

    it('passes includeCurrent through when asked to end this session too', async () => {
      const del = vi.fn(async () => ({ succeeded: true, data: null }));
      const a = useAccountSettings({ client: makeClient({ delete: del }) });

      expect(await a.revokeAllSessions(true)).toBe(true);
      expect(del).toHaveBeenCalledWith('/users/profile/sessions?includeCurrent=true');
    });

    it('reloads the list afterwards', async () => {
      const get = vi.fn(async (url: string) => {
        if (url.endsWith('/sessions')) return { succeeded: true, data: [{ id: 'mine' }] };
        if (url.endsWith('/two-factor/status')) return { succeeded: true, data: TWO_FACTOR };
        return { succeeded: true, data: USER };
      });
      const a = useAccountSettings({ client: makeClient({ get }) });

      await a.revokeAllSessions();
      expect(a.sessions.value.map((s) => s.id)).toEqual(['mine']);
    });
  });

  /**
   * With `Identity:StepUp:Enabled=true` the backend answers the two-factor,
   * contact-change and account-destroy writes with `IDENTITY_STEP_UP_REQUIRED`
   * + a scope. Every write funnels through `write()`, so the challenge ->
   * verify -> replay-once loop (core's `withStepUp`) is closed there once.
   */
  describe('step-up', () => {
    const CHALLENGE = {
      succeeded: false,
      code: 401,
      message: 'This action requires re-authentication',
      errorCode: 'IDENTITY_STEP_UP_REQUIRED',
      errorDetails: { scope: 'identity.two-factor.manage' },
    };
    const GRANT = { scope: 'identity.two-factor.manage', expiresAt: '2030-01-01T00:00:00Z', singleUse: false };

    function challengedOnce() {
      let calls = 0;
      return vi.fn(async (url: string) => {
        if (!url.endsWith('/two-factor/totp/disable')) return { succeeded: true, data: null };
        calls += 1;
        return calls === 1 ? CHALLENGE : { succeeded: true, data: null };
      });
    }

    it('★ a challenged write asks the verifier for the scope and replays once', async () => {
      const post = challengedOnce();
      const stepUp = vi.fn(async () => GRANT);
      const a = useAccountSettings({ client: makeClient({ post }), stepUp });

      expect(await a.disableTotp()).toBe(true);

      expect(stepUp).toHaveBeenCalledWith('identity.two-factor.manage');
      expect(post.mock.calls.filter(([url]) => url.endsWith('/two-factor/totp/disable'))).toHaveLength(2);
      expect(a.error.value).toBeNull();
    });

    it('cancelling reports the challenge as the failure, not a silent success', async () => {
      const post = challengedOnce();
      const a = useAccountSettings({ client: makeClient({ post }), stepUp: async () => null });

      expect(await a.disableTotp()).toBe(false);
      expect(a.error.value).toBe('This action requires re-authentication');
      expect(post.mock.calls.filter(([url]) => url.endsWith('/two-factor/totp/disable'))).toHaveLength(1);
    });

    it('a non-challenge failure never asks the verifier', async () => {
      const post = vi.fn(async () => ({ succeeded: false, message: 'Not enabled' }));
      const stepUp = vi.fn(async () => GRANT);
      const a = useAccountSettings({ client: makeClient({ post }), stepUp });

      expect(await a.disableTotp()).toBe(false);
      expect(stepUp).not.toHaveBeenCalled();
    });

    it('ships a built-in prompt controller when no verifier is supplied', async () => {
      const post = challengedOnce();
      const a = useAccountSettings({ client: makeClient({ post }) });
      expect(a.stepUp).not.toBeNull();

      const pending = a.disableTotp();
      for (let i = 0; i < 5; i += 1) await Promise.resolve();
      expect(a.stepUp!.open).toBe(true);
      expect(a.stepUp!.scope).toBe('identity.two-factor.manage');

      a.stepUp!.cancel();
      expect(await pending).toBe(false);
    });

    it('has no prompt without a client', () => {
      expect(useAccountSettings().stepUp).toBeNull();
    });
  });

  /**
   * The session list has no "this is you" marker, but the access token the
   * backend issued does (its `session_id` claim). Reading it lets the page
   * label the caller's row and keep a one-click Revoke from signing the user
   * out of the page they are on.
   */
  describe('current session', () => {
    const b64 = (s: string) => Buffer.from(s).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    const jwtWith = (sessionId: string) => `${b64('{"alg":"HS256"}')}.${b64(JSON.stringify({ sub: 'u1', session_id: sessionId }))}.sig`;
    const SESSIONS = [{ id: 'S-AAA' }, { id: 's-bbb' }, { id: 's-ccc' }];

    function clientWithSessions(token: string | null) {
      return makeClient({
        getAccessToken: () => token,
        get: vi.fn(async (url: string) => {
          if (url.endsWith('/sessions')) return { succeeded: true, data: SESSIONS };
          if (url.endsWith('/two-factor/status')) return { succeeded: true, data: TWO_FACTOR };
          return { succeeded: true, data: USER };
        }),
      });
    }

    it('reads the session id off the access token and marks that row (case-insensitively)', async () => {
      const a = useAccountSettings({ client: clientWithSessions(jwtWith('s-aaa')) });
      await a.loadSessions();

      expect(a.currentSessionId.value).toBe('s-aaa');
      expect(a.isCurrentSession(SESSIONS[0]! as never)).toBe(true);
      expect(a.isCurrentSession(SESSIONS[1]! as never)).toBe(false);
      expect(a.otherSessions.value.map((s) => s.id)).toEqual(['s-bbb', 's-ccc']);
    });

    it("★ with only the caller's own session listed there is nothing else to sign out", async () => {
      const client = makeClient({
        getAccessToken: () => jwtWith('s-aaa'),
        get: vi.fn(async (url: string) => {
          if (url.endsWith('/sessions')) return { succeeded: true, data: [{ id: 's-aaa' }] };
          return { succeeded: true, data: USER };
        }),
      });
      const a = useAccountSettings({ client });
      await a.loadSessions();

      expect(a.sessions.value).toHaveLength(1);
      expect(a.otherSessions.value).toHaveLength(0);
    });

    it('cannot tell without a session-bound token: no row is marked, every row counts as "other"', async () => {
      const a = useAccountSettings({ client: clientWithSessions('opaque-token') });
      await a.loadSessions();

      expect(a.currentSessionId.value).toBeNull();
      expect(SESSIONS.some((s) => a.isCurrentSession(s as never))).toBe(false);
      expect(a.otherSessions.value).toHaveLength(3);
    });
  });
});
