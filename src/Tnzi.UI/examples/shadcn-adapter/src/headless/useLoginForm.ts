import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface LoginCredentials {
  userName: string
  password: string
  rememberMe: boolean
  captchaId?: string
  captchaCode: string
}

export interface UseLoginFormOptions {
  showRememberMe?: boolean
  showCaptcha?: boolean
  showSocialLogin?: boolean
  socialProviders?: string[]
  captchaId?: string
  onSubmit?: (credentials: LoginCredentials) => Promise<void>
  onForgotPassword?: () => void
  onSocialLogin?: (provider: string) => void
  onRefreshCaptcha?: () => void
}

export interface UseLoginFormReturn {
  fields: {
    username: Ref<string>
    password: Ref<string>
    rememberMe: Ref<boolean>
    captchaCode: Ref<string>
  }
  errors: Ref<Record<string, string>>
  isSubmitting: Ref<boolean>
  isValid: ComputedRef<boolean>
  canSubmit: ComputedRef<boolean>
  submit: () => Promise<void>
  reset: () => void
  validate: () => boolean
  forgotPassword: () => void
  socialLogin: (provider: string) => void
  refreshCaptcha: () => void
}

export function useLoginForm(options: UseLoginFormOptions = {}): UseLoginFormReturn {
  const username = ref('')
  const password = ref('')
  const rememberMe = ref(false)
  const captchaCode = ref('')
  const errors = ref<Record<string, string>>({})
  const isSubmitting = ref(false)

  const isValid = computed(() => {
    if (!username.value || !password.value) return false
    if (options.showCaptcha && !captchaCode.value) return false
    return true
  })

  const canSubmit = computed(() => isValid.value && !isSubmitting.value)

  function validate(): boolean {
    const newErrors: Record<string, string> = {}
    if (!username.value) newErrors.username = 'Username is required'
    if (!password.value) newErrors.password = 'Password is required'
    if (options.showCaptcha && !captchaCode.value) {
      newErrors.captchaCode = 'Captcha is required'
    }
    errors.value = newErrors
    return Object.keys(newErrors).length === 0
  }

  async function submit(): Promise<void> {
    if (!validate()) return
    if (!options.onSubmit) return
    isSubmitting.value = true
    errors.value = {}
    try {
      await options.onSubmit({
        userName: username.value,
        password: password.value,
        rememberMe: rememberMe.value,
        captchaId: options.captchaId,
        captchaCode: captchaCode.value,
      })
    } catch (err) {
      errors.value = { _form: err instanceof Error ? err.message : String(err) }
    } finally {
      isSubmitting.value = false
    }
  }

  function reset(): void {
    username.value = ''
    password.value = ''
    rememberMe.value = false
    captchaCode.value = ''
    errors.value = {}
  }

  function forgotPassword(): void { options.onForgotPassword?.() }
  function socialLogin(provider: string): void { options.onSocialLogin?.(provider) }
  function refreshCaptcha(): void { options.onRefreshCaptcha?.() }

  return {
    fields: { username, password, rememberMe, captchaCode },
    errors,
    isSubmitting,
    isValid,
    canSubmit,
    submit,
    reset,
    validate,
    forgotPassword,
    socialLogin,
    refreshCaptcha,
  }
}
