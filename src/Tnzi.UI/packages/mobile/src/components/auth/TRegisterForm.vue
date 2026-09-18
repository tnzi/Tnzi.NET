<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import { composeImageCaptchaToken } from '@tnzi/core/services/captcha';
import type { OAuthSocialProvider } from '@tnzi/core/types/shared-ui';
import { useRegisterForm } from '../../headless/useRegisterForm';

interface IRegisterFormProps {
  showUsername?: boolean;
  showPhone?: boolean;
  showSocialLogin?: boolean;
  socialProviders?: OAuthSocialProvider[];
  loading?: boolean;
  disabled?: boolean;
  emailLabel?: string;
  usernameLabel?: string;
  phoneLabel?: string;
  passwordLabel?: string;
  confirmPasswordLabel?: string;
  submitLabel?: string;
  showLoginLink?: boolean;
  loginLinkText?: string;
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
  /** Minimum password length enforced by the field rules (default: 6) */
  passwordMinLength?: number;
}

interface IRegisterFormEmits {
  submit: [
    data: {
      email: string;
      password: string;
      userName?: string;
      firstName?: string;
      lastName?: string;
      phoneNumber?: string;
      captchaId?: string;
      captchaCode?: string;
      /** Unified captcha token (`{captchaId}:{code}` for the picture, the widget's token otherwise). */
      captchaToken?: string;
    }
  ];
  login: [];
  socialLogin: [provider: OAuthSocialProvider];
}

const { t } = useI18n();

const props = withDefaults(defineProps<IRegisterFormProps>(), {
  showUsername: true,
  showPhone: false,
  showSocialLogin: false,
  socialProviders: () => [],
  loading: false,
  disabled: false,
  showLoginLink: true,
  loginLinkText: '',
  showCaptcha: false,
  captchaUrl: '',
  captchaLabel: '',
  captchaPlaceholder: '',
  captchaToken: '',
  passwordMinLength: 6,
});

const emit = defineEmits<IRegisterFormEmits>();

// Not part of the credential payload: the captcha answer and the terms checkbox
// are gates in front of submit, so they stay local to the component.
const captchaCode = ref('');
const agreedToTerms = ref(false);

// Field state and validation come from the headless composable; see TLoginForm
// for why the reactive options are declared as getters.
const form = useRegisterForm({
  get showUsername() {
    return props.showUsername;
  },
  get showPhone() {
    return props.showPhone;
  },
  onSubmit: (data) => {
    // The terms gate is an invariant of the component, not just a disabled
    // button: implicit form submission must not slip past it either. (The
    // captcha needs no such gate: its Vant rule runs before van-form emits submit.)
    if (!agreedToTerms.value) return;
    emit('submit', {
      email: data.email,
      password: data.password,
      userName: props.showUsername ? data.userName : undefined,
      phoneNumber: props.showPhone ? data.phoneNumber : undefined,
      captchaId: props.showCaptcha ? props.captchaId : undefined,
      captchaCode: props.showCaptcha ? captchaCode.value : undefined,
      captchaToken:
        props.captchaToken || (props.showCaptcha ? composeImageCaptchaToken(props.captchaId, captchaCode.value) : undefined),
    });
  },
  onLogin: () => emit('login'),
});

const { email, userName, phoneNumber, password, confirmPassword } = form.fields;
const { passwordMismatch } = form;

const isDisabled = computed(() => props.disabled);
const isLoading = computed(() => props.loading || form.isSubmitting.value);

const emailRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: t('auth.email') }) },
  { pattern: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: t('form.invalidEmail') },
]);
const usernameRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: t('auth.username') }) },
]);
const phoneRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: t('auth.phone') }) },
]);
const passwordRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: t('auth.password') }) },
  {
    validator: (val: string) => val.length >= props.passwordMinLength,
    message: t('auth.passwordMinLength', { min: props.passwordMinLength }),
  },
]);
const confirmPasswordRules = computed(() => [
  { required: true, message: t('auth.pleaseConfirm', { field: t('auth.password') }) },
  { validator: (val: string) => val === password.value, message: t('auth.passwordMismatch') },
]);
const captchaRules = computed(() => [
  { required: true, message: t('auth.pleaseEnter', { field: props.captchaLabel || t('auth.verificationCode') }) },
]);

const handleSocialLogin = (provider: NonNullable<IRegisterFormProps['socialProviders']>[number]) => {
  emit('socialLogin', provider);
};
</script>

<template>
  <div class="rounded-xl bg-van-surface">
    <van-form @submit="form.submit">
      <van-field
        v-model="email"
        name="email"
        :label="props.emailLabel || t('auth.email')"
        :disabled="isDisabled"
        :rules="emailRules"
      />
      <van-field
        v-if="props.showUsername"
        v-model="userName"
        name="userName"
        :label="props.usernameLabel || t('auth.username')"
        :disabled="isDisabled"
        :rules="usernameRules"
      />
      <van-field
        v-if="props.showPhone"
        v-model="phoneNumber"
        name="phoneNumber"
        :label="props.phoneLabel || t('auth.phone')"
        :disabled="isDisabled"
        :rules="phoneRules"
      />
      <van-field
        v-model="password"
        name="password"
        type="password"
        :label="props.passwordLabel || t('auth.password')"
        :disabled="isDisabled"
        :rules="passwordRules"
      />
      <van-field
        v-model="confirmPassword"
        name="confirmPassword"
        type="password"
        :label="props.confirmPasswordLabel || t('auth.confirmPassword')"
        :disabled="isDisabled"
        :rules="confirmPasswordRules"
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
            @click="props.onRefreshCaptcha?.()"
          />
        </template>
      </van-field>

      <!-- A provider widget the consumer renders (Turnstile / hCaptcha / reCAPTCHA / Altcha). -->
      <div v-if="$slots.captcha" class="mb-3 px-4">
        <slot name="captcha" />
      </div>

      <div v-if="props.showLoginLink" class="px-4 pb-2 pt-1 text-center text-sm">
        <span class="text-van-muted">{{ t('auth.hasAccount') }}</span>
        <button
          type="button"
          class="ml-1 border-0 bg-transparent text-van-primary"
          :disabled="isDisabled"
          @click="form.login"
        >
          {{ props.loginLinkText || t('auth.loginNow') }}
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

      <div class="px-4 py-2">
        <van-checkbox v-model="agreedToTerms" :disabled="isDisabled" shape="square">
          {{ t('auth.agreeTerms') }}
        </van-checkbox>
      </div>

      <div class="px-4 pb-4">
        <van-button
          round
          block
          type="primary"
          native-type="submit"
          :loading="isLoading"
          :disabled="isDisabled || passwordMismatch || !agreedToTerms"
        >
          {{ props.submitLabel || t('auth.register') }}
        </van-button>
      </div>
    </van-form>
  </div>
</template>
