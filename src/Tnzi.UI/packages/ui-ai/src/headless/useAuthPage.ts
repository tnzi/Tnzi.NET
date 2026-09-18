/**
 * `useAuthPage` - the identifier-first sign-in / sign-up state machine behind
 * `TAuthPage`.
 *
 * Extracted from the SFC so the submit handlers can be unit-tested: this
 * package's vitest has no Vue SFC plugin (Critical Rule #3), and the two
 * defects this file exists to keep out both lived in handler bodies nobody
 * could execute in a test:
 *
 *   - **sign-up emitted `authenticated` without a session.** The shared
 *     `register` callback (`@tnzi/ui`'s `buildDefaultLoginCallbacks`) creates
 *     the account and sets the password; it does NOT sign the user in. The
 *     route then bounced the user back to the same register pane with no
 *     notice, and a second click failed with "account exists".
 *   - **two-factor verification never passed `helpers`**, so a pending action
 *     surfaced after 2FA (a forced password change) came back as a raw error
 *     line while the code was correct - the very case the callback's own
 *     comment says most needs it.
 *
 * The composable owns state and transitions; the SFC owns markup and copy.
 * `translate` is the same `(key, fallback) => string` shape the login stack
 * uses, so every user-facing string here carries an English fallback.
 */
import { computed, ref, type Ref, type ComputedRef } from 'vue';
import { composeImageCaptchaToken } from '@tnzi/core/services/captcha';
import type {
  LoginCallbacks,
  LoginFeatures,
  PendingActionChallenge,
  TwoFactorChallenge,
  TwoFactorMethodName,
  LoginCaptchaData,
  Translate,
} from '@tnzi/ui';

/** Which pane fills the column. `identify` is always the entry point. */
export type AuthStep = 'identify' | 'password' | 'code' | 'register' | 'two-factor';

export interface UseAuthPageOptions {
  /** The auth callbacks - same contract the admin login page consumes. */
  callbacks: () => LoginCallbacks;
  /** Backend-derived feature flags. */
  features: () => LoginFeatures;
  /** `(key, fallback?) => string`. */
  translate: Translate;
  /** Consumer-driven busy flag (e.g. during redirect). */
  loading?: () => boolean;
  /** Authentication completed with a session - the consumer routes onward. */
  onAuthenticated: () => void;
  /** Move focus into the pane that was just shown. DOM work stays outside. */
  focusFirstField?: () => void | Promise<void>;
  /**
   * Produce the captcha token for a script-rendered provider (Turnstile, hCaptcha,
   * reCAPTCHA, Altcha) right before the password submit - the page owns the
   * widget (`useCaptchaWidget`), this only asks it. Not used for the `image`
   * provider, whose token is composed from `captcha.captchaId` + `captchaCode`.
   */
  executeCaptcha?: () => Promise<string>;
}

export interface UseAuthPageReturn {
  step: Ref<AuthStep>;
  account: Ref<string>;
  password: Ref<string>;
  code: Ref<string>;
  captchaCode: Ref<string>;
  submitting: Ref<boolean>;
  error: Ref<string>;
  notice: Ref<string>;
  accountType: ComputedRef<'email' | 'phone' | undefined>;
  busy: ComputedRef<boolean>;
  canContinue: ComputedRef<boolean>;
  challenge: Ref<TwoFactorChallenge | null>;
  twoFactorMethod: Ref<TwoFactorMethodName | undefined>;
  otherTwoFactorMethods: ComputedRef<TwoFactorMethodName[]>;
  captcha: Ref<LoginCaptchaData | null>;
  /** The revealed challenge is the built-in picture (typed code) rather than a provider widget. */
  captchaIsImage: ComputedRef<boolean>;
  pendingAction: Ref<PendingActionChallenge | null>;
  reset: () => void;
  backToIdentify: () => void;
  onContinue: () => Promise<void>;
  onPasswordSubmit: () => Promise<void>;
  onCodeSubmit: () => Promise<void>;
  onRegisterSubmit: () => Promise<void>;
  onTwoFactorSubmit: () => Promise<void>;
  switchTo: (next: AuthStep) => Promise<void>;
  useTwoFactorMethod: (method: TwoFactorMethodName) => Promise<void>;
}

export function useAuthPage(options: UseAuthPageOptions): UseAuthPageReturn {
  const t = options.translate;

  const step = ref<AuthStep>('identify');
  const account = ref('');
  const password = ref('');
  const code = ref('');
  const submitting = ref(false);
  const error = ref('');
  const notice = ref('');

  /** Detected from what the user typed - drives the backend's channel split. */
  const accountType = computed<'email' | 'phone' | undefined>(() => {
    const value = account.value.trim();
    if (!value) return undefined;
    if (value.includes('@')) return 'email';
    if (/^\+?[\d\s-]{6,}$/.test(value)) return 'phone';
    return undefined;
  });

  const busy = computed(() => submitting.value || (options.loading?.() ?? false));
  const canContinue = computed(() => account.value.trim().length > 0 && !busy.value);

  // -- Two-factor / captcha / pending action -----------------------------------
  const challenge = ref<TwoFactorChallenge | null>(null);
  const twoFactorMethod = ref<TwoFactorMethodName | undefined>(undefined);
  const captcha = ref<LoginCaptchaData | null>(null);
  const captchaCode = ref('');
  const captchaIsImage = computed(() => captcha.value?.provider === 'image');

  /**
   * The account owes something before it can be used (forced password change,
   * authenticator enrolment, email confirmation).
   *
   * ★ This shell has no module for discharging those - that flow lives in
   * `@tnzi/ui-admin`'s `PendingActions` page, and a product built on this shell
   * should route to its own equivalent. What matters here is that we do NOT
   * pretend the sign-in succeeded: the backend answered with a challenge, not a
   * token, so `onAuthenticated` must not fire and the user must be told why.
   * Silently swallowing it is how "a correct password with nowhere to go"
   * happens.
   */
  const pendingAction = ref<PendingActionChallenge | null>(null);

  /**
   * Set by every helper that reveals a challenge (2FA, pending action,
   * captcha) and cleared when an attempt starts. A callback that raised one
   * returned without a session, whatever else the state says: the captcha
   * in particular stays revealed across attempts, so "is the challenge
   * visible" cannot tell this attempt's refusal from the last one's.
   */
  let challengeRaised = false;

  const helpers = {
    setTwoFactorRequired: (next: TwoFactorChallenge) => {
      challengeRaised = true;
      challenge.value = next;
      twoFactorMethod.value = next.method;
      step.value = 'two-factor';
      void options.focusFirstField?.();
    },
    clearTwoFactor: () => {
      challenge.value = null;
    },
    setPendingActionRequired: (next: PendingActionChallenge) => {
      challengeRaised = true;
      pendingAction.value = next;
      error.value = t(
        'auth.errors.pendingActions',
        'Your account must complete a required action before signing in: {actions}.',
      ).replace('{actions}', next.requiredActions.join(', '));
    },
    clearPendingAction: () => {
      pendingAction.value = null;
    },
    setCaptchaRequired: (next: LoginCaptchaData) => {
      challengeRaised = true;
      captcha.value = next;
      captchaCode.value = '';
    },
    clearCaptcha: () => {
      captcha.value = null;
      captchaCode.value = '';
    },
  };

  /**
   * Clears what a fresh attempt must not inherit: the messages, and the
   * pending-action challenge. The challenge used to survive here, so once an
   * account had reported one in this instance every later successful login
   * (another account, or the same one after discharging it elsewhere) was
   * silently withheld from `onAuthenticated` until a reload.
   */
  function reset(): void {
    error.value = '';
    notice.value = '';
    pendingAction.value = null;
    challengeRaised = false;
  }

  function backToIdentify(): void {
    reset();
    password.value = '';
    code.value = '';
    step.value = 'identify';
  }

  function describeError(e: unknown): string {
    if (e instanceof Error && e.message) return e.message;
    return t('auth.errors.generic', 'Something went wrong. Please try again.');
  }

  async function run(fn: () => Promise<void>): Promise<void> {
    if (busy.value) return;
    reset();
    submitting.value = true;
    try {
      await fn();
    } catch (e) {
      error.value = describeError(e);
    } finally {
      submitting.value = false;
    }
  }

  /**
   * A callback that raised a challenge during this attempt (2FA, pending
   * action, captcha) did NOT sign us in. The shared callbacks return without
   * throwing on all three; only "no challenge" means a session was set.
   */
  const settledWithSession = () => !challengeRaised && !pendingAction.value;

  /**
   * `Continue` does NOT ask the backend whether the account exists - no endpoint
   * offers that, and one that did would be an account-enumeration oracle. It
   * moves to the password pane, which also carries the routes to the code and
   * register flows. That keeps the first screen to a single field while leaving
   * every enabled path one tap away.
   */
  async function onContinue(): Promise<void> {
    if (!canContinue.value) return;
    reset();
    const features = options.features();
    if (features.passwordLogin) {
      step.value = 'password';
    } else if (features.codeLogin) {
      await sendCode('code-login');
      step.value = 'code';
    } else {
      error.value = t('auth.errors.noMethod', 'Sign-in is not available right now.');
    }
    await options.focusFirstField?.();
  }

  async function onPasswordSubmit(): Promise<void> {
    const call = options.callbacks().pwdLogin;
    if (!call) {
      error.value = t('auth.errors.notConfigured', 'Password sign-in is not configured.');
      return;
    }
    await run(async () => {
      // A revealed challenge must be answered before the backend will look at
      // the password again. The picture composes its token here; a provider
      // widget produces it through the page (`executeCaptcha`) - which is also
      // the only moment the invisible providers (reCAPTCHA v3) make one.
      let captchaToken: string | undefined;
      if (captcha.value) {
        if (captchaIsImage.value) {
          captchaToken = composeImageCaptchaToken(captcha.value.captchaId, captchaCode.value);
          if (!captchaToken) throw new Error(t('auth.captcha.required', 'Please complete the captcha.'));
        } else if (options.executeCaptcha) {
          captchaToken = await options.executeCaptcha();
        }
      }
      await call(
        {
          // `userName` carries whatever identifier the user typed - the backend
          // resolves username / email / phone from the one field (the admin page
          // feeds it the same way). No `type` here: unlike the code flows, the
          // password endpoint does not split the identifier by channel.
          userName: account.value.trim(),
          password: password.value,
          captchaToken,
        },
        helpers,
      );
      if (settledWithSession()) options.onAuthenticated();
    });
  }

  async function sendCode(purpose: 'code-login' | 'register' | 'reset-pwd'): Promise<void> {
    const call = options.callbacks().sendCode;
    if (!call) throw new Error(t('auth.errors.notConfigured', 'This flow is not configured.'));
    await call({ account: account.value.trim(), type: accountType.value, purpose });
    notice.value = t('auth.notice.codeSent', 'Verification code sent.');
  }

  async function onCodeSubmit(): Promise<void> {
    const call = options.callbacks().codeLogin;
    if (!call) {
      error.value = t('auth.errors.notConfigured', 'Code sign-in is not configured.');
      return;
    }
    await run(async () => {
      await call({ account: account.value.trim(), code: code.value, type: accountType.value }, helpers);
      if (settledWithSession()) options.onAuthenticated();
    });
  }

  /**
   * Sign-up creates the account and sets its password; it establishes NO
   * session (`QuickRegisterResultDto` carries no tokens - the shared callback's
   * own comment says "the login shell returns to pwd-login on success"). So
   * this returns to the password pane with a notice and keeps the identifier,
   * exactly as the admin login page does. Emitting `authenticated` here sent
   * the user into the route guard, which bounced them straight back to this
   * same pane with no feedback.
   */
  async function onRegisterSubmit(): Promise<void> {
    const call = options.callbacks().register;
    if (!call) {
      error.value = t('auth.errors.notConfigured', 'Sign-up is not configured.');
      return;
    }
    await run(async () => {
      await call({
        account: account.value.trim(),
        code: code.value,
        password: password.value,
        type: accountType.value,
      });
      await switchTo('password');
      notice.value = t('auth.notice.registered', 'Account created. Sign in with your password.');
    });
  }

  async function onTwoFactorSubmit(): Promise<void> {
    const call = options.callbacks().verifyTwoFactor;
    if (!call) {
      error.value = t('auth.errors.notConfigured', 'Two-factor verification is not configured.');
      return;
    }
    await run(async () => {
      // `helpers` is what lets the callback report a pending action instead
      // of throwing "verification failed" at a correct code.
      await call(
        {
          challengeId: challenge.value?.challengeId,
          code: code.value,
          method: twoFactorMethod.value,
        },
        helpers,
      );
      if (settledWithSession()) options.onAuthenticated();
    });
  }

  async function switchTo(next: AuthStep): Promise<void> {
    reset();
    code.value = '';
    password.value = '';
    if (next === 'code') await run(() => sendCode('code-login'));
    if (next === 'register') await run(() => sendCode('register'));
    step.value = next;
    await options.focusFirstField?.();
  }

  const otherTwoFactorMethods = computed(() =>
    (challenge.value?.methods ?? []).filter((m) => m !== twoFactorMethod.value),
  );

  async function useTwoFactorMethod(method: TwoFactorMethodName): Promise<void> {
    twoFactorMethod.value = method;
    code.value = '';
    const resend = options.callbacks().resendTwoFactor;
    if (method !== 'totp' && resend) {
      await run(async () => {
        const result = await resend({ challengeId: challenge.value?.challengeId, method });
        const masked = result && 'maskedAddress' in result ? result.maskedAddress : undefined;
        notice.value = masked
          ? t('auth.notice.codeSentTo', 'Verification code sent to {to}.').replace('{to}', masked)
          : t('auth.notice.codeSent', 'Verification code sent.');
      });
    }
    await options.focusFirstField?.();
  }

  return {
    step,
    account,
    password,
    code,
    captchaCode,
    submitting,
    error,
    notice,
    accountType,
    busy,
    canContinue,
    challenge,
    twoFactorMethod,
    otherTwoFactorMethods,
    captcha,
    captchaIsImage,
    pendingAction,
    reset,
    backToIdentify,
    onContinue,
    onPasswordSubmit,
    onCodeSubmit,
    onRegisterSubmit,
    onTwoFactorSubmit,
    switchTo,
    useTwoFactorMethod,
  };
}
