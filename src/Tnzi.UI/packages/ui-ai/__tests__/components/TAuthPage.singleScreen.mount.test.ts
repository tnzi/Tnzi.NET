/**
 * `TAuthPage` mounted with the opt-in `singleScreen` form: account and
 * password together, two-factor after the password, and the identifier
 * wording for a user-name deployment. The default two-screen flow is covered
 * by `TAuthPage.mount.test.ts`.
 */
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createApp, h, nextTick, ref, type App } from 'vue';
import { DEFAULT_LOGIN_FEATURES, type LoginCallbacks, type LoginFeatures } from '@tnzi/ui/headless';

vi.mock('@tnzi/core/services/captcha', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tnzi/core/services/captcha')>();
  return {
    ...actual,
    useCaptchaWidget: () => ({
      container: ref(null),
      token: ref(''),
      ready: ref(false),
      error: ref(''),
      isScriptProvider: ref(false),
      isInvisible: ref(false),
      execute: vi.fn(async () => 'widget-token'),
      reset: vi.fn(),
    }),
  };
});

const { default: TAuthPage } = await import('../../src/auth/TAuthPage.vue');

let app: App | null = null;
let host: HTMLElement | null = null;

function mountPage(props: Record<string, unknown>) {
  host = document.createElement('div');
  document.body.appendChild(host);
  const errors: unknown[] = [];
  app = createApp({ render: () => h(TAuthPage, { singleScreen: true, ...props }) });
  app.config.errorHandler = (err) => {
    errors.push(err);
  };
  app.mount(host);
  return { host, errors };
}

async function settle(): Promise<void> {
  for (let i = 0; i < 5; i += 1) {
    await nextTick();
    await Promise.resolve();
  }
}

function type(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function inputs(root: HTMLElement): HTMLInputElement[] {
  return [...root.querySelectorAll<HTMLInputElement>('.t-auth__pane input')];
}

function button(root: HTMLElement, text: string): HTMLButtonElement {
  const found = [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text);
  if (!found) throw new Error(`No "${text}" button`);
  return found;
}

async function submitForm(root: HTMLElement): Promise<void> {
  root.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  await settle();
}

async function signInWith(root: HTMLElement, account: string, password: string): Promise<void> {
  const [a, p] = inputs(root);
  type(a, account);
  type(p, password);
  await settle();
  await submitForm(root);
}

const FEATURES = { ...DEFAULT_LOGIN_FEATURES, passwordLogin: true } as unknown as LoginFeatures;
const TOTP = { challengeId: 'c1', method: 'totp', methods: ['totp', 'email'] } as never;
const TOTP_PROMPT = 'Enter the code from your authenticator app.';

afterEach(() => {
  app?.unmount();
  host?.remove();
  app = null;
  host = null;
});

describe('TAuthPage singleScreen (mounted)', () => {
  it('is one real form with autocomplete tokens and a single Sign in submit', async () => {
    const pwdLogin = vi.fn<NonNullable<LoginCallbacks['pwdLogin']>>(async () => undefined);
    const { host, errors } = mountPage({ callbacks: { pwdLogin }, features: FEATURES });
    await settle();

    const form = host.querySelector('form')!;
    expect(form.querySelector('input[autocomplete="username"]')).not.toBeNull();
    expect(form.querySelector('input[type="password"][autocomplete="current-password"]')).not.toBeNull();
    expect([...host.querySelectorAll('button')].some((b) => b.textContent?.trim() === 'Continue')).toBe(false);

    await signInWith(host, 'jsmith', 'pw');

    expect(errors).toEqual([]);
    expect(pwdLogin).toHaveBeenCalledTimes(1);
  });

  it('is off by default: the account-first form shows no password field', async () => {
    const { host } = mountPage({ singleScreen: false, features: FEATURES });
    await settle();
    expect(host.querySelector('input[type="password"]')).toBeNull();
    expect(button(host, 'Continue')).toBeTruthy();
  });

  it('a wrong password shows on the form and keeps what was typed', async () => {
    const pwdLogin = vi.fn(async () => {
      throw new Error('Incorrect password.');
    });
    const { host } = mountPage({ callbacks: { pwdLogin }, features: FEATURES });
    await settle();
    await signInWith(host, 'jsmith', 'wrong');

    expect(host.querySelector('.t-auth__error')?.textContent).toBe('Incorrect password.');
    const [a, p] = inputs(host);
    expect([a.value, p.value]).toEqual(['jsmith', 'wrong']);
  });

  describe('two-factor', () => {
    it('a correct password opens the verify step; a wrong code stays there with the error; a right one signs in', async () => {
      const verifyTwoFactor = vi
        .fn<NonNullable<LoginCallbacks['verifyTwoFactor']>>()
        .mockRejectedValueOnce(new Error('Invalid code.'))
        .mockResolvedValueOnce(undefined);
      const onAuthenticated = vi.fn();
      const { host, errors } = mountPage({
        features: FEATURES,
        onAuthenticated,
        callbacks: {
          pwdLogin: async (_p: unknown, helpers: { setTwoFactorRequired: (c: never) => void }) =>
            helpers.setTwoFactorRequired(TOTP),
          verifyTwoFactor,
        },
      });
      await settle();
      await signInWith(host, 'jsmith', 'pw');

      expect(host.querySelector('input[type="password"]')).toBeNull();
      expect(host.textContent).toContain(TOTP_PROMPT);
      expect(onAuthenticated).not.toHaveBeenCalled();

      type(inputs(host)[0], '000000');
      await settle();
      button(host, 'Verify').click();
      await settle();
      expect(host.querySelector('.t-auth__error')?.textContent).toBe('Invalid code.');
      expect(host.textContent).toContain(TOTP_PROMPT);
      expect(onAuthenticated).not.toHaveBeenCalled();

      type(inputs(host)[0], '123456');
      await settle();
      button(host, 'Verify').click();
      await settle();
      expect(onAuthenticated).toHaveBeenCalledTimes(1);
      expect(errors).toEqual([]);
    });

    it('offers the other methods, resends on switching, and Back returns to the form with the account filled and the password cleared', async () => {
      const resendTwoFactor = vi.fn(async () => ({ maskedAddress: 'j***@example.com' }));
      const { host } = mountPage({
        features: FEATURES,
        callbacks: {
          pwdLogin: async (_p: unknown, helpers: { setTwoFactorRequired: (c: never) => void }) =>
            helpers.setTwoFactorRequired(TOTP),
          resendTwoFactor,
        },
      });
      await settle();
      await signInWith(host, 'jsmith', 'pw');

      button(host, 'Use email').click();
      await settle();
      expect(resendTwoFactor).toHaveBeenCalledTimes(1);
      expect(host.querySelector('.t-auth__notice')?.textContent).toContain('j***@example.com');

      button(host, 'Back').click();
      await settle();
      const [account, password] = inputs(host);
      expect(account.value).toBe('jsmith');
      expect(password.type).toBe('password');
      expect(password.value).toBe('');
    });
  });

  describe('identifier wording follows /auth/config', () => {
    it('a user-name-only deployment reads Username, with no email wording or keyboard', async () => {
      const features = {
        ...FEATURES,
        identifiers: { userName: true, email: false, phone: false },
      } as unknown as LoginFeatures;
      const { host } = mountPage({ features });
      await settle();
      const account = inputs(host)[0];
      expect(account.getAttribute('aria-label')).toBe('Username');
      expect(account.placeholder).toBe('Enter your username');
      expect(account.getAttribute('autocomplete')).toBe('username');
      expect(account.type).toBe('text');
      expect(account.getAttribute('inputmode')).toBe('text');
    });

    it('an email-only deployment keeps the email wording', async () => {
      const features = {
        ...FEATURES,
        identifiers: { userName: false, email: true, phone: false },
      } as unknown as LoginFeatures;
      const { host } = mountPage({ features });
      await settle();
      expect(inputs(host)[0].getAttribute('aria-label')).toBe('Email address');
      expect(inputs(host)[0].placeholder).toBe('Enter your email address');
    });
  });
});
