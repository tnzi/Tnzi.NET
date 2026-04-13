<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { OAuthSocialProvider } from '@tnzi/core/types/shared-ui';

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
});

const emit = defineEmits<IRegisterFormEmits>();

const email = ref('');
const userName = ref('');
const phoneNumber = ref('');
const password = ref('');
const confirmPassword = ref('');
const captchaCode = ref('');
const agreedToTerms = ref(false);

const isDisabled = computed(() => props.disabled);
const passwordMismatch = computed(() => confirmPassword.value !== '' && confirmPassword.value !== password.value);

const emailRules = [
  { required: true, message: 'Email is required' },
  { pattern: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: 'Invalid email format' },
];
const passwordRules = [
  { required: true, message: 'Password is required' },
  { validator: (val: string) => val.length >= 6, message: 'Password must be at least 6 characters' },
];
const confirmPasswordRules = [
  { required: true, message: 'Confirm password is required' },
  { validator: (val: string) => val === password.value, message: 'Passwords do not match' },
];

const handleSubmit = () => {
  if (passwordMismatch.value) return;
  if (!agreedToTerms.value) return;

  emit('submit', {
    email: email.value,
    password: password.value,
    userName: props.showUsername ? userName.value : undefined,
    phoneNumber: props.showPhone ? phoneNumber.value : undefined,
    captchaId: props.showCaptcha ? props.captchaId : undefined,
    captchaCode: props.showCaptcha ? captchaCode.value : undefined,
  });
};

const handleSocialLogin = (provider: NonNullable<IRegisterFormProps['socialProviders']>[number]) => {
  emit('socialLogin', provider);
};
</script>

<template>
  <div class="rounded-xl bg-white">
    <van-form @submit="handleSubmit">
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
        :label="props.usernameLabel || t('auth.username')"
        :disabled="isDisabled"
      />
      <van-field
        v-if="props.showPhone"
        v-model="phoneNumber"
        :label="props.phoneLabel || t('auth.phone')"
        :disabled="isDisabled"
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
            @click="props.onRefreshCaptcha?.()"
          />
        </template>
      </van-field>

      <div v-if="props.showLoginLink" class="px-4 pb-2 pt-1 text-center text-sm">
        <span class="text-slate-500">{{ t('auth.hasAccount') }}</span>
        <button
          type="button"
          class="ml-1 border-0 bg-transparent text-[#1989fa]"
          :disabled="isDisabled"
          @click="emit('login')"
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
          {{ t('auth.agreeToTerms') }}
        </van-checkbox>
      </div>

      <div class="px-4 pb-4">
        <van-button round block type="primary" native-type="submit" :loading="props.loading" :disabled="isDisabled || passwordMismatch || !agreedToTerms">
          {{ props.submitLabel || t('auth.register') }}
        </van-button>
      </div>
    </van-form>
  </div>
</template>
