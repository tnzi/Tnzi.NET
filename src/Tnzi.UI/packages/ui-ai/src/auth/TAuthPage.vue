<script setup lang="ts">
/**
 * `TAuthPage` - the sign-in / sign-up page for a conversational AI product.
 *
 * ## Why this is not `@tnzi/ui-admin`'s login page
 *
 * The two serve different products and the difference is the interaction model,
 * not the styling. The admin page presents every route up front (password /
 * code / register / recover as switchable modules) because an operator knows
 * which one they need. A consumer product asks for ONE identifier and decides
 * what comes next - the "identifier-first" pattern every current AI product
 * uses. Rendering four tabs at a stranger is a different product decision, not
 * a different skin, so the two pages stay separate.
 *
 * What they DO share is all the logic: `@tnzi/ui` owns the login stack
 * (`LoginCallbacks`, feature gating off `GET /auth/config`, account-type
 * detection, captcha, two-factor). This page is a different arrangement of the
 * same contracts, so a consumer wires it exactly like the admin one.
 *
 * ## Layout
 *
 * Measured against a live product session (2026-08-02) - measurements are
 * facts, not copyrighted expression:
 *
 *   - no card: a centred single column on the page background
 *   - column `min(360px, 100% - 24px)` - the ONLY thing that changes on a
 *     phone. Type sizes, control heights and radii stay put; a 24px radius
 *     button does not become a 16px one because the viewport narrowed
 *   - provider buttons `radius 10px`, input + primary button `radius 8px` -
 *     the difference is deliberate hierarchy, do not unify it
 *
 * ## Copy
 *
 * Every string goes through the injected `translate` (the `Translate` shape
 * `@tnzi/ui`'s login stack already uses: `(key, fallback?) => string`), so a
 * consumer wires whichever i18n it has. Without one, the English fallbacks
 * below render - which is what makes the page usable before any wiring.
 */
import { computed, ref, watch, nextTick } from 'vue';
import { Icon } from '@iconify/vue';
import {
  DEFAULT_LOGIN_FEATURES,
  type LoginCallbacks,
  type LoginFeatures,
  type LoginThirdPartyProvider,
  type TwoFactorChallenge,
  type TwoFactorMethodName,
  type LoginCaptchaData,
  type Translate,
} from '@tnzi/ui';
import TAuthField from './TAuthField.vue';
import TAuthProviderButton from './TAuthProviderButton.vue';

/** Which pane fills the column. `identify` is always the entry point. */
type AuthStep = 'identify' | 'password' | 'code' | 'register' | 'two-factor';

const props = withDefaults(
  defineProps<{
    /** Wordmark above the heading. Omit to render no brand line. */
    brandName?: string;
    /** Iconify name rendered left of the wordmark. */
    brandIcon?: string;
    /** Overrides the default "Sign in or sign up" heading. */
    heading?: string;
    /** Secondary line under the heading. */
    subheading?: string;
    /** Backend-derived feature flags (`mapAuthConfig(authConfig)`). */
    features?: LoginFeatures;
    /** Third-party providers (`buildOAuthProviders(...)`). */
    providers?: readonly LoginThirdPartyProvider[];
    /** The auth callbacks - same contract the admin login page consumes. */
    callbacks?: LoginCallbacks;
    /** `(key, fallback?) => string`. Falls back to the English copy below. */
    translate?: Translate;
    /** Links rendered under the column. */
    termsHref?: string;
    privacyHref?: string;
    /** Footer line under the links (e.g. a copyright). */
    footnote?: string;
    /** Busy flag for the whole page (consumer-driven, e.g. during redirect). */
    loading?: boolean;
  }>(),
  {
    brandName: '',
    brandIcon: '',
    heading: '',
    subheading: '',
    features: undefined,
    providers: () => [],
    callbacks: () => ({}),
    translate: undefined,
    termsHref: '',
    privacyHref: '',
    footnote: '',
    loading: false,
  },
);

const emit = defineEmits<{
  /** A provider button was pressed. The consumer performs the navigation. */
  (e: 'oauth', provider: LoginThirdPartyProvider): void;
  /** Authentication completed - the consumer routes onward. */
  (e: 'authenticated'): void;
}>();

const t: Translate = (key, fallback) =>
  props.translate ? props.translate(key, fallback) : (fallback ?? key);

const features = computed<LoginFeatures>(() => props.features ?? DEFAULT_LOGIN_FEATURES);

// -- Step machine ------------------------------------------------------------
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

const busy = computed(() => submitting.value || props.loading);
const canContinue = computed(() => account.value.trim().length > 0 && !busy.value);

/** Placeholder + label follow what the deployment actually accepts. */
const identifierLabel = computed(() => {
  const id = features.value.identifiers;
  if (id.email && id.phone) return t('auth.identifier.emailOrPhone', 'Email or phone');
  if (id.phone && !id.email) return t('auth.identifier.phone', 'Phone number');
  if (id.userName && !id.email && !id.phone) return t('auth.identifier.userName', 'Username');
  return t('auth.identifier.email', 'Email address');
});

function reset(): void {
  error.value = '';
  notice.value = '';
}

function backToIdentify(): void {
  reset();
  password.value = '';
  code.value = '';
  step.value = 'identify';
}

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
  if (features.value.passwordLogin) {
    step.value = 'password';
  } else if (features.value.codeLogin) {
    await sendCode('code-login');
    step.value = 'code';
  } else {
    error.value = t('auth.errors.noMethod', 'Sign-in is not available right now.');
  }
  await focusFirstField();
}

async function focusFirstField(): Promise<void> {
  await nextTick();
  const el = document.querySelector<HTMLInputElement>('.t-auth__pane input:not([disabled])');
  el?.focus();
}

function describeError(e: unknown): string {
  if (e instanceof Error && e.message) return e.message;
  return t('auth.errors.generic', 'Something went wrong. Please try again.');
}

// -- Two-factor --------------------------------------------------------------
const challenge = ref<TwoFactorChallenge | null>(null);
const twoFactorMethod = ref<TwoFactorMethodName | undefined>(undefined);
const captcha = ref<LoginCaptchaData | null>(null);
const captchaCode = ref('');

const helpers = {
  setTwoFactorRequired: (next: TwoFactorChallenge) => {
    challenge.value = next;
    twoFactorMethod.value = next.method;
    step.value = 'two-factor';
    void focusFirstField();
  },
  clearTwoFactor: () => {
    challenge.value = null;
  },
  setCaptchaRequired: (next: LoginCaptchaData) => {
    captcha.value = next;
    captchaCode.value = '';
  },
  clearCaptcha: () => {
    captcha.value = null;
    captchaCode.value = '';
  },
};

// -- Submissions -------------------------------------------------------------
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

function onPasswordSubmit(): void {
  const call = props.callbacks.pwdLogin;
  if (!call) {
    error.value = t('auth.errors.notConfigured', 'Password sign-in is not configured.');
    return;
  }
  void run(async () => {
    await call(
      {
        // `userName` carries whatever identifier the user typed - the backend
        // resolves username / email / phone from the one field (the admin page
        // feeds it the same way). No `type` here: unlike the code flows, the
        // password endpoint does not split the identifier by channel.
        userName: account.value.trim(),
        password: password.value,
        remember: true,
        captchaId: captcha.value?.captchaId,
        captchaCode: captchaCode.value || undefined,
      },
      helpers,
    );
    // A pending two-factor challenge means the callback moved us on already.
    if (step.value !== 'two-factor') emit('authenticated');
  });
}

async function sendCode(purpose: 'code-login' | 'register' | 'reset-pwd'): Promise<void> {
  const call = props.callbacks.sendCode;
  if (!call) throw new Error(t('auth.errors.notConfigured', 'This flow is not configured.'));
  await call({ account: account.value.trim(), type: accountType.value, purpose });
  notice.value = t('auth.notice.codeSent', 'Verification code sent.');
}

function onCodeSubmit(): void {
  const call = props.callbacks.codeLogin;
  if (!call) {
    error.value = t('auth.errors.notConfigured', 'Code sign-in is not configured.');
    return;
  }
  void run(async () => {
    await call({ account: account.value.trim(), code: code.value, type: accountType.value }, helpers);
    if (step.value !== 'two-factor') emit('authenticated');
  });
}

function onRegisterSubmit(): void {
  const call = props.callbacks.register;
  if (!call) {
    error.value = t('auth.errors.notConfigured', 'Sign-up is not configured.');
    return;
  }
  void run(async () => {
    await call({
      account: account.value.trim(),
      code: code.value,
      password: password.value,
      type: accountType.value,
    });
    emit('authenticated');
  });
}

function onTwoFactorSubmit(): void {
  const call = props.callbacks.verifyTwoFactor;
  if (!call) {
    error.value = t('auth.errors.notConfigured', 'Two-factor verification is not configured.');
    return;
  }
  void run(async () => {
    await call({
      challengeId: challenge.value?.challengeId,
      code: code.value,
      method: twoFactorMethod.value,
    });
    emit('authenticated');
  });
}

async function switchTo(next: AuthStep): Promise<void> {
  reset();
  code.value = '';
  password.value = '';
  if (next === 'code') await run(() => sendCode('code-login'));
  if (next === 'register') await run(() => sendCode('register'));
  step.value = next;
  await focusFirstField();
}

// Typing anywhere clears a stale message: leaving "Incorrect password" on
// screen while the user edits it reads as if the new attempt already failed.
//
// ★ Only an actual EDIT counts. Switching panes blanks these fields
// programmatically and then sets its own notice ("Verification code sent"); a
// watcher that fired on any change would race that notice and could wipe it,
// depending on when the pre-flush queue happens to run. Keying on "a field
// gained content" removes the timing question entirely.
watch([account, password, code, captchaCode], (next, prev) => {
  const edited = next.some((value, i) => value && value !== prev[i]);
  if (edited && (error.value || notice.value)) reset();
});

const headingText = computed(
  () => props.heading || t('auth.heading', 'Sign in or sign up'),
);
const subheadingText = computed(() => props.subheading || '');

const otherTwoFactorMethods = computed(() =>
  (challenge.value?.methods ?? []).filter((m) => m !== twoFactorMethod.value),
);

async function useTwoFactorMethod(method: TwoFactorMethodName): Promise<void> {
  twoFactorMethod.value = method;
  code.value = '';
  const resend = props.callbacks.resendTwoFactor;
  if (method !== 'totp' && resend) {
    await run(async () => {
      const result = await resend({ challengeId: challenge.value?.challengeId, method });
      const masked = result && 'maskedAddress' in result ? result.maskedAddress : undefined;
      notice.value = masked
        ? t('auth.notice.codeSentTo', 'Verification code sent to {to}.').replace('{to}', masked)
        : t('auth.notice.codeSent', 'Verification code sent.');
    });
  }
  await focusFirstField();
}
</script>

<template>
  <main class="t-auth">
    <div class="t-auth__column">
      <header v-if="brandName || brandIcon" class="t-auth__brand">
        <slot name="brand">
          <Icon v-if="brandIcon" class="t-auth__brand-mark" :icon="brandIcon" />
          <span v-if="brandName" class="t-auth__brand-name">{{ brandName }}</span>
        </slot>
      </header>

      <h1 class="t-auth__heading">{{ headingText }}</h1>
      <h2 v-if="subheadingText" class="t-auth__subheading">{{ subheadingText }}</h2>

      <!-- Step: identify -->
      <div v-if="step === 'identify'" class="t-auth__pane">
        <div v-if="providers.length" class="t-auth__providers">
          <TAuthProviderButton
            v-for="p in providers"
            :key="p.key"
            :provider="p"
            :disabled="busy"
            :label="t(`auth.provider.${p.key}`, `Continue with ${p.label}`)"
            @select="emit('oauth', p)"
          />
        </div>

        <div v-if="providers.length" class="t-auth__divider">
          <span>{{ t('auth.or', 'Or') }}</span>
        </div>

        <TAuthField
          v-model="account"
          :placeholder="t('auth.identifier.placeholder', 'Enter your email address')"
          :aria-label="identifierLabel"
          autocomplete="username"
          :disabled="busy"
          @submit="onContinue"
        />
        <button
          type="button"
          class="t-auth__primary"
          :disabled="!canContinue"
          @click="onContinue"
        >
          {{ t('auth.continue', 'Continue') }}
        </button>
      </div>

      <!-- Step: password -->
      <div v-else-if="step === 'password'" class="t-auth__pane">
        <p class="t-auth__account">
          {{ t('auth.signingInAs', 'Signing in as') }} <strong>{{ account }}</strong>
        </p>
        <TAuthField
          v-model="password"
          type="password"
          :placeholder="t('auth.password.placeholder', 'Password')"
          :aria-label="t('auth.password.label', 'Password')"
          autocomplete="current-password"
          :disabled="busy"
          @submit="onPasswordSubmit"
        />
        <div v-if="captcha" class="t-auth__captcha">
          <img :src="captcha.imageBase64" :alt="t('auth.captcha.alt', 'Captcha')" />
          <TAuthField
            v-model="captchaCode"
            :placeholder="t('auth.captcha.placeholder', 'Captcha')"
            :aria-label="t('auth.captcha.label', 'Captcha')"
            :disabled="busy"
            @submit="onPasswordSubmit"
          />
        </div>
        <button type="button" class="t-auth__primary" :disabled="busy" @click="onPasswordSubmit">
          {{ t('auth.signIn', 'Sign in') }}
        </button>
        <div class="t-auth__links">
          <button
            v-if="features.codeLogin"
            type="button"
            class="t-auth__link"
            :disabled="busy"
            @click="switchTo('code')"
          >
            {{ t('auth.useCode', 'Use a verification code') }}
          </button>
          <button
            v-if="features.register"
            type="button"
            class="t-auth__link"
            :disabled="busy"
            @click="switchTo('register')"
          >
            {{ t('auth.createAccount', 'Create an account') }}
          </button>
        </div>
      </div>

      <!-- Step: code sign-in -->
      <div v-else-if="step === 'code'" class="t-auth__pane">
        <p class="t-auth__account">
          {{ t('auth.codeSentTo', 'We sent a code to') }} <strong>{{ account }}</strong>
        </p>
        <TAuthField
          v-model="code"
          :placeholder="t('auth.code.placeholder', 'Verification code')"
          :aria-label="t('auth.code.label', 'Verification code')"
          autocomplete="one-time-code"
          inputmode="numeric"
          :disabled="busy"
          @submit="onCodeSubmit"
        />
        <button type="button" class="t-auth__primary" :disabled="busy" @click="onCodeSubmit">
          {{ t('auth.signIn', 'Sign in') }}
        </button>
      </div>

      <!-- Step: register -->
      <div v-else-if="step === 'register'" class="t-auth__pane">
        <p class="t-auth__account">
          {{ t('auth.codeSentTo', 'We sent a code to') }} <strong>{{ account }}</strong>
        </p>
        <TAuthField
          v-model="code"
          :placeholder="t('auth.code.placeholder', 'Verification code')"
          :aria-label="t('auth.code.label', 'Verification code')"
          autocomplete="one-time-code"
          inputmode="numeric"
          :disabled="busy"
        />
        <TAuthField
          v-model="password"
          type="password"
          :placeholder="t('auth.password.new', 'Choose a password')"
          :aria-label="t('auth.password.new', 'Choose a password')"
          autocomplete="new-password"
          :disabled="busy"
          @submit="onRegisterSubmit"
        />
        <button type="button" class="t-auth__primary" :disabled="busy" @click="onRegisterSubmit">
          {{ t('auth.createAccount', 'Create an account') }}
        </button>
      </div>

      <!-- Step: two-factor -->
      <div v-else class="t-auth__pane">
        <p class="t-auth__account">
          {{
            twoFactorMethod === 'totp'
              ? t('auth.twoFactor.totp', 'Enter the code from your authenticator app.')
              : challenge?.maskedAddress
                ? t('auth.twoFactor.sentTo', 'Enter the code we sent to {to}.').replace(
                    '{to}',
                    challenge.maskedAddress,
                  )
                : t('auth.twoFactor.sent', 'Enter the verification code we sent you.')
          }}
        </p>
        <TAuthField
          v-model="code"
          :placeholder="t('auth.code.placeholder', 'Verification code')"
          :aria-label="t('auth.code.label', 'Verification code')"
          autocomplete="one-time-code"
          inputmode="numeric"
          :disabled="busy"
          @submit="onTwoFactorSubmit"
        />
        <button type="button" class="t-auth__primary" :disabled="busy" @click="onTwoFactorSubmit">
          {{ t('auth.verify', 'Verify') }}
        </button>
        <div v-if="otherTwoFactorMethods.length" class="t-auth__links">
          <button
            v-for="m in otherTwoFactorMethods"
            :key="m"
            type="button"
            class="t-auth__link"
            :disabled="busy"
            @click="useTwoFactorMethod(m)"
          >
            {{ t(`auth.twoFactor.use.${m}`, `Use ${m}`) }}
          </button>
        </div>
      </div>

      <p v-if="error" class="t-auth__error" role="alert">{{ error }}</p>
      <p v-else-if="notice" class="t-auth__notice" role="status">{{ notice }}</p>

      <button
        v-if="step !== 'identify'"
        type="button"
        class="t-auth__link t-auth__back"
        :disabled="busy"
        @click="backToIdentify"
      >
        {{ t('auth.back', 'Back') }}
      </button>

      <p v-if="termsHref || privacyHref || footnote" class="t-auth__legal">
        <slot name="legal">
          <template v-if="termsHref || privacyHref">
            {{ t('auth.legal.prefix', 'By continuing, you agree to our') }}
            <a v-if="termsHref" :href="termsHref" target="_blank" rel="noopener">{{
              t('auth.legal.terms', 'Terms of Service')
            }}</a>
            <template v-if="termsHref && privacyHref">
              {{ t('auth.legal.and', 'and have read our') }}
            </template>
            <a v-if="privacyHref" :href="privacyHref" target="_blank" rel="noopener">{{
              t('auth.legal.privacy', 'Privacy Policy')
            }}</a>.
          </template>
          <span v-if="footnote" class="t-auth__footnote">{{ footnote }}</span>
        </slot>
      </p>
    </div>
  </main>
</template>

<style scoped>
.t-auth {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px 0 120px;
  background: var(--tnzi-ai-surface);
  color: var(--tnzi-ai-text);
  font-family: var(--tnzi-ai-font-body);
}

/* The only thing that changes on a phone. Type sizes and control heights are
   identical at 375px and 1440px - deliberately so. */
.t-auth__column {
  width: min(360px, 100% - 24px);
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 12px;
}

.t-auth__brand {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin-bottom: 12px;
}

.t-auth__brand-name {
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.t-auth__heading {
  margin: 0;
  font-size: 24px;
  font-weight: 500;
  line-height: 30px;
  text-align: center;
  color: var(--tnzi-ai-text);
}

.t-auth__subheading {
  margin: 0 0 12px;
  font-size: 14px;
  font-weight: 400;
  line-height: 22px;
  text-align: center;
  color: var(--tnzi-ai-text-secondary);
}

.t-auth__pane {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.t-auth__providers {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.t-auth__divider {
  display: flex;
  align-items: center;
  gap: 12px;
  color: var(--tnzi-ai-text-tertiary);
  font-size: 13px;
}

.t-auth__divider::before,
.t-auth__divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--tnzi-ai-border);
}

.t-auth__primary {
  height: 40px;
  border: none;
  border-radius: 8px;
  background: var(--tnzi-ai-accent);
  color: var(--tnzi-ai-on-accent);
  font-size: 14px;
  font-weight: 500;
  font-family: inherit;
  cursor: pointer;
  transition: opacity var(--tnzi-ai-duration-fast) var(--tnzi-ai-easing);
}

.t-auth__primary:hover:not(:disabled) {
  opacity: 0.9;
}

.t-auth__primary:disabled {
  background: var(--tnzi-ai-text-tertiary);
  cursor: not-allowed;
}

.t-auth__account {
  margin: 0;
  font-size: 14px;
  line-height: 20px;
  text-align: center;
  color: var(--tnzi-ai-text-secondary);
}

.t-auth__account strong {
  color: var(--tnzi-ai-text);
  font-weight: 500;
}

.t-auth__captcha {
  display: flex;
  align-items: center;
  gap: 8px;
}

.t-auth__captcha img {
  height: 40px;
  border-radius: 8px;
  border: 1px solid var(--tnzi-ai-border);
  flex-shrink: 0;
}

.t-auth__captcha :deep(.t-auth-field) {
  flex: 1;
  min-width: 0;
}

.t-auth__links {
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 12px;
}

.t-auth__link {
  border: none;
  background: none;
  padding: 0;
  font-size: 13px;
  font-family: inherit;
  color: var(--tnzi-ai-accent);
  cursor: pointer;
}

.t-auth__link:hover:not(:disabled) {
  text-decoration: underline;
}

.t-auth__link:disabled {
  color: var(--tnzi-ai-text-tertiary);
  cursor: not-allowed;
}

.t-auth__back {
  align-self: center;
  color: var(--tnzi-ai-text-secondary);
}

.t-auth__error {
  margin: 0;
  font-size: 13px;
  line-height: 18px;
  text-align: center;
  color: var(--tnzi-ai-danger);
}

.t-auth__notice {
  margin: 0;
  font-size: 13px;
  line-height: 18px;
  text-align: center;
  color: var(--tnzi-ai-text-secondary);
}

.t-auth__legal {
  margin: 24px 0 0;
  font-size: 12px;
  line-height: 18px;
  text-align: center;
  color: var(--tnzi-ai-text-tertiary);
}

.t-auth__legal a {
  color: var(--tnzi-ai-text-secondary);
  text-decoration: underline;
}

.t-auth__footnote {
  display: block;
  margin-top: 6px;
}
</style>
