<template>
  <n-form
    ref="formRef"
    class="t-password-reset"
    :show-label="false"
    @submit.prevent="handleStep"
  >
    <slot name="header" :step="state.step" />

    <!-- Step 1: Request reset (email entry) -->
    <slot
      v-if="state.step === 'request'"
      name="request-step"
      :state="state"
      :loading="loading"
      :send-code="sendCode"
    >
      <n-form-item :feedback="state.errors.email">
        <n-input
          v-model:value="state.email"
          :placeholder="emailLabel"
          :disabled="loading"
        />
      </n-form-item>

      <n-button
        type="primary"
        block
        :loading="loading"
        class="t-password-reset__submit"
        @click="sendCode"
      >
        {{ sendCodeLabel }}
      </n-button>
    </slot>

    <!-- Step 2: Verify code + new password -->
    <slot
      v-else-if="state.step === 'verify'"
      name="verify-step"
      :state="state"
      :loading="loading"
      :countdown="state.countdown"
      :verify-and-reset="verifyAndReset"
      :resend="sendCode"
    >
      <n-form-item :feedback="state.errors.captcha">
        <n-input
          v-model:value="state.captcha"
          :placeholder="captchaLabel"
          :disabled="loading"
        />
      </n-form-item>

      <n-form-item :feedback="state.errors.newPassword">
        <n-input
          v-model:value="state.newPassword"
          type="password"
          show-password-on="click"
          :placeholder="newPasswordLabel"
          :disabled="loading"
        />
      </n-form-item>

      <n-form-item :feedback="state.errors.confirmPassword">
        <n-input
          v-model:value="state.confirmPassword"
          type="password"
          show-password-on="click"
          :placeholder="confirmPasswordLabel"
          :disabled="loading"
        />
      </n-form-item>

      <div class="t-password-reset__row">
        <a
          class="t-password-reset__resend"
          :class="{ 'is-disabled': state.countdown > 0 }"
          @click.prevent="state.countdown > 0 ? null : sendCode()"
        >
          {{ state.countdown > 0 ? `${resendLabel} (${state.countdown}s)` : resendLabel }}
        </a>
      </div>

      <n-button
        type="primary"
        block
        :loading="loading"
        class="t-password-reset__submit"
        @click="verifyAndReset"
      >
        {{ resetLabel }}
      </n-button>
    </slot>

    <!-- Step 3: Done -->
    <slot
      v-else
      name="done-step"
      :state="state"
      :on-back-to-login="() => emit('switch-mode', 'login')"
    >
      <div class="t-password-reset__done">
        {{ doneLabel }}
      </div>
      <n-button
        type="primary"
        block
        class="t-password-reset__submit"
        @click="emit('switch-mode', 'login')"
      >
        {{ backToLoginLabel }}
      </n-button>
    </slot>

    <slot name="footer-links" :on-login="() => emit('switch-mode', 'login')" />
  </n-form>
</template>

<script setup lang="ts">
import { NForm, NFormItem, NInput, NButton } from 'naive-ui'
import { usePasswordReset } from '../../composables/auth/usePasswordReset'
import type { PasswordResetProvider } from '../../composables/auth/types'

interface Props {
  /** Injectable password reset provider. Required. */
  provider: PasswordResetProvider
  /** Countdown seconds for resend button. */
  countdownSeconds?: number
  /** Label overrides. */
  emailLabel?: string
  captchaLabel?: string
  newPasswordLabel?: string
  confirmPasswordLabel?: string
  sendCodeLabel?: string
  resendLabel?: string
  resetLabel?: string
  doneLabel?: string
  backToLoginLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  countdownSeconds: 60,
  emailLabel: 'Email',
  captchaLabel: 'Verification code',
  newPasswordLabel: 'New password',
  confirmPasswordLabel: 'Confirm password',
  sendCodeLabel: 'Send code',
  resendLabel: 'Resend code',
  resetLabel: 'Reset password',
  doneLabel: 'Your password has been reset successfully.',
  backToLoginLabel: 'Back to sign in',
})

const emit = defineEmits<{
  success: []
  error: [err: Error]
  'switch-mode': [mode: 'login']
  'step-change': [step: 'request' | 'verify' | 'done']
}>()

const { state, loading, sendCode, verifyAndReset } = usePasswordReset({
  provider: props.provider,
  countdownSeconds: props.countdownSeconds,
  onSuccess: () => emit('success'),
  onError: (err) => emit('error', err),
})

function handleStep(): void {
  if (state.value.step === 'request') {
    void sendCode()
  } else if (state.value.step === 'verify') {
    void verifyAndReset()
  }
}

defineExpose({ state, loading, sendCode, verifyAndReset })
</script>

<style scoped>
.t-password-reset {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-password-reset__row {
  display: flex;
  justify-content: flex-end;
  font-size: 14px;
}
.t-password-reset__resend {
  color: var(--tnzi-primary-500);
  cursor: pointer;
}
.t-password-reset__resend.is-disabled {
  color: var(--tnzi-text-disabled, #999);
  cursor: not-allowed;
}
.t-password-reset__resend:not(.is-disabled):hover {
  text-decoration: underline;
}
.t-password-reset__submit {
  margin-top: 8px;
}
.t-password-reset__done {
  text-align: center;
  padding: 16px;
  color: var(--tnzi-success-500, #18a058);
}
</style>
