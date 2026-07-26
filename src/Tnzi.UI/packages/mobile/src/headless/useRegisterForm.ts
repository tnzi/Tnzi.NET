import { ref, computed, type Ref, type ComputedRef } from 'vue'
import { useI18n } from '@tnzi/core/adapters/i18n'

export interface RegisterData {
  email: string
  userName?: string
  phoneNumber?: string
  password: string
  confirmPassword?: string
}

export interface UseRegisterFormOptions {
  showUsername?: boolean
  showPhone?: boolean
  onSubmit?: (data: RegisterData) => void | Promise<void>
  onLogin?: () => void
}

export interface UseRegisterFormReturn {
  fields: {
    email: Ref<string>
    userName: Ref<string>
    phoneNumber: Ref<string>
    password: Ref<string>
    confirmPassword: Ref<string>
  }
  errors: Ref<Record<string, string>>
  passwordMismatch: ComputedRef<boolean>
  canSubmit: ComputedRef<boolean>
  isSubmitting: Ref<boolean>
  submit: () => Promise<void>
  reset: () => void
  validate: () => boolean
  login: () => void
}

export function useRegisterForm(options: UseRegisterFormOptions = {}): UseRegisterFormReturn {
  const { t } = useI18n()

  const email = ref('')
  const userName = ref('')
  const phoneNumber = ref('')
  const password = ref('')
  const confirmPassword = ref('')
  const errors = ref<Record<string, string>>({})
  const isSubmitting = ref(false)

  const passwordMismatch = computed(
    () => confirmPassword.value !== '' && confirmPassword.value !== password.value,
  )

  const canSubmit = computed(
    () =>
      !passwordMismatch.value &&
      email.value !== '' &&
      password.value !== '' &&
      !isSubmitting.value,
  )

  function validate(): boolean {
    const newErrors: Record<string, string> = {}
    if (!email.value) newErrors.email = t('auth.pleaseEnter', { field: t('auth.email') })
    if (!password.value) newErrors.password = t('auth.pleaseEnter', { field: t('auth.password') })
    if (passwordMismatch.value) newErrors.confirmPassword = t('auth.passwordMismatch')
    if (options.showUsername && !userName.value) {
      newErrors.userName = t('auth.pleaseEnter', { field: t('auth.username') })
    }
    if (options.showPhone && !phoneNumber.value) {
      newErrors.phoneNumber = t('auth.pleaseEnter', { field: t('auth.phone') })
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
        email: email.value,
        password: password.value,
        confirmPassword: confirmPassword.value,
        userName: options.showUsername ? userName.value : undefined,
        phoneNumber: options.showPhone ? phoneNumber.value : undefined,
      })
    } catch (err) {
      errors.value = { _form: err instanceof Error ? err.message : String(err) }
    } finally {
      isSubmitting.value = false
    }
  }

  function reset(): void {
    email.value = ''
    userName.value = ''
    phoneNumber.value = ''
    password.value = ''
    confirmPassword.value = ''
    errors.value = {}
  }

  function login(): void { options.onLogin?.() }

  return {
    fields: { email, userName, phoneNumber, password, confirmPassword },
    errors,
    passwordMismatch,
    canSubmit,
    isSubmitting,
    submit,
    reset,
    validate,
    login,
  }
}
