import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface RegisterData {
  email: string
  password: string
  userName?: string
  phoneNumber?: string
  captchaId?: string
  captchaCode?: string
}

export interface UseRegisterFormOptions {
  showUsername?: boolean
  showPhone?: boolean
  showCaptcha?: boolean
  captchaId?: string
  onSubmit?: (data: RegisterData) => void | Promise<void>
  onLogin?: () => void
  onSocialLogin?: (provider: string) => void
  onRefreshCaptcha?: () => void
}

export interface UseRegisterFormReturn {
  fields: {
    email: Ref<string>
    userName: Ref<string>
    phone: Ref<string>
    password: Ref<string>
    confirmPassword: Ref<string>
    captchaCode: Ref<string>
  }
  errors: Ref<Record<string, string>>
  isSubmitting: Ref<boolean>
  passwordMismatch: ComputedRef<boolean>
  canSubmit: ComputedRef<boolean>
  submit: () => Promise<void>
  login: () => void
  socialLogin: (provider: string) => void
  refreshCaptcha: () => void
}

export function useRegisterForm(options: UseRegisterFormOptions = {}): UseRegisterFormReturn {
  const email = ref('')
  const userName = ref('')
  const phone = ref('')
  const password = ref('')
  const confirmPassword = ref('')
  const captchaCode = ref('')
  const errors = ref<Record<string, string>>({})
  const isSubmitting = ref(false)

  const passwordMismatch = computed(
    () => confirmPassword.value !== '' && confirmPassword.value !== password.value,
  )

  const canSubmit = computed(
    () => !passwordMismatch.value && email.value !== '' && password.value !== '' && !isSubmitting.value,
  )

  async function submit(): Promise<void> {
    if (passwordMismatch.value) return
    if (!options.onSubmit) return
    isSubmitting.value = true
    errors.value = {}
    try {
      await options.onSubmit({
        email: email.value,
        password: password.value,
        userName: options.showUsername ? userName.value : undefined,
        phoneNumber: options.showPhone ? phone.value : undefined,
        captchaId: options.showCaptcha ? options.captchaId : undefined,
        captchaCode: options.showCaptcha ? captchaCode.value : undefined,
      })
    } catch (err) {
      errors.value = { _form: err instanceof Error ? err.message : String(err) }
    } finally {
      isSubmitting.value = false
    }
  }

  function login(): void { options.onLogin?.() }
  function socialLogin(provider: string): void { options.onSocialLogin?.(provider) }
  function refreshCaptcha(): void { options.onRefreshCaptcha?.() }

  return {
    fields: { email, userName, phone, password, confirmPassword, captchaCode },
    errors,
    isSubmitting,
    passwordMismatch,
    canSubmit,
    submit,
    login,
    socialLogin,
    refreshCaptcha,
  }
}
