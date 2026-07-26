<script setup lang="ts">
import { computed, onUnmounted } from 'vue';
import { useI18n } from '@tnzi/core/adapters/i18n';
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

const { t } = useI18n();

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

// Field state, the resend countdown and its timer cleanup all come from the
// headless composable; see TLoginForm for why the options are getters.
const form = usePasswordReset({
  get countdownSeconds() {
    return props.countdownSeconds;
  },
  onSubmit: (data) => emit('submit', data),
  onCancel: () => emit('cancel'),
  onSendCode: (address) => emit('sendCode', address),
});

const { email, code, password, confirmPassword } = form.fields;
const { countdown, passwordMismatch } = form;

const isDisabled = computed(() => props.disabled);
const isLoading = computed(() => props.loading || form.isSubmitting.value);
const canSendCode = computed(() => form.canSendCode.value && !props.disabled);

onUnmounted(() => form.dispose());
</script>

<template>
  <div class="rounded-xl bg-van-surface">
    <van-form @submit="form.submit">
      <van-field
        v-model="email"
        type="email"
        :label="props.emailLabel || t('auth.email')"
        :placeholder="t('auth.email')"
        :disabled="isDisabled"
        required
      />

      <van-field
        v-model="code"
        :label="props.codeLabel || t('auth.verificationCode')"
        :placeholder="t('auth.enterVerificationCode')"
        :disabled="isDisabled"
        required
      >
        <template #button>
          <van-button
            size="small"
            type="primary"
            :disabled="!canSendCode"
            @click="form.sendCode"
          >
            <span v-if="countdown > 0">{{ countdown }}s</span>
            <span v-else>{{ props.sendCodeLabel || t('auth.sendCode') }}</span>
          </van-button>
        </template>
      </van-field>

      <van-field
        v-model="password"
        type="password"
        :label="props.passwordLabel || t('auth.newPassword')"
        :placeholder="t('auth.newPassword')"
        :disabled="isDisabled"
        required
      />

      <van-field
        v-model="confirmPassword"
        type="password"
        :label="props.confirmPasswordLabel || t('auth.confirmPassword')"
        :placeholder="t('auth.confirmPassword')"
        :error-message="passwordMismatch ? t('auth.passwordMismatch') : ''"
        :disabled="isDisabled"
        required
      />

      <div class="flex gap-3 px-4 pb-4 pt-2">
        <van-button
          round
          block
          plain
          :disabled="isDisabled || isLoading"
          @click="form.cancel"
        >
          {{ props.cancelLabel || t('common.cancel') }}
        </van-button>
        <van-button
          round
          block
          type="primary"
          native-type="submit"
          :loading="isLoading"
          :disabled="isDisabled || passwordMismatch"
        >
          {{ props.submitLabel || t('auth.resetPassword') }}
        </van-button>
      </div>
    </van-form>
  </div>
</template>
