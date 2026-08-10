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
});
