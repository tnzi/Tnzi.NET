<template>
  <n-form
    ref="formRef"
    class="t-login-form"
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
          @update:value="emit('field-change', 'username', $event)"
        />
      </n-form-item>

      <n-form-item :feedback="state.errors.password">
        <n-input
          v-model:value="state.password"
          type="password"
          show-password-on="click"
          :placeholder="passwordLabel"
          :disabled="loading"
          @update:value="emit('field-change', 'password', $event)"
        />
      </n-form-item>

      <div class="t-login-form__row">
        <n-checkbox v-model:checked="state.rememberMe">
          {{ rememberMeLabel }}
        </n-checkbox>
        <a class="t-login-form__forgot" @click.prevent="emit('forgot-password')">
          {{ forgotPasswordLabel }}
        </a>
      </div>
    </slot>

    <slot name="extra-fields-after" :state="state" />

    <slot name="before-submit" :state="state" />

    <n-button
      type="primary"
      block
      :loading="loading"
      class="t-login-form__submit"
      @click="handleSubmit"
    >
      <slot name="submit-label">{{ submitLabel }}</slot>
    </n-button>

    <slot name="social-providers" :on-click="handleSocial" />

    <slot name="footer-links" :on-forgot="() => emit('forgot-password')" :on-register="() => emit('switch-mode', 'register')" />
  </n-form>
</template>

<script setup lang="ts">
import { NForm, NFormItem, NInput, NButton, NCheckbox } from 'naive-ui'
import { useLoginForm } from '../../composables/auth/useLoginForm'
import type { LoginProvider, LoginCredentials, SocialProvider } from '../../composables/auth/types'

interface Props {
  /** Injectable authentication provider. Required. */
  provider: LoginProvider
  /** Initial field values. */
  initialValues?: Partial<LoginCredentials>
  /** Label overrides. */
  usernameLabel?: string
  passwordLabel?: string
  rememberMeLabel?: string
  forgotPasswordLabel?: string
  submitLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  usernameLabel: 'Username',
  passwordLabel: 'Password',
  rememberMeLabel: 'Remember me',
  forgotPasswordLabel: 'Forgot password?',
  submitLabel: 'Sign in',
})

const emit = defineEmits<{
  submit: [credentials: LoginCredentials]
  success: [user: unknown]
  error: [err: Error]
  'field-change': [field: string, value: unknown]
  'social-click': [provider: SocialProvider]
  'forgot-password': []
  'switch-mode': [mode: 'register' | 'reset']
  'before-submit': [state: LoginCredentials]
}>()

const { state, loading, handleSubmit: composableSubmit, handleSocial: composableSocial } = useLoginForm({
  provider: props.provider,
  initialValues: props.initialValues,
  onSubmit: (credentials) => emit('submit', credentials),
  onSuccess: (user) => emit('success', user),
  onError: (err) => emit('error', err),
})

async function handleSubmit(): Promise<void> {
  emit('before-submit', { ...state.value })
  await composableSubmit()
}

async function handleSocial(provider: SocialProvider): Promise<void> {
  emit('social-click', provider)
  await composableSocial(provider)
}

defineExpose({ state, loading, handleSubmit, handleSocial })
</script>

<style scoped>
.t-login-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-login-form__row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 14px;
}
.t-login-form__forgot {
  color: var(--tnzi-primary-500);
  cursor: pointer;
}
.t-login-form__forgot:hover {
  text-decoration: underline;
}
.t-login-form__submit {
  margin-top: 8px;
}
</style>
