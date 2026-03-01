<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IRegisterFormEmits, IRegisterFormProps } from '@tnzi/core/components';

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

const isDisabled = computed(() => props.disabled);
const passwordMismatch = computed(() => confirmPassword.value !== '' && confirmPassword.value !== password.value);

const handleSubmit = () => {
  if (passwordMismatch.value) return;

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
      <van-field v-model="email" :label="props.emailLabel || t('auth.email')" :disabled="isDisabled" required />
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
        type="password"
        :label="props.passwordLabel || t('auth.password')"
        :disabled="isDisabled"
        required
      />
      <van-field
        v-model="confirmPassword"
        type="password"
        :label="props.confirmPasswordLabel || t('auth.confirmPassword')"
        :error-message="passwordMismatch ? t('auth.passwordMismatch') : ''"
        :disabled="isDisabled"
        required
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

      <div class="px-4 pb-4">
        <van-button round block type="primary" native-type="submit" :loading="props.loading" :disabled="isDisabled || passwordMismatch">
          {{ props.submitLabel || t('auth.register') }}
        </van-button>
      </div>
    </van-form>
  </div>
</template>

