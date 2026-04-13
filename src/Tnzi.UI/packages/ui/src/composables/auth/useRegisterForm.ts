import { ref, type Ref } from 'vue'
import type { RegisterCredentials, RegisterState, RegisterProvider } from './types'

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const PHONE_RE = /^[0-9+\-() ]{6,20}$/

export interface UseRegisterFormOptions {
  provider: RegisterProvider
  initialValues?: Partial<RegisterCredentials>
  requirePhone?: boolean
  onSuccess?: (user: unknown) => void
  onError?: (error: Error) => void
  onSubmit?: (credentials: RegisterCredentials) => void
}

export function useRegisterForm(options: UseRegisterFormOptions) {
  const state: Ref<RegisterState> = ref({
    username: options.initialValues?.username ?? '',
    email: options.initialValues?.email ?? '',
    phone: options.initialValues?.phone ?? '',
    password: options.initialValues?.password ?? '',
    confirmPassword: options.initialValues?.confirmPassword ?? '',
    agreeTerms: options.initialValues?.agreeTerms ?? false,
    captcha: options.initialValues?.captcha ?? '',
    errors: {},
  })

  const loading = ref(false)

  function clearErrors() {
    state.value.errors = {}
  }

  function validate(): boolean {
    clearErrors()
    const errors: RegisterState['errors'] = {}

    if (!state.value.username.trim()) {
      errors.username = 'Username is required'
    } else if (state.value.username.trim().length < 3) {
      errors.username = 'Username must be at least 3 characters'
    }

    if (!state.value.email.trim()) {
      errors.email = 'Email is required'
    } else if (!EMAIL_RE.test(state.value.email.trim())) {
      errors.email = 'Email is invalid'
    }

    if (options.requirePhone) {
      if (!state.value.phone || !state.value.phone.trim()) {
        errors.phone = 'Phone is required'
      } else if (!PHONE_RE.test(state.value.phone.trim())) {
        errors.phone = 'Phone is invalid'
      }
    } else if (state.value.phone && state.value.phone.trim() && !PHONE_RE.test(state.value.phone.trim())) {
      errors.phone = 'Phone is invalid'
    }

    if (!state.value.password) {
      errors.password = 'Password is required'
    } else if (state.value.password.length < 8) {
      errors.password = 'Password must be at least 8 characters'
    }

    if (!state.value.confirmPassword) {
      errors.confirmPassword = 'Please confirm your password'
    } else if (state.value.confirmPassword !== state.value.password) {
      errors.confirmPassword = 'Passwords do not match'
    }

    if (!state.value.agreeTerms) {
      errors.agreeTerms = 'You must agree to the terms'
    }

    state.value.errors = errors
    return Object.keys(errors).length === 0
  }

  async function handleSubmit(): Promise<void> {
    if (!validate()) return
    const credentials: RegisterCredentials = {
      username: state.value.username,
      email: state.value.email,
      phone: state.value.phone,
      password: state.value.password,
      confirmPassword: state.value.confirmPassword,
      agreeTerms: state.value.agreeTerms,
      captcha: state.value.captcha,
    }
    options.onSubmit?.(credentials)
    loading.value = true
    try {
      const result = await options.provider.register(credentials)
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

  async function checkUsername(): Promise<boolean | null> {
    if (!options.provider.checkUsernameAvailable) return null
    if (!state.value.username.trim()) return null
    return await options.provider.checkUsernameAvailable(state.value.username.trim())
  }

  function reset() {
    state.value = {
      username: options.initialValues?.username ?? '',
      email: options.initialValues?.email ?? '',
      phone: options.initialValues?.phone ?? '',
      password: options.initialValues?.password ?? '',
      confirmPassword: options.initialValues?.confirmPassword ?? '',
      agreeTerms: options.initialValues?.agreeTerms ?? false,
      captcha: '',
      errors: {},
    }
  }

  return {
    state,
    loading,
    validate,
    handleSubmit,
    checkUsername,
    reset,
    clearErrors,
  }
}
