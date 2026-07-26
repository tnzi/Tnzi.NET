<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { OAuthSocialProvider } from '@tnzi/core/types/shared-ui';
import { useLoginForm } from '../../headless/useLoginForm';

interface ILoginFormProps {
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
}

interface ILoginFormEmits {
  submit: [
    credentials: {
      userName: string;
      password: string;
      rememberMe?: boolean;
      captchaId?: string;
      captchaCode?: string;
    }
  ];
  forgotPassword: [];
  socialLogin: [provider: OAuthSocialProvider];
}

const { t } = useI18n();

const props = withDefaults(defineProps<ILoginFormProps>(), {
  showRememberMe: true,
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
  onSubmit: async (credentials) => {
    emit('submit', {
      userName: credentials.userName,
      password: credentials.password,
      rememberMe: props.showRememberMe ? credentials.rememberMe : undefined,
      captchaId: props.showCaptcha ? props.captchaId : undefined,
      captchaCode: props.showCaptcha ? credentials.captchaCode : undefined,
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
        :label="props.captchaLabel || t('auth.verificationCode')"
        :placeholder="props.captchaPlaceholder || t('auth.enterVerificationCode')"
        :disabled="isDisabled"
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
