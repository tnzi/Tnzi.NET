<script setup lang="ts">
import { computed, onUnmounted } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from '../primitive/ui';
import { usePasswordReset } from '../../headless/usePasswordReset';

interface IPasswordResetProps {
  loading?: boolean;
  disabled?: boolean;
  emailLabel?: string;
  codeLabel?: string;
  passwordLabel?: string;
  confirmPasswordLabel?: string;
  submitLabel?: string;
  cancelLabel?: string;
  sendCodeLabel?: string;
  countdownSeconds?: number;
}

interface IPasswordResetEmits {
  submit: [data: { email: string; code: string; password: string }];
  cancel: [];
  sendCode: [email: string];
}

const props = withDefaults(defineProps<IPasswordResetProps>(), {
  loading: false,
  disabled: false,
  emailLabel: '',
  codeLabel: '',
  passwordLabel: '',
  confirmPasswordLabel: '',
  submitLabel: '',
  cancelLabel: '',
  sendCodeLabel: '',
  countdownSeconds: 60,
});

const emit = defineEmits<IPasswordResetEmits>();

const { t } = useI18n();

const { fields, countdown, passwordMismatch, canSendCode: canSendCodeBase, sendCode, submit: doSubmit, cancel: doCancel, dispose } = usePasswordReset({
  countdownSeconds: props.countdownSeconds,
  onSendCode: (email) => emit('sendCode', email),
  onSubmit: (data) => emit('submit', data),
  onCancel: () => emit('cancel'),
});

const { email, code, password, confirmPassword } = fields;

const isLoading = computed(() => props.loading);
const isDisabled = computed(() => props.disabled);
const canSendCode = computed(() => !isDisabled.value && canSendCodeBase.value);

const handleSendCode = () => sendCode();
const handleSubmit = () => doSubmit();
const handleCancel = () => doCancel();

onUnmounted(() => dispose());
</script>

<template>
  <Card class="w-full max-w-md">
    <CardHeader>
      <CardTitle class="text-center">{{ t('auth.resetPassword') }}</CardTitle>
    </CardHeader>
    <CardContent>
      <form class="space-y-4" @submit.prevent="handleSubmit">
        <div class="space-y-2">
          <Label>{{ props.emailLabel || t('auth.email') }}</Label>
          <Input
            v-model="email"
            type="email"
            :placeholder="t('auth.email')"
            :disabled="isDisabled"
          />
        </div>

        <div class="space-y-2">
          <Label>{{ props.codeLabel || t('auth.verificationCode') }}</Label>
          <div class="flex gap-2">
            <Input
              v-model="code"
              type="text"
              class="min-w-0 flex-1"
              :placeholder="t('auth.verificationCode')"
              :disabled="isDisabled"
            />
            <Button
              type="button"
              variant="default"
              size="sm"
              :disabled="!canSendCode"
              @click="handleSendCode"
            >
              <span v-if="countdown > 0">{{ countdown }}s</span>
              <span v-else>{{ props.sendCodeLabel || t('auth.sendCode') }}</span>
            </Button>
          </div>
        </div>

        <div class="space-y-2">
          <Label>{{ props.passwordLabel || t('auth.newPassword') }}</Label>
          <Input
            v-model="password"
            type="password"
            :placeholder="t('auth.newPassword')"
            :disabled="isDisabled"
          />
        </div>

        <div class="space-y-2">
          <Label>{{ props.confirmPasswordLabel || t('auth.confirmPassword') }}</Label>
          <Input
            v-model="confirmPassword"
            type="password"
            :class="passwordMismatch ? 'border-destructive focus-visible:ring-destructive' : ''"
            :placeholder="t('auth.confirmPassword')"
            :disabled="isDisabled"
          />
          <p v-if="passwordMismatch" class="text-xs text-destructive">{{ t('auth.passwordMismatch') }}</p>
        </div>

        <div class="flex gap-3 pt-2">
          <Button
            type="button"
            variant="default"
            class="flex-1"
            :disabled="isDisabled || isLoading"
            @click="handleCancel"
          >
            {{ props.cancelLabel || t('common.cancel') }}
          </Button>
          <Button
            type="submit"
            variant="primary"
            class="flex-[1.5]"
            :disabled="isDisabled || isLoading || passwordMismatch"
          >
            <span v-if="isLoading">{{ t('common.loading') }}</span>
            <span v-else>{{ props.submitLabel || t('auth.resetPassword') }}</span>
          </Button>
        </div>
      </form>
    </CardContent>
  </Card>
</template>

