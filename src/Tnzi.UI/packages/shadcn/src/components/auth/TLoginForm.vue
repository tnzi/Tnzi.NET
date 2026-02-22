<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { ILoginFormProps, ILoginFormEmits } from '@tnzi/core/components';
import { Button, Card, CardContent, Input, Label, Checkbox } from '../primitive/ui';

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

const username = ref('');
const password = ref('');
const rememberMe = ref(false);
const captchaCode = ref('');

const isLoading = computed(() => props.loading);
const isDisabled = computed(() => props.disabled);

const handleSubmit = () => {
  emit('submit', {
    userName: username.value,
    password: password.value,
    rememberMe: props.showRememberMe ? rememberMe.value : undefined,
    captchaId: props.showCaptcha ? props.captchaId : undefined,
    captchaCode: props.showCaptcha ? captchaCode.value : undefined,
  });
};

const handleForgotPassword = () => emit('forgotPassword');
const handleSocialLogin = (provider: NonNullable<ILoginFormProps['socialProviders']>[number]) => emit('socialLogin', provider);
const handleRefreshCaptcha = () => props.onRefreshCaptcha?.();
</script>

<template>
  <Card class="w-full max-w-md">
    <CardContent class="pt-6">
      <form class="space-y-4" @submit.prevent="handleSubmit">
      <div class="space-y-2">
        <Label>
          {{ props.usernameLabel || t('auth.username') }}
        </Label>
        <Input
          v-model="username"
          type="text"
          :placeholder="props.usernamePlaceholder || t('auth.username')"
          :disabled="isDisabled"
        />
      </div>

      <div class="space-y-2">
        <Label>
          {{ props.passwordLabel || t('auth.password') }}
        </Label>
        <Input
          v-model="password"
          type="password"
          :placeholder="props.passwordPlaceholder || t('auth.password')"
          :disabled="isDisabled"
        />
      </div>

      <div v-if="props.showCaptcha" class="space-y-2">
        <Label>
          {{ props.captchaLabel || t('auth.verificationCode') }}
        </Label>
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

      <div
        v-if="props.showRememberMe || props.showForgotPassword"
        class="flex items-center justify-between text-sm"
      >
        <label v-if="props.showRememberMe" class="flex items-center gap-2 text-muted-foreground">
          <Checkbox v-model="rememberMe" :disabled="isDisabled" />
          {{ t('auth.rememberMe') }}
        </label>
        <Button
          v-if="props.showForgotPassword"
          type="button"
          variant="link"
          size="sm"
          :disabled="isDisabled"
          @click="handleForgotPassword"
        >
          {{ t('auth.forgotPassword') }}
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
        :disabled="isDisabled || isLoading"
      >
        <span v-if="isLoading">{{ t('common.loading') }}</span>
        <span v-else>{{ props.submitLabel || t('auth.login') }}</span>
      </Button>
    </form>
    </CardContent>
  </Card>
</template>

