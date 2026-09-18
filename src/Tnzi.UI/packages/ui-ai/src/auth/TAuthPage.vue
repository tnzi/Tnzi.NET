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
import { computed, watch, nextTick } from 'vue';
import { Icon } from '@iconify/vue';
import type { SessionEndReason } from '@tnzi/core/state';
import { useCaptchaWidget } from '@tnzi/core/services/captcha';
import {
  DEFAULT_LOGIN_FEATURES,
  type LoginCallbacks,
  type LoginFeatures,
  type LoginThirdPartyProvider,
  type Translate,
} from '@tnzi/ui';
import { useAuthPage } from '../headless/useAuthPage';
import TAuthField from './TAuthField.vue';
import TAuthProviderButton from './TAuthProviderButton.vue';

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
    /**
     * Why the previous session ended (`runtime.auth.sessionEndReason`),
     * rendered as a notice above the pane. `'security'` means the backend
     * revoked the session because the credentials looked stolen - the one
     * message the user has no other way to receive, so it gets the warning
     * tone rather than the routine "expired" one.
     */
    sessionEndReason?: SessionEndReason | null;
    /**
     * Turns the captcha config's relative challenge URL into an absolute one
     * (Altcha fetches its challenge itself). `TAuthRoute` passes
     * `runtime.http.resolveUrl`; leave unset when the API lives at the page origin.
     */
    resolveUrl?: (url: string) => string;
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
    sessionEndReason: null,
    resolveUrl: undefined,
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

async function focusFirstField(): Promise<void> {
  await nextTick();
  const el = document.querySelector<HTMLInputElement>('.t-auth__pane input:not([disabled])');
  el?.focus();
}

// A provider-rendered captcha (Turnstile / hCaptcha / reCAPTCHA / Altcha) for
// the password pane. Mounts into `captchaContainer` only while the backend has
// revealed a non-image challenge; the built-in picture is rendered inline below.
const captchaWidget = useCaptchaWidget({
  config: () => (captcha.value && !captchaIsImage.value ? features.value.captcha : null),
  purpose: 'login',
  client: props.resolveUrl ? { resolveUrl: props.resolveUrl } : undefined,
  translate: t,
});
const captchaContainer = captchaWidget.container;

// The state machine and every submit handler live in `useAuthPage` (unit
// tested there); this file is markup and copy.
const {
  step,
  account,
  password,
  code,
  captchaCode,
  error,
  notice,
  busy,
  canContinue,
  challenge,
  twoFactorMethod,
  otherTwoFactorMethods,
  captcha,
  captchaIsImage,
  reset,
  backToIdentify,
  onContinue,
  onPasswordSubmit,
  onCodeSubmit,
  onRegisterSubmit,
  onTwoFactorSubmit,
  switchTo,
  useTwoFactorMethod,
} = useAuthPage({
  callbacks: () => props.callbacks,
  features: () => features.value,
  translate: t,
  loading: () => props.loading,
  onAuthenticated: () => emit('authenticated'),
  focusFirstField,
  executeCaptcha: () => captchaWidget.execute(),
});

// A rejected submit leaves the challenge on screen; the widget's token was
// consumed by the attempt, so it has to be solved again.
watch(error, (message) => {
  if (message && captcha.value && !captchaIsImage.value) captchaWidget.reset();
});

/** Placeholder + label follow what the deployment actually accepts. */
const identifierLabel = computed(() => {
  const id = features.value.identifiers;
  if (id.email && id.phone) return t('auth.identifier.emailOrPhone', 'Email or phone');
  if (id.phone && !id.email) return t('auth.identifier.phone', 'Phone number');
  if (id.userName && !id.email && !id.phone) return t('auth.identifier.userName', 'Username');
  return t('auth.identifier.email', 'Email address');
});

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

const sessionNotice = computed<{ text: string; tone: 'warning' | 'info' } | null>(() => {
  switch (props.sessionEndReason) {
    case 'security':
      return {
        tone: 'warning',
        text: t(
          'auth.notice.sessionEndedForSecurity',
          'Your session was ended for security reasons. Please sign in again.',
        ),
      };
    case 'expired':
      return {
        tone: 'info',
        text: t('auth.notice.sessionExpired', 'Your session expired. Please sign in again.'),
      };
    default:
      return null;
  }
});
const subheadingText = computed(() => props.subheading || '');
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

      <p
        v-if="sessionNotice"
        data-test="t-auth-session-notice"
        class="t-auth__session-notice"
        :class="`t-auth__session-notice--${sessionNotice.tone}`"
        role="status"
      >
        {{ sessionNotice.text }}
      </p>

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
        <div v-if="captcha && captchaIsImage" class="t-auth__captcha">
          <img :src="`data:image/png;base64,${captcha.imageBase64 ?? ''}`" :alt="t('auth.captcha.alt', 'Captcha')" />
          <TAuthField
            v-model="captchaCode"
            :placeholder="t('auth.captcha.placeholder', 'Captcha')"
            :aria-label="t('auth.captcha.label', 'Captcha')"
            :disabled="busy"
            @submit="onPasswordSubmit"
          />
        </div>
        <div v-else-if="captcha" class="t-auth__captcha t-auth__captcha--widget">
          <div ref="captchaContainer" />
          <p v-if="captchaWidget.error.value" class="t-auth__captcha-error">{{ captchaWidget.error.value }}</p>
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

/* Provider widgets (Turnstile / hCaptcha / reCAPTCHA / Altcha) bring their own box. */
.t-auth__captcha--widget {
  flex-direction: column;
  align-items: stretch;
}

.t-auth__captcha-error {
  margin: 0;
  font-size: 12px;
  color: var(--tnzi-ai-danger, #d03050);
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

.t-auth__session-notice {
  margin: 0 0 16px;
  padding: 10px 12px;
  border-radius: 8px;
  border: 1px solid var(--tnzi-ai-border);
  font-size: 13px;
  line-height: 18px;
  text-align: center;
  color: var(--tnzi-ai-text-secondary);
}

.t-auth__session-notice--warning {
  color: var(--tnzi-ai-warning);
  border-color: color-mix(in srgb, var(--tnzi-ai-warning) 40%, transparent);
  background: color-mix(in srgb, var(--tnzi-ai-warning) 10%, transparent);
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
