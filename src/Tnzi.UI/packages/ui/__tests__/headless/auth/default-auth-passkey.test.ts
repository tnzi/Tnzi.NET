import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TwoFactorType } from '@tnzi/core/services/identity';
import { buildDefaultLoginCallbacks } from '../../../src/headless/auth/default-auth';
import type { LoginCallbackHelpers } from '../../../src/headless/auth/useLoginContext';

/**
 * Passkey as the second factor, callback side.
 *
 * The challenge names the method as `passkey`, nothing is "sent" for it, and
 * the verify leg runs the WebAuthn ceremony against the account the temp token
 * names, then establishes the session exactly as after a code. A dismissed
 * system dialog resolves `false` and leaves the challenge open.
 */

function makeHelpers(): LoginCallbackHelpers {
  return {
    setTwoFactorRequired: vi.fn(),
    clearTwoFactor: vi.fn(),
    setCaptchaRequired: vi.fn(),
    clearCaptcha: vi.fn(),
    setPendingActionRequired: vi.fn(),
    clearPendingAction: vi.fn(),
  } as unknown as LoginCallbackHelpers;
}

function makeRuntime(overrides: Record<string, unknown> = {}) {
  const applyTokenSession = vi.fn().mockResolvedValue(undefined);
  const authApi = {
    loginWithRefreshToken: vi.fn().mockResolvedValue({
      succeeded: false,
      errorCode: '2FA_REQUIRED',
      errorDetails: { tempToken: 'tmp-pk', supportedTypes: [TwoFactorType.Passkey, TwoFactorType.Email] },
    }),
    sendTwoFactorCode: vi.fn().mockResolvedValue({ succeeded: true, data: { maskedAddress: 'a***@example.com' } }),
    beginTwoFactorPasskey: vi.fn().mockResolvedValue({
      succeeded: true,
      data: { optionsJson: JSON.stringify({ challenge: 'Y2hhbGxlbmdl', rpId: 'localhost' }), stateId: 'state-1' },
    }),
    completeTwoFactorPasskey: vi.fn().mockResolvedValue({
      succeeded: true,
      data: { accessToken: 'at', refreshToken: 'rt', expiresIn: 3600 },
    }),
    ...overrides,
  };
  return {
    runtime: { auth: { applyTokenSession }, authApi } as never,
    authApi,
    applyTokenSession,
  };
}

/** happy-dom has no WebAuthn; stand in for the JSON bridges and the ceremony. */
function installWebAuthn(credential: unknown) {
  const pkc = {
    parseRequestOptionsFromJSON: vi.fn((o: unknown) => o),
    parseCreationOptionsFromJSON: vi.fn((o: unknown) => o),
  };
  (window as unknown as Record<string, unknown>).PublicKeyCredential = pkc;
  const get = vi.fn().mockResolvedValue(credential);
  Object.defineProperty(navigator, 'credentials', { value: { get }, configurable: true });
  return { get };
}

describe('buildDefaultLoginCallbacks - passkey as the second factor', () => {
  beforeEach(() => {
    installWebAuthn({ toJSON: () => ({ id: 'cred', type: 'public-key' }) });
  });
  afterEach(() => {
    delete (window as unknown as Record<string, unknown>).PublicKeyCredential;
  });

  it('names the method passkey in the challenge and sends nothing for it', async () => {
    const { runtime, authApi, applyTokenSession } = makeRuntime();
    const helpers = makeHelpers();

    await buildDefaultLoginCallbacks(runtime).pwdLogin!({ userName: 'alice', password: 'x' }, helpers);

    expect(helpers.setTwoFactorRequired).toHaveBeenCalledWith(
      expect.objectContaining({ challengeId: 'tmp-pk', method: 'passkey', methods: ['passkey', 'email'] }),
    );
    expect(authApi.sendTwoFactorCode).not.toHaveBeenCalled();
    expect(applyTokenSession).not.toHaveBeenCalled();
  });

  it('runs the ceremony for the challenged account and establishes the session', async () => {
    const { runtime, authApi, applyTokenSession } = makeRuntime();
    const helpers = makeHelpers();
    const callbacks = buildDefaultLoginCallbacks(runtime);
    await callbacks.pwdLogin!({ userName: 'alice', password: 'x' }, helpers);

    const done = await callbacks.verifyTwoFactorWithPasskey!({ challengeId: 'tmp-pk' }, helpers);

    expect(done).toBe(true);
    // Bound to the temp token, never to a username the browser supplies.
    expect(authApi.beginTwoFactorPasskey).toHaveBeenCalledWith({ tempToken: 'tmp-pk' });
    expect(authApi.completeTwoFactorPasskey).toHaveBeenCalledWith(
      expect.objectContaining({ tempToken: 'tmp-pk', stateId: 'state-1' }),
    );
    expect(applyTokenSession).toHaveBeenCalledWith(expect.objectContaining({ accessToken: 'at' }));
  });

  it('treats a dismissed system dialog as not done, with the challenge left open', async () => {
    installWebAuthn(null);
    const { runtime, authApi, applyTokenSession } = makeRuntime();
    const helpers = makeHelpers();
    const callbacks = buildDefaultLoginCallbacks(runtime);
    await callbacks.pwdLogin!({ userName: 'alice', password: 'x' }, helpers);

    const done = await callbacks.verifyTwoFactorWithPasskey!({ challengeId: 'tmp-pk' }, helpers);

    expect(done).toBe(false);
    expect(authApi.completeTwoFactorPasskey).not.toHaveBeenCalled();
    expect(applyTokenSession).not.toHaveBeenCalled();
    expect(helpers.clearTwoFactor).not.toHaveBeenCalled();
  });

  it('hands an obligation challenge from the complete leg to the shell instead of failing', async () => {
    const { runtime, applyTokenSession } = makeRuntime({
      completeTwoFactorPasskey: vi.fn().mockResolvedValue({
        succeeded: false,
        code: 403,
        errorCode: 'IDENTITY_PENDING_ACTIONS_REQUIRED',
        errorDetails: { tempToken: 'pending-1', requiredActions: ['ChangePassword'] },
      }),
    });
    const helpers = makeHelpers();
    const callbacks = buildDefaultLoginCallbacks(runtime);
    await callbacks.pwdLogin!({ userName: 'alice', password: 'x' }, helpers);

    const done = await callbacks.verifyTwoFactorWithPasskey!({ challengeId: 'tmp-pk' }, helpers);

    expect(done).toBe(true);
    expect(helpers.setPendingActionRequired).toHaveBeenCalledWith(
      expect.objectContaining({ tempToken: 'pending-1', userName: 'alice' }),
    );
    expect(applyTokenSession).not.toHaveBeenCalled();
  });

  it('resend is a no-op for the passkey method', async () => {
    const { runtime, authApi } = makeRuntime();
    const callbacks = buildDefaultLoginCallbacks(runtime);

    const res = await callbacks.resendTwoFactor!({ challengeId: 'tmp-pk', method: 'passkey' });

    expect(res).toBeUndefined();
    expect(authApi.sendTwoFactorCode).not.toHaveBeenCalled();
  });
});
