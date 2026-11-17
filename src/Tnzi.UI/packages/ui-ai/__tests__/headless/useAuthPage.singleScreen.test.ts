/**
 * The opt-in one-screen form of `useAuthPage` (`singleScreen`). The default
 * account-first flow is covered, unchanged, by `useAuthPage.test.ts`.
 */
import { describe, it, expect, vi } from 'vitest';
import { useAuthPage, type UseAuthPageOptions } from '../../src/headless/useAuthPage';
import type { LoginCallbacks, LoginFeatures } from '@tnzi/ui/headless';

const FEATURES = {
  passwordLogin: true,
  codeLogin: true,
  register: true,
  identifiers: { email: true, phone: false, userName: true },
} as unknown as LoginFeatures;

function page(callbacks: LoginCallbacks, overrides: Partial<UseAuthPageOptions> = {}) {
  const onAuthenticated = vi.fn();
  const auth = useAuthPage({
    callbacks: () => callbacks,
    features: () => FEATURES,
    translate: (_key, fallback) => fallback ?? _key,
    onAuthenticated,
    singleScreen: () => true,
    ...overrides,
  });
  return { auth, onAuthenticated };
}

function filled(auth: ReturnType<typeof page>['auth']) {
  auth.account.value = 'jsmith';
  auth.password.value = 'pw';
}

describe('useAuthPage singleScreen', () => {
  it('is off unless asked for, and never applies without password sign-in', () => {
    const off = page({}, { singleScreen: undefined }).auth;
    expect(off.singleScreen.value).toBe(false);
    const codeOnly = page({}, { features: () => ({ ...FEATURES, passwordLogin: false }) as LoginFeatures }).auth;
    expect(codeOnly.singleScreen.value).toBe(false);
    expect(page({}).auth.singleScreen.value).toBe(true);
  });

  it('one submit signs in from the entry pane, passing a user name through unchanged', async () => {
    const pwdLogin = vi.fn(async () => undefined);
    const { auth, onAuthenticated } = page({ pwdLogin });
    filled(auth);
    await auth.onContinue();
    expect(pwdLogin.mock.calls[0][0]).toMatchObject({ userName: 'jsmith', password: 'pw' });
    expect(onAuthenticated).toHaveBeenCalledTimes(1);
    expect(auth.step.value).toBe('identify');
  });

  it('an empty field is refused locally and nothing is sent', async () => {
    const pwdLogin = vi.fn(async () => undefined);
    const { auth, onAuthenticated } = page({ pwdLogin });
    auth.account.value = 'jsmith';
    await auth.onContinue();
    expect(pwdLogin).not.toHaveBeenCalled();
    expect(onAuthenticated).not.toHaveBeenCalled();
    expect(auth.error.value).toBe('Enter your account and password.');
  });

  it('a rejected password shows the error and keeps what was typed', async () => {
    const pwdLogin = vi.fn(async () => {
      throw new Error('Incorrect password.');
    });
    const { auth, onAuthenticated } = page({ pwdLogin });
    filled(auth);
    await auth.onContinue();
    expect(auth.error.value).toBe('Incorrect password.');
    expect([auth.account.value, auth.password.value, auth.step.value]).toEqual(['jsmith', 'pw', 'identify']);
    expect(onAuthenticated).not.toHaveBeenCalled();
  });

  it('two-factor follows the password submit; back keeps the account and clears the password', async () => {
    const pwdLogin = vi.fn<NonNullable<LoginCallbacks['pwdLogin']>>(async (_p, helpers) => {
      helpers.setTwoFactorRequired({ challengeId: 'c1', method: 'totp', methods: ['totp'] } as never);
    });
    const verifyTwoFactor = vi.fn(async () => undefined);
    const { auth, onAuthenticated } = page({ pwdLogin, verifyTwoFactor });
    filled(auth);
    await auth.onContinue();
    expect(auth.step.value).toBe('two-factor');
    expect(onAuthenticated).not.toHaveBeenCalled();
    auth.code.value = '123456';
    await auth.onTwoFactorSubmit();
    expect(onAuthenticated).toHaveBeenCalledTimes(1);

    auth.backToIdentify();
    expect([auth.step.value, auth.account.value, auth.password.value]).toEqual(['identify', 'jsmith', '']);
  });

  it('the code and register links need an account to send to', async () => {
    const sendCode = vi.fn(async () => undefined);
    const { auth } = page({ sendCode });
    await auth.switchTo('code');
    expect(sendCode).not.toHaveBeenCalled();
    expect(auth.error.value).toBe('Enter your email address or phone number first.');
  });

  it('a finished registration returns to the one-screen form with a notice', async () => {
    const { auth } = page({ register: vi.fn(async () => undefined), sendCode: vi.fn(async () => undefined) });
    auth.account.value = 'new@example.com';
    await auth.switchTo('register');
    await auth.onRegisterSubmit();
    expect(auth.step.value).toBe('identify');
    expect(auth.notice.value).toBe('Account created. Sign in with your password.');
  });
});
