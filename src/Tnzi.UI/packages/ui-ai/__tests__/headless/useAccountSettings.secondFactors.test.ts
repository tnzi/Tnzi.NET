import { describe, it, expect, vi, beforeEach } from 'vitest';

const registerPasskey = vi.fn();
const isPasskeySupported = vi.fn(() => true);

vi.mock('@tnzi/core/services/identity', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tnzi/core/services/identity')>();
  return {
    ...actual,
    registerPasskey: (...args: unknown[]) => registerPasskey(...args),
    isPasskeySupported: () => isPasskeySupported(),
  };
});

import { useAccountSettings } from '../../src/headless/useAccountSettings';

const KEY = { credentialId: 'k1', name: 'YubiKey', createdAt: '2026-10-01T00:00:00Z', isBackedUp: false };

function status(passkeyEnabled = false) {
  return {
    isEnabled: true,
    supportedTypes: [],
    isTotpEnabled: true,
    methods: [
      { type: 'Email', available: true, enabled: false, isPreferred: false },
      { type: 'Passkey', available: true, enabled: passkeyEnabled, isPreferred: false },
    ],
  };
}

function makeClient(over: Record<string, unknown> = {}, keys: unknown[] = [KEY]) {
  return {
    get: vi.fn(async (url: string) => {
      if (url.endsWith('/two-factor/status')) return { succeeded: true, data: status() };
      if (url.endsWith('/passkey/credentials')) return { succeeded: true, data: keys };
      return { succeeded: true, data: {} };
    }),
    put: vi.fn(async () => ({ succeeded: true })),
    post: vi.fn(async () => ({ succeeded: true, data: null })),
    delete: vi.fn(async () => ({ succeeded: true })),
    ...over,
  } as never;
}

beforeEach(() => {
  registerPasskey.mockReset();
  isPasskeySupported.mockReset();
  isPasskeySupported.mockReturnValue(true);
});

describe('useAccountSettings second factors', () => {
  it('turns the email code on and off through the enable and per-method disable endpoints', async () => {
    const post = vi.fn(async () => ({ succeeded: true, data: null }));
    const a = useAccountSettings({ client: makeClient({ post }) });

    expect(await a.setEmailTwoFactor(true)).toBe(true);
    expect(post).toHaveBeenLastCalledWith('/users/profile/two-factor/enable', { type: 'Email' });

    expect(await a.setEmailTwoFactor(false)).toBe(true);
    expect(post).toHaveBeenLastCalledWith('/users/profile/two-factor/method/disable', { type: 'Email' });
    expect(a.twoFactor.value?.methods).toHaveLength(2);
  });

  it('shows the API message when the server refuses', async () => {
    const post = vi.fn(async () => ({ succeeded: false, message: 'Confirm your email first' }));
    const a = useAccountSettings({ client: makeClient({ post }) });

    expect(await a.setEmailTwoFactor(true)).toBe(false);
    expect(a.error.value).toBe('Confirm your email first');
  });

  it('lists passkeys and reads a failing listing as none', async () => {
    const a = useAccountSettings({ client: makeClient() });
    await a.loadPasskeys();
    expect(a.passkeys.value).toEqual([KEY]);

    const broken = useAccountSettings({
      client: makeClient({ get: vi.fn(async () => { throw new Error('404'); }) }),
    });
    await broken.loadPasskeys();
    expect(broken.passkeys.value).toEqual([]);
  });

  it('registers a passkey and then allows it as a second factor', async () => {
    registerPasskey.mockResolvedValue(KEY);
    const post = vi.fn(async () => ({ succeeded: true, data: null }));
    const a = useAccountSettings({ client: makeClient({ post }) });
    await a.load();

    expect(await a.addPasskey()).toBe(true);
    expect(registerPasskey).toHaveBeenCalledTimes(1);
    expect(post).toHaveBeenCalledWith('/users/profile/two-factor/enable', { type: 'Passkey' });
    expect(a.passkeys.value).toEqual([KEY]);
  });

  it('does not enable the method again when it is already allowed', async () => {
    registerPasskey.mockResolvedValue(KEY);
    const post = vi.fn(async () => ({ succeeded: true, data: null }));
    const get = vi.fn(async (url: string) => {
      if (url.endsWith('/two-factor/status')) return { succeeded: true, data: status(true) };
      return { succeeded: true, data: [KEY] };
    });
    const a = useAccountSettings({ client: makeClient({ post, get }) });
    await a.load();

    expect(await a.addPasskey()).toBe(true);
    expect(post).not.toHaveBeenCalled();
  });

  it('treats a dismissed system dialog as neither success nor error', async () => {
    registerPasskey.mockResolvedValue(null);
    const post = vi.fn(async () => ({ succeeded: true, data: null }));
    const a = useAccountSettings({ client: makeClient({ post }) });

    expect(await a.addPasskey()).toBe(false);
    expect(a.error.value).toBeNull();
    expect(post).not.toHaveBeenCalled();
  });

  it('reports a failed ceremony', async () => {
    registerPasskey.mockRejectedValue(new Error('This key is already registered'));
    const a = useAccountSettings({ client: makeClient() });

    expect(await a.addPasskey()).toBe(false);
    expect(a.error.value).toBe('This key is already registered');
  });

  it('removes a passkey and refreshes the list and the status', async () => {
    const del = vi.fn(async () => ({ succeeded: true }));
    const a = useAccountSettings({ client: makeClient({ delete: del }, []) });

    expect(await a.removePasskey('k/1')).toBe(true);
    expect(del).toHaveBeenCalledWith('/auth/passkey/credentials/k%2F1');
    expect(a.passkeys.value).toEqual([]);
  });

  it('reports whether the browser can run the ceremony', () => {
    isPasskeySupported.mockReturnValue(false);
    expect(useAccountSettings({ client: makeClient() }).passkeySupported).toBe(false);
  });

  it('is inert without a client', async () => {
    const a = useAccountSettings();
    expect(await a.setEmailTwoFactor(true)).toBe(false);
    expect(await a.addPasskey()).toBe(false);
    expect(await a.removePasskey('k1')).toBe(false);
  });
});
