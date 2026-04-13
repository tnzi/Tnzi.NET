<template>
  <n-form
    ref="formRef"
    class="t-register-form"
    :show-label="false"
    @submit.prevent="handleSubmit"
  >
    <slot name="header" />

    <slot name="extra-fields-before" :state="state" />

    <slot name="default-fields" :state="state" :loading="loading">
      <n-form-item :feedback="state.errors.username">
        <n-input
          v-model:value="state.username"
          :placeholder="usernameLabel"
          :disabled="loading"
        />
      </n-form-item>

      <n-form-item :feedback="state.errors.email">
        <n-input
          v-model:value="state.email"
          :placeholder="emailLabel"
          :disabled="loading"
        />
      </n-form-item>

      <n-form-item v-if="showPhone" :feedback="state.errors.phone">
        <n-input
          v-model:value="state.phone"
          :placeholder="phoneLabel"
          :disabled="loading"
        />
      </n-form-item>

      <n-form-item :feedback="state.errors.password">
        <n-input
          v-model:value="state.password"
          type="password"
          show-password-on="click"
          :placeholder="passwordLabel"
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

      <n-form-item :feedback="state.errors.agreeTerms">
        <n-checkbox v-model:checked="state.agreeTerms">
          <slot name="terms-label">{{ termsLabel }}</slot>
        </n-checkbox>
      </n-form-item>
    </slot>

    <slot name="extra-fields-after" :state="state" />

    <slot name="before-submit" :state="state" />

    <n-button
      type="primary"
      block
      :loading="loading"
      class="t-register-form__submit"
      @click="handleSubmit"
    >
      <slot name="submit-label">{{ submitLabel }}</slot>
    </n-button>

    <slot name="social-providers" />

    <slot name="footer-links" :on-login="() => emit('switch-mode', 'login')" />
  </n-form>
</template>

<script setup lang="ts">
import { NForm, NFormItem, NInput, NButton, NCheckbox } from 'naive-ui'
import { useRegisterForm } from '../../composables/auth/useRegisterForm'
import type { RegisterProvider, RegisterCredentials } from '../../composables/auth/types'

interface Props {
  /** Injectable registration provider. Required. */
  provider: RegisterProvider
  /** Initial field values. */
  initialValues?: Partial<RegisterCredentials>
  /** Require phone field. */
  requirePhone?: boolean
  /** Show phone field (optional when requirePhone is false). */
  showPhone?: boolean
  /** Label overrides. */
  usernameLabel?: string
  emailLabel?: string
  phoneLabel?: string
  passwordLabel?: string
  confirmPasswordLabel?: string
  termsLabel?: string
  submitLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  requirePhone: false,
  showPhone: true,
  usernameLabel: 'Username',
  emailLabel: 'Email',
  phoneLabel: 'Phone',
  passwordLabel: 'Password',
  confirmPasswordLabel: 'Confirm password',
  termsLabel: 'I agree to the Terms of Service',
  submitLabel: 'Create account',
})

const emit = defineEmits<{
  submit: [credentials: RegisterCredentials]
  success: [user: unknown]
  error: [err: Error]
  'switch-mode': [mode: 'login']
  'before-submit': [state: RegisterCredentials]
}>()

const { state, loading, handleSubmit: composableSubmit } = useRegisterForm({
  provider: props.provider,
  initialValues: props.initialValues,
  requirePhone: props.requirePhone,
  onSubmit: (credentials) => emit('submit', credentials),
  onSuccess: (user) => emit('success', user),
  onError: (err) => emit('error', err),
})

async function handleSubmit(): Promise<void> {
  emit('before-submit', { ...state.value })
  await composableSubmit()
}

defineExpose({ state, loading, handleSubmit })
</script>

<style scoped>
.t-register-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-register-form__submit {
  margin-top: 8px;
}
</style>
