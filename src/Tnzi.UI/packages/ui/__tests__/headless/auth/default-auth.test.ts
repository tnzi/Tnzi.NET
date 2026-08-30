import { describe, expect, it, vi } from 'vitest';
import { TwoFactorType } from '@tnzi/core/services/identity';
import { buildDefaultLoginCallbacks } from '../../../src/headless/auth/default-auth';
import type { LoginCallbackHelpers } from '../../../src/headless/auth/useLoginContext';

/**
 * Code login is not a bypass around two-factor.
 *
 * The backend answers `POST /auth/code-login` with the same 403 `2FA_REQUIRED`
 * envelope the password form gets whenever the account has a factor the code
 * did not already prove. These cover the callback side of that: the challenge
 * has to reach the login shell, and nothing may be signed in behind it.
 */

function makeHelpers(): LoginCallbackHelpers {
  return {
    setTwoFactorRequired: vi.fn(),
    clearTwoFactor: vi.fn(),
    setCaptchaRequired: vi.fn(),
    clearCaptcha: vi.fn(),
  } as unknown as LoginCallbackHelpers;
}

function makeRuntime(overrides: Record<string, unknown> = {}) {
  const applyTokenSession = vi.fn().mockResolvedValue(undefined);
  const authApi = {
    codeLogin: vi.fn(),
    sendCodeLoginCode: vi.fn().mockResolvedValue({ succeeded: true }),
    sendPasswordRecoveryCode: vi.fn().mockResolvedValue({ succeeded: true }),
    sendQuickRegisterCode: vi.fn().mockResolvedValue({ succeeded: true }),
    sendTwoFactorCode: vi.fn().mockResolvedValue({ succeeded: true, data: { maskedAddress: 'a***@example.com' } }),
    ...overrides,
  };
  return {
    runtime: { auth: { applyTokenSession }, authApi } as never,
    authApi,
    applyTokenSession,
  };
}

describe('buildDefaultLoginCallbacks - code login', () => {
  it('turns a 2FA_REQUIRED answer into a challenge and signs nobody in', async () => {
    const { runtime, authApi, applyTokenSession } = makeRuntime({
      codeLogin: vi.fn().mockResolvedValue({
        succeeded: false,
        errorCode: '2FA_REQUIRED',
        errorDetails: { tempToken: 'tmp-1', supportedTypes: [TwoFactorType.Totp] },
      }),
    });
    const helpers = makeHelpers();

    await buildDefaultLoginCallbacks(runtime).codeLogin!(
      { account: 'someone@example.com', code: '123456', type: 'email' },
      helpers,
    );

    expect(helpers.setTwoFactorRequired).toHaveBeenCalledWith(
      expect.objectContaining({ challengeId: 'tmp-1', method: 'totp', userName: 'someone@example.com' }),
    );
    // No session may be established behind a pending challenge.
    expect(applyTokenSession).not.toHaveBeenCalled();
    // TOTP is read from the authenticator - nothing to deliver.
    expect(authApi.sendTwoFactorCode).not.toHaveBeenCalled();
  });

  it('delivers the code first when the remaining factor is a channel', async () => {
    const { runtime, authApi } = makeRuntime({
      codeLogin: vi.fn().mockResolvedValue({
        succeeded: false,
        errorCode: '2FA_REQUIRED',
        errorDetails: { tempToken: 'tmp-2', supportedTypes: [TwoFactorType.Sms] },
      }),
    });
    const helpers = makeHelpers();

    await buildDefaultLoginCallbacks(runtime).codeLogin!(
      { account: '+14155552671', code: '123456', type: 'phone' },
      helpers,
    );

    expect(authApi.sendTwoFactorCode).toHaveBeenCalledWith({ tempToken: 'tmp-2', type: TwoFactorType.Sms });
    expect(helpers.setTwoFactorRequired).toHaveBeenCalledWith(
      expect.objectContaining({ method: 'sms', maskedAddress: 'a***@example.com' }),
    );
  });

  it('still signs in normally when no factor is owed', async () => {
    const { runtime, applyTokenSession } = makeRuntime({
      codeLogin: vi.fn().mockResolvedValue({
        succeeded: true,
        data: { accessToken: 'at', refreshToken: 'rt', expiresIn: 1800 },
      }),
    });
    const helpers = makeHelpers();

    await buildDefaultLoginCallbacks(runtime).codeLogin!(
      { account: 'someone@example.com', code: '123456', type: 'email' },
      helpers,
    );

    expect(applyTokenSession).toHaveBeenCalledWith({ accessToken: 'at', refreshToken: 'rt', expiresIn: 1800 });
    expect(helpers.setTwoFactorRequired).not.toHaveBeenCalled();
  });

  it('carries the image captcha into the code-login send-code call', async () => {
    // That endpoint spends a real SMS / email per call, so the backend gates it
    // on EnableCaptchaOnLogin. Dropping these fields here turns the feature into
    // "every send is rejected" on a deployment that has the switch on.
    const { runtime, authApi } = makeRuntime();

    await buildDefaultLoginCallbacks(runtime).sendCode!({
      account: 'someone@example.com',
      type: 'email',
      purpose: 'code-login',
      captchaId: 'cid',
      captchaCode: 'ABCD',
    });

    expect(authApi.sendCodeLoginCode).toHaveBeenCalledWith(
      expect.objectContaining({ email: 'someone@example.com', captchaId: 'cid', captchaCode: 'ABCD' }),
    );
  });
});
