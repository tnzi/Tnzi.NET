import { describe, it, expect, vi } from 'vitest';
import { useAuthPage, type UseAuthPageOptions } from '../../src/headless/useAuthPage';
import type { LoginCallbacks, LoginFeatures } from '@tnzi/ui';

const FEATURES: LoginFeatures = {
  passwordLogin: true,
  codeLogin: true,
  register: true,
  identifiers: { email: true, phone: false, userName: false },
} as unknown as LoginFeatures;

function page(callbacks: LoginCallbacks, overrides: Partial<UseAuthPageOptions> = {}) {
  const onAuthenticated = vi.fn();
  const auth = useAuthPage({
    callbacks: () => callbacks,
    features: () => FEATURES,
    translate: (_key, fallback) => fallback ?? _key,
    onAuthenticated,
    ...overrides,
  });
  return { auth, onAuthenticated };
}

describe('useAuthPage', () => {
  it('Continue moves to the password pane without asking the backend', async () => {
    const { auth } = page({});
    auth.account.value = 'me@example.com';
    await auth.onContinue();
    expect(auth.step.value).toBe('password');
    expect(auth.accountType.value).toBe('email');
  });

  /**
   * With password sign-in off, Continue sends the code itself. That send ran
   * outside `run()`: a refused send rejected out of the click handler with no
   * error on screen, and nothing marked the page busy, so every further click
   * sent another code.
   */
  describe('Continue on a code-only deployment', () => {
    const CODE_ONLY = { ...FEATURES, passwordLogin: false } as LoginFeatures;

    it('sends the code and moves to the code pane', async () => {
      const sendCode = vi.fn(async () => undefined);
      const { auth } = page({ sendCode }, { features: () => CODE_ONLY });
      auth.account.value = 'me@example.com';
      await auth.onContinue();
      expect(sendCode).toHaveBeenCalledWith({ account: 'me@example.com', type: 'email', purpose: 'code-login' });
      expect(auth.step.value).toBe('code');
      expect(auth.notice.value).toBe('Verification code sent.');
    });

    it('shows a refused send and stays on the identify step', async () => {
      const sendCode = vi.fn(async () => {
        throw new Error('Too many requests. Try again later.');
      });
      const { auth } = page({ sendCode }, { features: () => CODE_ONLY });
      auth.account.value = 'me@example.com';
      await expect(auth.onContinue()).resolves.toBeUndefined();
      expect(auth.error.value).toBe('Too many requests. Try again later.');
      expect(auth.step.value).toBe('identify');
      expect(auth.submitting.value).toBe(false);
    });

    it('is busy while the code is being sent, so a second click sends nothing', async () => {
      let finish: (() => void) | undefined;
      const sendCode = vi.fn(() => new Promise<void>((resolve) => (finish = resolve)));
      const { auth } = page({ sendCode }, { features: () => CODE_ONLY });
      auth.account.value = 'me@example.com';
      const first = auth.onContinue();
      expect(auth.submitting.value).toBe(true);
      expect(auth.canContinue.value).toBe(false);
      await auth.onContinue();
      finish!();
      await first;
      expect(sendCode).toHaveBeenCalledTimes(1);
      expect(auth.step.value).toBe('code');
    });
  });

  it('a password login that establishes a session reports authenticated', async () => {
    const pwdLogin = vi.fn(async () => undefined);
    const { auth, onAuthenticated } = page({ pwdLogin });
    auth.account.value = 'me@example.com';
    auth.password.value = 'pw';
    await auth.onPasswordSubmit();
    expect(pwdLogin).toHaveBeenCalledTimes(1);
    expect(onAuthenticated).toHaveBeenCalledTimes(1);
  });

  it('a two-factor challenge moves to the two-factor pane and does not report authenticated', async () => {
    const pwdLogin = vi.fn(async (_p, helpers) => {
      helpers.setTwoFactorRequired({ challengeId: 'c1', method: 'totp', methods: ['totp', 'email'] });
    });
    const { auth, onAuthenticated } = page({ pwdLogin } as LoginCallbacks);
    auth.account.value = 'me@example.com';
    await auth.onPasswordSubmit();
    expect(auth.step.value).toBe('two-factor');
    expect(auth.otherTwoFactorMethods.value).toEqual(['email']);
    expect(onAuthenticated).not.toHaveBeenCalled();
  });

  /**
   * Sign-up creates the account and sets the password; the shared callback
   * establishes NO session. Emitting `authenticated` sent the user into the
   * route guard, which bounced them back to this same register pane with
   * nothing said, and a second click failed with "account exists".
   */
  /**
   * Sign-in and sign-up by code have their own channels on the backend. The
   * links on the password pane are offered only for an address the flow can
   * send its code to; offering sign-up by email on an SMS-only deployment led
   * to a send the backend refused.
   */
  describe('code flow links follow their own channels', () => {
    const SPLIT = {
      ...FEATURES,
      codeChannels: { sms: true, email: true },
      registerChannels: { sms: true, email: false },
    } as LoginFeatures;

    it('hides sign-up for an email when quick registration only texts', () => {
      const { auth } = page({}, { features: () => SPLIT });
      auth.account.value = 'me@example.com';
      expect(auth.offersCodeSignIn.value).toBe(true);
      expect(auth.offersSignUp.value).toBe(false);
      auth.account.value = '+1 555 0100';
      expect(auth.offersSignUp.value).toBe(true);
    });

    it('hides code sign-in for a phone when codes only go by email', () => {
      const emailCodes = { ...SPLIT, codeChannels: { sms: false, email: true } } as LoginFeatures;
      const { auth } = page({}, { features: () => emailCodes });
      auth.account.value = '+1 555 0100';
      expect(auth.offersCodeSignIn.value).toBe(false);
      auth.account.value = 'me@example.com';
      expect(auth.offersCodeSignIn.value).toBe(true);
    });

    it('leaves an identifier it cannot classify to the backend, and a closed flow closed', () => {
      const { auth } = page({}, { features: () => SPLIT });
      auth.account.value = 'jsmith';
      expect(auth.offersSignUp.value).toBe(true);
      const closed = page({}, { features: () => ({ ...SPLIT, register: false }) as LoginFeatures }).auth;
      closed.account.value = '+1 555 0100';
      expect(closed.offersSignUp.value).toBe(false);
    });
  });

  describe('register', () => {
    it('returns to the password pane with a notice and does not report authenticated', async () => {
      const register = vi.fn(async () => undefined);
      const sendCode = vi.fn(async () => undefined);
      const { auth, onAuthenticated } = page({ register, sendCode });
      auth.account.value = 'new@example.com';
      await auth.switchTo('register');
      auth.code.value = '123456';
      auth.password.value = 'chosen';

      await auth.onRegisterSubmit();

      expect(register).toHaveBeenCalledWith({
        account: 'new@example.com',
        code: '123456',
        password: 'chosen',
        type: 'email',
      });
      expect(onAuthenticated).not.toHaveBeenCalled();
      expect(auth.step.value).toBe('password');
      expect(auth.account.value).toBe('new@example.com');
      expect(auth.password.value).toBe('');
      expect(auth.notice.value).toBe('Account created. Sign in with your password.');
      expect(auth.error.value).toBe('');
    });

    it('a failed registration stays on the register pane with the error', async () => {
      const register = vi.fn(async () => {
        throw new Error('Registration failed');
      });
      const sendCode = vi.fn(async () => undefined);
      const { auth, onAuthenticated } = page({ register, sendCode });
      auth.account.value = 'new@example.com';
      await auth.switchTo('register');
      await auth.onRegisterSubmit();
      expect(auth.step.value).toBe('register');
      expect(auth.error.value).toBe('Registration failed');
      expect(onAuthenticated).not.toHaveBeenCalled();
    });
  });

  /**
   * The backend asks for obligations AFTER 2FA, so a forced password change
   * surfaces exactly here. The page never passed `helpers`, so the callback
   * could only throw "verification failed" at a correct code.
   */
  describe('two-factor', () => {
    it('passes helpers, so a pending action is recorded and authenticated is not reported', async () => {
      const verifyTwoFactor = vi.fn(async (_p, helpers) => {
        helpers?.setPendingActionRequired({ userName: 'me', tempToken: 't', requiredActions: ['ChangePassword'] });
      });
      const { auth, onAuthenticated } = page({ verifyTwoFactor } as LoginCallbacks);
      auth.step.value = 'two-factor';
      auth.code.value = '000000';

      await auth.onTwoFactorSubmit();

      expect(verifyTwoFactor.mock.calls[0]?.[1]).toBeDefined();
      expect(auth.pendingAction.value?.requiredActions).toEqual(['ChangePassword']);
      expect(auth.error.value).toContain('ChangePassword');
      expect(onAuthenticated).not.toHaveBeenCalled();
    });

    it('a verified code with a session reports authenticated', async () => {
      const verifyTwoFactor = vi.fn(async () => undefined);
      const { auth, onAuthenticated } = page({ verifyTwoFactor });
      auth.step.value = 'two-factor';
      await auth.onTwoFactorSubmit();
      expect(onAuthenticated).toHaveBeenCalledTimes(1);
    });

    // A security key is a ceremony, not a code: its own leg, its own outcome.
    it('the passkey leg reports authenticated when the ceremony completes', async () => {
      const verifyTwoFactorWithPasskey = vi.fn(async () => true);
      const { auth, onAuthenticated } = page({ verifyTwoFactorWithPasskey } as LoginCallbacks);
      auth.step.value = 'two-factor';
      auth.twoFactorMethod.value = 'passkey';

      await auth.onTwoFactorPasskey();

      expect(verifyTwoFactorWithPasskey.mock.calls[0]?.[1]).toBeDefined();
      expect(onAuthenticated).toHaveBeenCalledTimes(1);
    });

    it('a closed system dialog leaves the passkey step open and reports nothing', async () => {
      const verifyTwoFactorWithPasskey = vi.fn(async () => false);
      const { auth, onAuthenticated } = page({ verifyTwoFactorWithPasskey } as LoginCallbacks);
      auth.step.value = 'two-factor';
      auth.twoFactorMethod.value = 'passkey';

      await auth.onTwoFactorPasskey();

      expect(onAuthenticated).not.toHaveBeenCalled();
      expect(auth.error.value).toBe('');
      expect(auth.step.value).toBe('two-factor');
    });

    it('switching to the passkey sends nothing, and the passkey is offered only when wired', async () => {
      vi.stubGlobal('PublicKeyCredential', { parseCreationOptionsFromJSON: vi.fn(), parseRequestOptionsFromJSON: vi.fn() });
      const resendTwoFactor = vi.fn(async () => ({ maskedAddress: 'a***@x' }));
      const { auth } = page({ resendTwoFactor, verifyTwoFactorWithPasskey: vi.fn(async () => true) } as LoginCallbacks);
      auth.challenge.value = { challengeId: 't', method: 'email', methods: ['email', 'passkey'] };
      auth.twoFactorMethod.value = 'email';
      expect(auth.otherTwoFactorMethods.value).toEqual(['passkey']);

      await auth.useTwoFactorMethod('passkey');

      expect(resendTwoFactor).not.toHaveBeenCalled();
      expect(auth.twoFactorMethod.value).toBe('passkey');

      const { auth: unwired } = page({ resendTwoFactor } as LoginCallbacks);
      unwired.challenge.value = { challengeId: 't', method: 'email', methods: ['email', 'passkey'] };
      unwired.twoFactorMethod.value = 'email';
      expect(unwired.otherTwoFactorMethods.value).toEqual([]);
      vi.unstubAllGlobals();
    });

    it('does not offer a wired passkey in a browser that cannot run the ceremony', () => {
      vi.stubGlobal('PublicKeyCredential', undefined);
      const { auth } = page({ verifyTwoFactorWithPasskey: vi.fn(async () => true) } as LoginCallbacks);
      auth.challenge.value = { challengeId: 't', method: 'email', methods: ['email', 'passkey'] };
      auth.twoFactorMethod.value = 'email';

      expect(auth.otherTwoFactorMethods.value).toEqual([]);
      vi.unstubAllGlobals();
    });

    it('shows why the initial code could not be delivered instead of a silent prompt', async () => {
      const pwdLogin = vi.fn(async (_p, helpers) => {
        helpers.setTwoFactorRequired({ challengeId: 'c1', method: 'email', methods: ['email'], codeSendError: 'Too many codes requested' });
      });
      const { auth, onAuthenticated } = page({ pwdLogin } as LoginCallbacks);
      auth.account.value = 'me@example.com';
      await auth.onPasswordSubmit();

      expect(auth.step.value).toBe('two-factor');
      expect(auth.error.value).toBe('Too many codes requested');
      expect(onAuthenticated).not.toHaveBeenCalled();
    });
  });

  /**
   * A pending action reported by one attempt must not outlive it: once set,
   * it used to suppress `authenticated` for every later successful login in
   * the same page instance until a reload.
   */
  it('a stale pendingAction does not block a later successful login', async () => {
    let pending = true;
    const pwdLogin = vi.fn(async (_p, helpers) => {
      if (pending) helpers.setPendingActionRequired({ userName: 'a', tempToken: 't', requiredActions: ['X'] });
    });
    const { auth, onAuthenticated } = page({ pwdLogin } as LoginCallbacks);
    auth.account.value = 'a@example.com';
    await auth.onPasswordSubmit();
    expect(onAuthenticated).not.toHaveBeenCalled();

    pending = false;
    auth.account.value = 'b@example.com';
    await auth.onPasswordSubmit();
    expect(auth.pendingAction.value).toBeNull();
    expect(onAuthenticated).toHaveBeenCalledTimes(1);
  });

  /**
   * The third way the shared callback returns without a session: adaptive
   * captcha. `setCaptchaRequired` reveals the field and returns; reporting
   * `authenticated` there sent the user into the route guard, which bounced
   * them back to this pane, and the captcha the callback just revealed was
   * never seen.
   */
  describe('captcha challenge', () => {
    const captcha = { provider: 'image', captchaId: 'c1', imageBase64: 'AAA=', expirationSeconds: 60 };

    it('a password login that revealed a captcha is not reported as authenticated', async () => {
      const pwdLogin = vi.fn(async (_p, helpers) => {
        helpers.setCaptchaRequired(captcha);
      });
      const { auth, onAuthenticated } = page({ pwdLogin } as LoginCallbacks);
      auth.account.value = 'a@example.com';
      await auth.onPasswordSubmit();
      expect(auth.captcha.value).toEqual(captcha);
      expect(onAuthenticated).not.toHaveBeenCalled();
    });

    it('a code login that revealed a captcha is not reported as authenticated', async () => {
      const codeLogin = vi.fn(async (_p, helpers) => {
        helpers.setCaptchaRequired(captcha);
      });
      const { auth, onAuthenticated } = page({ codeLogin } as LoginCallbacks);
      auth.account.value = 'a@example.com';
      await auth.onCodeSubmit();
      expect(onAuthenticated).not.toHaveBeenCalled();
    });

    it('the next attempt with the captcha answered reports authenticated, even if the callback leaves the picture', async () => {
      let first = true;
      const pwdLogin = vi.fn(async (_p, helpers) => {
        if (first) helpers.setCaptchaRequired(captcha);
        // A callback that does not call clearCaptcha on success.
      });
      const { auth, onAuthenticated } = page({ pwdLogin } as LoginCallbacks);
      auth.account.value = 'a@example.com';
      await auth.onPasswordSubmit();
      expect(onAuthenticated).not.toHaveBeenCalled();

      first = false;
      auth.captchaCode.value = 'abcd';
      await auth.onPasswordSubmit();
      // The picture's token is the composed id:code pair.
      expect(pwdLogin.mock.calls[1]?.[0]).toMatchObject({ captchaToken: 'c1:abcd' });
      expect(onAuthenticated).toHaveBeenCalledTimes(1);
    });

    it('an unanswered picture is refused locally instead of round-tripping', async () => {
      const pwdLogin = vi.fn(async (_p, helpers) => {
        helpers.setCaptchaRequired(captcha);
      });
      const { auth } = page({ pwdLogin } as LoginCallbacks);
      auth.account.value = 'a@example.com';
      await auth.onPasswordSubmit();

      await auth.onPasswordSubmit();
      expect(pwdLogin).toHaveBeenCalledTimes(1);
      expect(auth.error.value).toBe('Please complete the captcha.');
    });

    it('a provider widget challenge takes its token from executeCaptcha', async () => {
      let first = true;
      const pwdLogin = vi.fn(async (_p, helpers) => {
        if (first) helpers.setCaptchaRequired({ provider: 'turnstile' });
      });
      const executeCaptcha = vi.fn(async () => 'widget-token');
      const { auth, onAuthenticated } = page({ pwdLogin } as LoginCallbacks, { executeCaptcha });
      auth.account.value = 'a@example.com';
      await auth.onPasswordSubmit();
      expect(auth.captchaIsImage.value).toBe(false);

      first = false;
      await auth.onPasswordSubmit();
      expect(executeCaptcha).toHaveBeenCalledTimes(1);
      expect(pwdLogin.mock.calls[1]?.[0]).toMatchObject({ captchaToken: 'widget-token' });
      expect(onAuthenticated).toHaveBeenCalledTimes(1);
    });

    it('a widget that cannot produce a token surfaces as an error, not a submit', async () => {
      const pwdLogin = vi.fn(async (_p, helpers) => {
        helpers.setCaptchaRequired({ provider: 'turnstile' });
      });
      const executeCaptcha = vi.fn(async () => {
        throw new Error('Captcha not solved yet.');
      });
      const { auth } = page({ pwdLogin } as LoginCallbacks, { executeCaptcha });
      auth.account.value = 'a@example.com';
      await auth.onPasswordSubmit();

      await auth.onPasswordSubmit();
      expect(pwdLogin).toHaveBeenCalledTimes(1);
      expect(auth.error.value).toBe('Captcha not solved yet.');
    });
  });

  it('backToIdentify clears the pending action and the fields', async () => {
    const { auth } = page({});
    auth.pendingAction.value = { userName: 'a', tempToken: 't', requiredActions: ['X'] };
    auth.password.value = 'x';
    auth.backToIdentify();
    expect(auth.pendingAction.value).toBeNull();
    expect(auth.password.value).toBe('');
    expect(auth.step.value).toBe('identify');
  });
});
