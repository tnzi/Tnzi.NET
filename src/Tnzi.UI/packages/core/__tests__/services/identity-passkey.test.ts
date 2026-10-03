import { describe, it, expect, afterEach, vi } from 'vitest';
import type { HttpClient } from '../../src/http/http';
import {
  defaultPasskeyName,
  isPasskeySupported,
  registerPasskey,
  signInWithPasskey,
  verifyTwoFactorWithPasskey,
  PasskeyUnsupportedError,
} from '../../src/services/identity/passkey';

// ---------------------------------------------------------------------------
// The ceremony is a chain of four things that must happen in order: capability
// check -> begin -> navigator.credentials -> complete. These tests pin the
// places where a broken link is silent rather than loud.
// ---------------------------------------------------------------------------

type Envelope<T> = { succeeded: boolean; data?: T | null; message?: string };

function ok<T>(data: T): Promise<Envelope<T>> {
  return Promise.resolve({ succeeded: true, data });
}

function fail(message: string): Promise<Envelope<never>> {
  return Promise.resolve({ succeeded: false, message });
}

function createClient(overrides: Partial<Record<'get' | 'post' | 'delete', unknown>> = {}) {
  return {
    get: vi.fn(() => ok(null)),
    post: vi.fn(() => ok(null)),
    put: vi.fn(() => ok(null)),
    patch: vi.fn(() => ok(null)),
    delete: vi.fn(() => ok(null)),
    ...overrides,
  } as unknown as HttpClient;
}

/**
 * Install the JSON bridges plus a `navigator.credentials` that returns `result`.
 *
 * Stubs both `window.PublicKeyCredential` (what the capability check reads) and the
 * bare global (what the ceremony calls). They are the same object in a browser; the
 * suite runs on the node environment, where they have to be wired by hand.
 */
function givenBrowserSupportsPasskeys(result: unknown) {
  const parseCreationOptionsFromJSON = vi.fn((json: unknown) => json);
  const parseRequestOptionsFromJSON = vi.fn((json: unknown) => json);
  const publicKeyCredential = { parseCreationOptionsFromJSON, parseRequestOptionsFromJSON };

  const create = vi.fn(() => Promise.resolve(result));
  const get = vi.fn(() => Promise.resolve(result));
  const navigatorStub = { credentials: { create, get } };

  vi.stubGlobal('PublicKeyCredential', publicKeyCredential);
  vi.stubGlobal('navigator', navigatorStub);
  vi.stubGlobal('window', { PublicKeyCredential: publicKeyCredential, navigator: navigatorStub });

  return { create, get, parseCreationOptionsFromJSON, parseRequestOptionsFromJSON };
}

/** A credential whose `toJSON()` bridge exists, as the real one has. */
function fakeCredential() {
  return { toJSON: () => ({ id: 'cred-1', type: 'public-key' }) };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('isPasskeySupported', () => {
  it('reports unsupported when PublicKeyCredential exists but the JSON bridges do not', () => {
    // The credential type shipped years before parseCreationOptionsFromJSON. Testing
    // for it alone reports "supported" on browsers that then throw at parse time -
    // halfway through the ceremony, after the user has already been prompted.
    const bridgeless = function PublicKeyCredentialStub() {};
    vi.stubGlobal('window', { PublicKeyCredential: bridgeless });

    expect(isPasskeySupported()).toBe(false);
  });

  it('reports supported once both bridges are present', () => {
    givenBrowserSupportsPasskeys(fakeCredential());

    expect(isPasskeySupported()).toBe(true);
  });
});

describe('registerPasskey', () => {
  it('throws PasskeyUnsupportedError before touching the network', async () => {
    vi.stubGlobal('window', {});
    const client = createClient();

    await expect(registerPasskey(client)).rejects.toBeInstanceOf(PasskeyUnsupportedError);
    expect(client.post).not.toHaveBeenCalled();
  });

  it('does not prompt the user when `begin` failed', async () => {
    const { create } = givenBrowserSupportsPasskeys(fakeCredential());
    const client = createClient({ post: vi.fn(() => fail('Passkey is not enabled')) });

    // Falling through to navigator.credentials here would replace the server's
    // actual message with an opaque browser error, and prompt for a ceremony
    // that cannot possibly complete.
    await expect(registerPasskey(client)).rejects.toThrow('Passkey is not enabled');
    expect(create).not.toHaveBeenCalled();
  });

  it('returns null when the user dismisses the system dialog', async () => {
    givenBrowserSupportsPasskeys(null);
    const post = vi.fn(() => ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' }));
    const client = createClient({ post });

    // Dismissal is a normal outcome, not an error - and `complete` must not be called.
    await expect(registerPasskey(client)).resolves.toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('★ rethrows a step-up challenge on `begin` with its code and scope intact', async () => {
    // With Identity:StepUp on, the signed-in `begin` leg answers
    // IDENTITY_STEP_UP_REQUIRED. `withStepUp` matches on `errorCode`, so a
    // bare Error(message) here left the user with "This action requires
    // re-authentication" and no way forward.
    const { create } = givenBrowserSupportsPasskeys(fakeCredential());
    const client = createClient({
      post: vi.fn(() =>
        Promise.resolve({
          succeeded: false,
          code: 401,
          message: 'This action requires re-authentication',
          errorCode: 'IDENTITY_STEP_UP_REQUIRED',
          errorDetails: { scope: 'identity.loginmethod.manage' },
        }),
      ),
    });

    await expect(registerPasskey(client)).rejects.toMatchObject({
      errorCode: 'IDENTITY_STEP_UP_REQUIRED',
      details: { scope: 'identity.loginmethod.manage' },
      message: 'This action requires re-authentication',
    });
    expect(create).not.toHaveBeenCalled();
  });

  it('posts the begin state handle back with the credential', async () => {
    givenBrowserSupportsPasskeys(fakeCredential());
    const post = vi.fn((url: string) =>
      url.endsWith('/register/begin')
        ? ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' })
        : ok({ credentialId: 'cred-1', createdAt: '2026-08-16T00:00:00Z', isBackedUp: false }),
    );
    const client = createClient({ post });

    const credential = await registerPasskey(client, { deviceName: 'My iPhone' });

    expect(credential?.credentialId).toBe('cred-1');
    const completeCall = post.mock.calls.find(([url]) => (url as string).endsWith('/register/complete'));
    expect(completeCall).toBeDefined();
    // The handle is single-use and server-side; losing it turns every completion
    // into "invalid or expired challenge".
    expect((completeCall![1] as { stateId: string }).stateId).toBe('state-1');
    expect((completeCall![1] as { deviceName?: string }).deviceName).toBe('My iPhone');
  });

  /**
   * A credential registered without a name is stored under one derived from the
   * ceremony, so the key list never fills with "Unnamed" entries the user cannot
   * tell apart when one has to go.
   */
  it('names an unnamed credential after what the ceremony knows', async () => {
    const { create } = givenBrowserSupportsPasskeys({ ...fakeCredential(), authenticatorAttachment: 'cross-platform' });
    const post = vi.fn((url: string) =>
      url.endsWith('/register/begin')
        ? ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' })
        : ok({ credentialId: 'cred-1', createdAt: '2026-08-16T00:00:00Z', isBackedUp: false }),
    );

    await registerPasskey(createClient({ post }));

    expect(create).toHaveBeenCalledTimes(1);
    const completeCall = post.mock.calls.find(([url]) => (url as string).endsWith('/register/complete'));
    expect((completeCall![1] as { deviceName?: string }).deviceName).toBe('Security key');
  });
});

describe('defaultPasskeyName', () => {
  it('calls a roaming authenticator a security key whatever the platform', () => {
    expect(defaultPasskeyName({ authenticatorAttachment: 'cross-platform' }, { userAgent: 'Windows NT 10.0' })).toBe('Security key');
  });

  it('names a platform authenticator after the device it was created on', () => {
    expect(defaultPasskeyName({ authenticatorAttachment: 'platform' }, { userAgentData: { platform: 'Windows' } })).toBe('Windows passkey');
    expect(defaultPasskeyName({ authenticatorAttachment: 'platform' }, { userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)' })).toBe('iPhone passkey');
    expect(defaultPasskeyName({ authenticatorAttachment: 'platform' }, { userAgent: 'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0)' })).toBe('Mac passkey');
    expect(defaultPasskeyName({ authenticatorAttachment: 'platform' }, { userAgent: 'Mozilla/5.0 (Linux; Android 14)' })).toBe('Android passkey');
  });

  it('still gives a usable label when nothing is known', () => {
    expect(defaultPasskeyName({}, undefined)).toBe('Passkey');
    expect(defaultPasskeyName({ authenticatorAttachment: null }, { userAgent: 'curl/8' })).toBe('Passkey');
  });
});

describe('verifyTwoFactorWithPasskey', () => {
  it('binds both legs to the temp token, never to a username', async () => {
    givenBrowserSupportsPasskeys(fakeCredential());
    const post = vi.fn((url: string) =>
      url.endsWith('/verify-2fa/passkey/begin')
        ? ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-2' })
        : ok({ accessToken: 'at' }),
    );
    const client = createClient({ post });

    const result = await verifyTwoFactorWithPasskey(client, 'tmp-token');

    expect(result?.accessToken).toBe('at');
    expect(post.mock.calls[0][0]).toMatch(/\/verify-2fa\/passkey\/begin$/);
    expect(post.mock.calls[0][1]).toEqual({ tempToken: 'tmp-token' });
    expect(post.mock.calls[1][0]).toMatch(/\/verify-2fa\/passkey\/complete$/);
    expect(post.mock.calls[1][1]).toEqual({
      tempToken: 'tmp-token',
      stateId: 'state-2',
      credentialJson: JSON.stringify({ id: 'cred-1', type: 'public-key' }),
    });
  });

  it('returns null when the user dismisses the system dialog, without completing', async () => {
    givenBrowserSupportsPasskeys(null);
    const post = vi.fn(() => ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-2' }));
    const client = createClient({ post });

    expect(await verifyTwoFactorWithPasskey(client, 'tmp-token')).toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('throws PasskeyUnsupportedError before touching the network', async () => {
    vi.stubGlobal('window', {});
    const post = vi.fn();

    await expect(verifyTwoFactorWithPasskey(createClient({ post }), 'tmp-token')).rejects.toBeInstanceOf(PasskeyUnsupportedError);
    expect(post).not.toHaveBeenCalled();
  });
});

describe('signInWithPasskey', () => {
  it('omits the body entirely for a discoverable-credential flow', async () => {
    givenBrowserSupportsPasskeys(null);
    const post = vi.fn(() => ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' }));
    const client = createClient({ post });

    await signInWithPasskey(client);

    // No username at all: the login page needs no username field, and the server
    // is not handed an empty string to interpret.
    expect(post.mock.calls[0][1]).toEqual({});
  });

  it('passes the username through when one is given', async () => {
    givenBrowserSupportsPasskeys(null);
    const post = vi.fn(() => ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' }));
    const client = createClient({ post });

    await signInWithPasskey(client, 'someone');

    expect(post.mock.calls[0][1]).toEqual({ userName: 'someone' });
  });

  it('surfaces the server message when assertion is rejected', async () => {
    givenBrowserSupportsPasskeys(fakeCredential());
    const post = vi.fn((url: string) =>
      url.endsWith('/assert/begin')
        ? ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' })
        : fail('Two-factor authentication is required'),
    );
    const client = createClient({ post });

    // Passkey sign-in shares the password login exit, so a 2FA challenge comes
    // back here as a failed envelope - the caller has to see the real message to
    // continue with the existing 2FA flow instead of reporting a passkey error.
    await expect(signInWithPasskey(client)).rejects.toThrow('Two-factor authentication is required');
  });
});

// ---------------------------------------------------------------------------
// A real browser never resolves the ceremony with `null` on cancel: it rejects
// with a `NotAllowedError` DOMException (also on timeout). The `null` contract
// the callers rely on ("dismissed, not failed") only holds if that rejection is
// mapped here; otherwise every cancel surfaces the browser's own error text.
// ---------------------------------------------------------------------------
describe('a dismissed ceremony (NotAllowedError)', () => {
  function givenUserCancels(error: unknown = new DOMException('The operation either timed out or was not allowed.', 'NotAllowedError')) {
    const stubs = givenBrowserSupportsPasskeys(null);
    stubs.create.mockImplementation(() => Promise.reject(error));
    stubs.get.mockImplementation(() => Promise.reject(error));
    return stubs;
  }

  const begun = () => vi.fn(() => ok({ optionsJson: '{"challenge":"x"}', stateId: 'state-1' }));

  it('registerPasskey resolves null and does not complete', async () => {
    givenUserCancels();
    const post = begun();

    await expect(registerPasskey(createClient({ post }))).resolves.toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('verifyTwoFactorWithPasskey resolves null and does not complete', async () => {
    givenUserCancels();
    const post = begun();

    await expect(verifyTwoFactorWithPasskey(createClient({ post }), 'tmp-token')).resolves.toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('signInWithPasskey resolves null and does not complete', async () => {
    givenUserCancels();
    const post = begun();

    await expect(signInWithPasskey(createClient({ post }))).resolves.toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it('still rejects on any other browser error (e.g. an already-registered authenticator)', async () => {
    const alreadyRegistered = new DOMException('The authenticator was previously registered.', 'InvalidStateError');
    givenUserCancels(alreadyRegistered);

    await expect(registerPasskey(createClient({ post: begun() }))).rejects.toBe(alreadyRegistered);
  });
});
