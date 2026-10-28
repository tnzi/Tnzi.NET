<script setup lang="ts">
import { requiredRule } from '../form/required-rule';
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
  /**
   * Send the recovery code; bind it as `@send-code`. A prop rather than an
   * event so the form can wait for it: the countdown starts only when it
   * resolves, and a rejection keeps the button available and shows its message
   * under the code field. An event cannot report back, so a send the server
   * refused used to lock the button anyway.
   *
   * `captchaToken` is the {@link captchaToken} prop: the backend requires one on
   * this call when captcha on password recovery is enabled.
   */
  onSendCode?: (email: string, captchaToken?: string) => void | Promise<void>;
  /**
   * Token from a consumer-rendered captcha widget (Turnstile / hCaptcha /
   * reCAPTCHA / Altcha via `useCaptchaWidget`), rendered through the `#captcha`
   * slot. Passed to `onSendCode`.
   */
  captchaToken?: string;
}

interface IPasswordResetEmits {
  submit: [data: { email: string; code: string; password: string }];
  cancel: [];
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
  onSendCode: undefined,
  captchaToken: '',
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
  onSendCode: (address) => props.onSendCode?.(address, props.captchaToken || undefined),
});

const { email, code, password, confirmPassword } = form.fields;
const { countdown, passwordMismatch } = form;
// Why the last send failed; the composable clears it on the next success.
const sendError = computed(() => form.errors.value.code ?? '');

const isDisabled = computed(() => props.disabled);
const isLoading = computed(() => props.loading || form.isSubmitting.value);
const canSendCode = computed(() => form.canSendCode.value && !props.disabled);

// Mirror of the checks in usePasswordReset.submit(). The composable refuses an
// incomplete submit and writes `errors`, but a refusal nobody renders looks
// like a dead button; the Vant rules put the same message under the field.
const pleaseEnter = (field: string) => [requiredRule(t('auth.pleaseEnter', { field }))];
const emailRules = computed(() => pleaseEnter(props.emailLabel || t('auth.email')));
const codeRules = computed(() => pleaseEnter(props.codeLabel || t('auth.verificationCode')));
const passwordRules = computed(() => pleaseEnter(props.passwordLabel || t('auth.newPassword')));
const confirmPasswordRules = computed(() => [
  requiredRule(t('auth.pleaseConfirm', { field: props.passwordLabel || t('auth.newPassword') })),
  { validator: (val: string) => val === password.value, message: t('auth.passwordMismatch') },
]);

onUnmounted(() => form.dispose());
</script>

<template>
  <div class="rounded-xl bg-van-surface">
    <van-form @submit="form.submit">
      <van-field
        v-model="email"
        name="email"
        type="email"
        :label="props.emailLabel || t('auth.email')"
        :placeholder="t('auth.email')"
        :disabled="isDisabled"
        :rules="emailRules"
        required
      />

      <!-- A provider widget the consumer renders (Turnstile / hCaptcha / reCAPTCHA / Altcha). -->
      <div v-if="$slots.captcha" class="mb-3 px-4">
        <slot name="captcha" />
      </div>

      <van-field
        v-model="code"
        name="code"
        :label="props.codeLabel || t('auth.verificationCode')"
        :placeholder="t('auth.enterVerificationCode')"
        :error-message="sendError"
        :disabled="isDisabled"
        :rules="codeRules"
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
        name="password"
        type="password"
        :label="props.passwordLabel || t('auth.newPassword')"
        :placeholder="t('auth.newPassword')"
        :disabled="isDisabled"
        :rules="passwordRules"
        required
      />

      <van-field
        v-model="confirmPassword"
        name="confirmPassword"
        type="password"
        :label="props.confirmPasswordLabel || t('auth.confirmPassword')"
        :placeholder="t('auth.confirmPassword')"
        :error-message="passwordMismatch ? t('auth.passwordMismatch') : ''"
        :disabled="isDisabled"
        :rules="confirmPasswordRules"
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
