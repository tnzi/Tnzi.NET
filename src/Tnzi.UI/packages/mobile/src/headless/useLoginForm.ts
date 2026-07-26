import { ref, computed, type Ref, type ComputedRef } from 'vue'
import { useI18n } from '@tnzi/core/adapters/i18n'

export interface LoginCredentials {
  userName: string
  password: string
  rememberMe: boolean
  captchaId?: string
  captchaCode: string
}

export interface UseLoginFormOptions {
  showCaptcha?: boolean
  captchaId?: string
  onSubmit?: (credentials: LoginCredentials) => Promise<void>
  onForgotPassword?: () => void
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
  refreshCaptcha: () => void
}

export function useLoginForm(options: UseLoginFormOptions = {}): UseLoginFormReturn {
  const { t } = useI18n()

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
    if (!username.value) newErrors.username = t('auth.pleaseEnter', { field: t('auth.username') })
    if (!password.value) newErrors.password = t('auth.pleaseEnter', { field: t('auth.password') })
    if (options.showCaptcha && !captchaCode.value) {
      newErrors.captchaCode = t('auth.pleaseEnter', { field: t('auth.verificationCode') })
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
    refreshCaptcha,
  }
}
