<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { ILoginFormEmits, ILoginFormProps } from '@tnzi/core/components';

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

const userName = ref('');
const password = ref('');
const rememberMe = ref(false);
const captchaCode = ref('');

const isLoading = computed(() => props.loading);
const isDisabled = computed(() => props.disabled);

const handleSubmit = () => {
  emit('submit', {
    userName: userName.value,
    password: password.value,
    rememberMe: props.showRememberMe ? rememberMe.value : undefined,
    captchaId: props.showCaptcha ? props.captchaId : undefined,
    captchaCode: props.showCaptcha ? captchaCode.value : undefined,
  });
};

const handleSocialLogin = (provider: NonNullable<ILoginFormProps['socialProviders']>[number]) => {
  emit('socialLogin', provider);
};
</script>

<template>
  <div class="tz-vant-card">
    <van-form @submit="handleSubmit">
      <van-field
        v-model="userName"
        :label="props.usernameLabel || t('auth.username')"
        :placeholder="props.usernamePlaceholder || t('auth.username')"
        :disabled="isDisabled"
        required
      />
      <van-field
        v-model="password"
        type="password"
        :label="props.passwordLabel || t('auth.password')"
        :placeholder="props.passwordPlaceholder || t('auth.password')"
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

      <div v-if="props.showRememberMe || props.showForgotPassword" class="mb-3 flex items-center justify-between px-4 text-sm">
        <van-checkbox v-if="props.showRememberMe" v-model="rememberMe" :disabled="isDisabled">
          {{ t('auth.rememberMe') }}
        </van-checkbox>
        <button
          v-if="props.showForgotPassword"
          type="button"
          class="border-0 bg-transparent text-[#1989fa]"
          :disabled="isDisabled"
          @click="emit('forgotPassword')"
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

<style scoped>
.tz-vant-card {
  border-radius: 12px;
  background: #fff;
  overflow: hidden;
}
</style>

