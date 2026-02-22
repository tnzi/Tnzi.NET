<script setup lang="ts">
import { computed, onUnmounted, ref } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
import type { IPasswordResetEmits, IPasswordResetProps } from '@tnzi/core/components';
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from '../primitive/ui';

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

const email = ref('');
const code = ref('');
const password = ref('');
const confirmPassword = ref('');
const countdown = ref(0);

const isLoading = computed(() => props.loading);
const isDisabled = computed(() => props.disabled);
const canSendCode = computed(() => !isDisabled.value && email.value && countdown.value === 0);
const passwordMismatch = computed(() => confirmPassword.value !== '' && confirmPassword.value !== password.value);

let timer: ReturnType<typeof setInterval> | null = null;

const startCountdown = () => {
  countdown.value = props.countdownSeconds;
  timer = setInterval(() => {
    countdown.value -= 1;
    if (countdown.value <= 0 && timer) {
      clearInterval(timer);
      timer = null;
    }
  }, 1000);
};

const handleSendCode = () => {
  if (!canSendCode.value) return;
  emit('sendCode', email.value);
  startCountdown();
};

const handleSubmit = () => {
  if (passwordMismatch.value) return;
  emit('submit', {
    email: email.value,
    code: code.value,
    password: password.value,
  });
};

const handleCancel = () => emit('cancel');

onUnmounted(() => {
  if (timer) {
    clearInterval(timer);
    timer = null;
  }
});
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
              variant="outline"
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
            variant="outline"
            class="flex-1"
            :disabled="isDisabled || isLoading"
            @click="handleCancel"
          >
            {{ props.cancelLabel || t('common.cancel') }}
          </Button>
          <Button
            type="submit"
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

