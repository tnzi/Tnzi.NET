<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IRegisterFormEmits, IRegisterFormProps } from '@tnzi/core/components';
import { Button, Card, CardContent, Input, Label } from '../primitive/ui';

const { t } = useI18n();

const props = withDefaults(defineProps<IRegisterFormProps>(), {
  showUsername: true,
  showPhone: false,
  showSocialLogin: false,
  socialProviders: () => [],
  loading: false,
  disabled: false,
  emailLabel: '',
  usernameLabel: '',
  phoneLabel: '',
  passwordLabel: '',
  confirmPasswordLabel: '',
  submitLabel: '',
  showLoginLink: true,
  loginLinkText: '',
  showCaptcha: false,
  captchaUrl: '',
  captchaLabel: '',
  captchaPlaceholder: '',
});

const emit = defineEmits<IRegisterFormEmits>();

const email = ref('');
const username = ref('');
const phone = ref('');
const password = ref('');
const confirmPassword = ref('');
const captchaCode = ref('');

const isLoading = computed(() => props.loading);
const isDisabled = computed(() => props.disabled);
const passwordMismatch = computed(() => confirmPassword.value !== '' && confirmPassword.value !== password.value);

const handleSubmit = () => {
  if (passwordMismatch.value) {
    return;
  }

  emit('submit', {
    email: email.value,
    password: password.value,
    userName: props.showUsername ? username.value : undefined,
    phoneNumber: props.showPhone ? phone.value : undefined,
    captchaId: props.showCaptcha ? props.captchaId : undefined,
    captchaCode: props.showCaptcha ? captchaCode.value : undefined,
  });
};

const handleLogin = () => emit('login');
const handleSocialLogin = (provider: NonNullable<IRegisterFormProps['socialProviders']>[number]) => emit('socialLogin', provider);
const handleRefreshCaptcha = () => props.onRefreshCaptcha?.();
</script>

<template>
  <Card class="w-full max-w-md">
    <CardContent class="pt-6">
      <form class="space-y-4" @submit.prevent="handleSubmit">
      <div class="space-y-2">
        <Label>{{ props.emailLabel || t('auth.email') }}</Label>
        <Input
          v-model="email"
          type="email"
          :disabled="isDisabled"
          :placeholder="t('auth.email')"
        />
      </div>

      <div v-if="props.showUsername" class="space-y-2">
        <Label>{{ props.usernameLabel || t('auth.username') }}</Label>
        <Input
          v-model="username"
          type="text"
          :disabled="isDisabled"
          :placeholder="t('auth.username')"
        />
      </div>

      <div v-if="props.showPhone" class="space-y-2">
        <Label>{{ props.phoneLabel || t('auth.phone') }}</Label>
        <Input
          v-model="phone"
          type="tel"
          :disabled="isDisabled"
          :placeholder="t('auth.phone')"
        />
      </div>

      <div class="space-y-2">
        <Label>{{ props.passwordLabel || t('auth.password') }}</Label>
        <Input
          v-model="password"
          type="password"
          :disabled="isDisabled"
          :placeholder="t('auth.password')"
        />
      </div>

      <div class="space-y-2">
        <Label>{{ props.confirmPasswordLabel || t('auth.confirmPassword') }}</Label>
        <Input
          v-model="confirmPassword"
          type="password"
          :class="passwordMismatch ? 'border-destructive focus-visible:ring-destructive' : ''"
          :disabled="isDisabled"
          :placeholder="t('auth.confirmPassword')"
        />
        <p v-if="passwordMismatch" class="text-xs text-destructive">{{ t('auth.passwordMismatch') }}</p>
      </div>

      <div v-if="props.showCaptcha" class="space-y-2">
        <Label>{{ props.captchaLabel || t('auth.verificationCode') }}</Label>
        <div class="flex gap-2">
          <Input
            v-model="captchaCode"
            type="text"
            class="min-w-0 flex-1"
            :placeholder="props.captchaPlaceholder || t('auth.enterVerificationCode')"
            :disabled="isDisabled"
          />
          <img
            v-if="props.captchaUrl"
            :src="props.captchaUrl"
            alt="captcha"
            class="h-10 w-24 cursor-pointer rounded-md border object-cover"
            @click="handleRefreshCaptcha"
          />
        </div>
      </div>

      <div v-if="props.showLoginLink" class="text-center text-sm text-muted-foreground">
        <span>{{ t('auth.hasAccount') }}</span>
        <Button
          type="button"
          variant="link"
          size="sm"
          :disabled="isDisabled"
          @click="handleLogin"
        >
          {{ props.loginLinkText || t('auth.loginNow') }}
        </Button>
      </div>

      <div
        v-if="props.showSocialLogin && props.socialProviders && props.socialProviders.length > 0"
        class="grid grid-cols-3 gap-2"
      >
        <Button
          v-for="provider in props.socialProviders"
          :key="provider"
          type="button"
          variant="outline"
          size="sm"
          class="capitalize"
          :disabled="isDisabled"
          @click="handleSocialLogin(provider)"
        >
          {{ provider }}
        </Button>
      </div>

      <Button
        type="submit"
        class="w-full"
        :disabled="isDisabled || isLoading || passwordMismatch"
      >
        <span v-if="isLoading">{{ t('common.loading') }}</span>
        <span v-else>{{ props.submitLabel || t('auth.register') }}</span>
      </Button>
    </form>
    </CardContent>
  </Card>
</template>

