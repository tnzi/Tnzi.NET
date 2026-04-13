import { ref, type Ref } from 'vue'
import type { LoginCredentials, LoginState, LoginProvider, SocialProvider } from './types'

export interface UseLoginFormOptions {
  provider: LoginProvider
  initialValues?: Partial<LoginCredentials>
  onSuccess?: (user: unknown) => void
  onError?: (error: Error) => void
  onSubmit?: (credentials: LoginCredentials) => void
}

export function useLoginForm(options: UseLoginFormOptions) {
  const state: Ref<LoginState> = ref({
    username: options.initialValues?.username ?? '',
    password: options.initialValues?.password ?? '',
    rememberMe: options.initialValues?.rememberMe ?? false,
    captcha: options.initialValues?.captcha ?? '',
    errors: {},
  })

  const loading = ref(false)

  function clearErrors() {
    state.value.errors = {}
  }

  function validate(): boolean {
    clearErrors()
    const errors: LoginState['errors'] = {}
    if (!state.value.username.trim()) {
      errors.username = 'Username is required'
    }
    if (!state.value.password) {
      errors.password = 'Password is required'
    } else if (state.value.password.length < 6) {
      errors.password = 'Password must be at least 6 characters'
    }
    state.value.errors = errors
    return Object.keys(errors).length === 0
  }

  async function handleSubmit(): Promise<void> {
    if (!validate()) return
    const credentials: LoginCredentials = {
      username: state.value.username,
      password: state.value.password,
      rememberMe: state.value.rememberMe,
      captcha: state.value.captcha,
    }
    options.onSubmit?.(credentials)
    loading.value = true
    try {
      const result = await options.provider.login(credentials)
      if (result.success) {
        options.onSuccess?.(result.user)
      } else {
        options.onError?.(new Error(result.error))
      }
    } catch (err) {
      options.onError?.(err instanceof Error ? err : new Error(String(err)))
    } finally {
      loading.value = false
    }
  }

  async function handleSocial(provider: SocialProvider): Promise<void> {
    if (!options.provider.loginWithSocial) {
      throw new Error('loginWithSocial is not implemented by provider')
    }
    loading.value = true
    try {
      await options.provider.loginWithSocial(provider)
    } finally {
      loading.value = false
    }
  }

  function reset() {
    state.value = {
      username: options.initialValues?.username ?? '',
      password: options.initialValues?.password ?? '',
      rememberMe: options.initialValues?.rememberMe ?? false,
      captcha: '',
      errors: {},
    }
  }

  return {
    state,
    loading,
    validate,
    handleSubmit,
    handleSocial,
    reset,
    clearErrors,
  }
}
