<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { OAuthSocialProvider } from '@tnzi/core/types/shared-ui';
import { useLoginForm } from '../../headless/useLoginForm';

interface ILoginFormProps {
  /**
   * Render a "remember me" checkbox and forward its value in `submit`.
   * Off by default: nothing in the framework reads the flag (the core
   * AuthStateManager persists the session the same way either way), so it is
   * a consumer-owned opt-in until session-scoped token persistence exists.
   */
  showRememberMe?: boolean;
  showForgotPassword?: boolean;
  showSocialLogin?: boolean;
  socialProviders?: OAuthSocialProvider[];
  loading?: boolean;
  disabled?: boolean;
  usernameLabel?: string;
  passwordLabel?: string;
  submitLabel?: string;
  usernamePlaceholder?: string;
  passwordPlaceholder?: string;
  showCaptcha?: boolean;
  captchaId?: string;
  captchaUrl?: string;
  onRefreshCaptcha?: () => void;
  captchaLabel?: string;
  captchaPlaceholder?: string;
  /**
   * Token from a consumer-rendered captcha widget (Turnstile / hCaptcha /
   * reCAPTCHA / Altcha via `useCaptchaWidget`), rendered through the `#captcha`
   * slot. Forwarded as `captchaToken`; wins over the picture's id + code.
   */
  captchaToken?: string;
}

interface ILoginFormEmits {
  submit: [
    credentials: {
      userName: string;
      password: string;
      rememberMe?: boolean;
      captchaId?: string;
      captchaCode?: string;
      /** Unified captcha token (`{captchaId}:{code}` for the picture, the widget's token otherwise). */
      captchaToken?: string;
    }
  ];
  forgotPassword: [];
  socialLogin: [provider: OAuthSocialProvider];
}

const { t } = useI18n();

const props = withDefaults(defineProps<ILoginFormProps>(), {
  showRememberMe: false,
  showForgotPassword: true,
  showSocialLogin: false,
  socialProviders: () => [],
  loading: false,
  disabled: false,
  usernameLabel: '',
  passwordLabel: '',
  submitLabel: '',
  usernamePlaceholder: '',
  passwordPlaceholder: '',
  showCaptcha: false,
  captchaUrl: '',
  captchaLabel: '',
  captchaPlaceholder: '',
  captchaToken: '',
});

const emit = defineEmits<ILoginFormEmits>();

// State and validation live in the headless composable so the logic stays
// testable and shared with consumers that draw their own UI. The options are
// declared as getters because the composable reads them lazily (inside computed
// / validate), which is what keeps them tracking the live props.
const form = useLoginForm({
  get showCaptcha() {
    return props.showCaptcha;
  },
  get captchaId() {
    return props.captchaId;
  },
  get captchaToken() {
    return props.captchaToken;
  },
  onSubmit: async (credentials) => {
    emit('submit', {
      userName: credentials.userName,
      password: credentials.password,
      rememberMe: props.showRememberMe ? credentials.rememberMe : undefined,
      captchaId: props.showCaptcha ? props.captchaId : undefined,
      captchaCode: props.showCaptcha ? credentials.captchaCode : undefined,
      captchaToken: props.captchaToken || (props.showCaptcha ? credentials.captchaToken : undefined),
    });
  },
  onForgotPassword: () => emit('forgotPassword'),
  onRefreshCaptcha: () => props.onRefreshCaptcha?.(),
});

const { username, password, rememberMe, captchaCode } = form.fields;

const isLoading = computed(() => props.loading || form.isSubmitting.value);
const isDisabled = computed(() => props.disabled);

const usernameRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: props.usernameLabel || t('auth.username') }) },
]);
const passwordRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: props.passwordLabel || t('auth.password') }) },
]);
// useLoginForm.validate() rejects a blank captcha whenever `showCaptcha` is on;
// the rule makes that refusal visible instead of a submit that does nothing.
const captchaRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: props.captchaLabel || t('auth.verificationCode') }) },
]);

const handleSocialLogin = (provider: NonNullable<ILoginFormProps['socialProviders']>[number]) => {
  emit('socialLogin', provider);
};
</script>

<template>
  <div class="overflow-hidden rounded-xl bg-van-surface">
    <van-form @submit="form.submit">
      <van-field
        v-model="username"
        name="username"
        :label="props.usernameLabel || t('auth.username')"
        :placeholder="props.usernamePlaceholder || t('auth.username')"
        :disabled="isDisabled"
        :rules="usernameRules"
      />
      <van-field
        v-model="password"
        name="password"
        type="password"
        :label="props.passwordLabel || t('auth.password')"
        :placeholder="props.passwordPlaceholder || t('auth.password')"
        :disabled="isDisabled"
        :rules="passwordRules"
      />

      <van-field
        v-if="props.showCaptcha"
        v-model="captchaCode"
        name="captchaCode"
        :label="props.captchaLabel || t('auth.verificationCode')"
        :placeholder="props.captchaPlaceholder || t('auth.enterVerificationCode')"
        :disabled="isDisabled"
        :rules="captchaRules"
      >
        <template #button>
          <img
            v-if="props.captchaUrl"
            :src="props.captchaUrl"
            alt="captcha"
            class="h-8 w-20 rounded"
            @click="form.refreshCaptcha"
          />
        </template>
      </van-field>

      <!-- A provider widget the consumer renders (Turnstile / hCaptcha / reCAPTCHA / Altcha). -->
      <div v-if="$slots.captcha" class="mb-3 px-4">
        <slot name="captcha" />
      </div>

      <div v-if="props.showRememberMe || props.showForgotPassword" class="mb-3 flex items-center justify-between px-4 text-sm">
        <van-checkbox v-if="props.showRememberMe" v-model="rememberMe" :disabled="isDisabled">
          {{ t('auth.rememberMe') }}
        </van-checkbox>
        <button
          v-if="props.showForgotPassword"
          type="button"
          class="border-0 bg-transparent text-van-primary"
          :disabled="isDisabled"
          @click="form.forgotPassword"
        >
          {{ t('auth.forgotPassword') }}
        </button>
      </div>

      <div
        v-if="props.showSocialLogin && props.socialProviders && props.socialProviders.length > 0"
        class="grid grid-cols-2 gap-2 px-4 pb-3"
      >
        <van-button
          v-for="provider in props.socialProviders"
          :key="provider"
          plain
          size="small"
          :disabled="isDisabled"
          @click="handleSocialLogin(provider)"
        >
          {{ provider }}
        </van-button>
      </div>

      <div class="px-4 pb-4">
        <van-button round block type="primary" native-type="submit" :loading="isLoading" :disabled="isDisabled">
          {{ props.submitLabel || t('auth.login') }}
        </van-button>
      </div>
    </van-form>
  </div>
</template>
