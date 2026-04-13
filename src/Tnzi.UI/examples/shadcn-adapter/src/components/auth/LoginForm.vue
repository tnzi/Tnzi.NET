<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { OAuthSocialProvider } from '@tnzi/core/types/shared-ui';
import { Button, Card, CardContent, Input, Label, Checkbox } from '../primitive/ui';
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

const isLoading = computed(() => props.loading);
const isDisabled = computed(() => props.disabled);

const { fields, forgotPassword: _forgotPassword, socialLogin: _socialLogin, refreshCaptcha: _refreshCaptcha } = useLoginForm({
  showCaptcha: props.showCaptcha,
  captchaId: props.captchaId,
  onRefreshCaptcha: props.onRefreshCaptcha,
});

const handleSubmit = () => {
  emit('submit', {
    userName: fields.username.value,
    password: fields.password.value,
    rememberMe: props.showRememberMe ? fields.rememberMe.value : undefined,
    captchaId: props.showCaptcha ? props.captchaId : undefined,
    captchaCode: props.showCaptcha ? fields.captchaCode.value : undefined,
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
          v-model="fields.username.value"
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
          v-model="fields.password.value"
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
            v-model="fields.captchaCode.value"
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
          <Checkbox v-model:checked="fields.rememberMe.value" :disabled="isDisabled" />
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
          variant="default"
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
        variant="primary"
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

