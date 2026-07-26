import { ref, computed, type Ref, type ComputedRef } from 'vue'
import { useI18n } from '@tnzi/core/adapters/i18n'

export interface PasswordResetData {
  email: string
  code: string
  password: string
}

export interface UsePasswordResetOptions {
  countdownSeconds?: number
  onSubmit?: (data: PasswordResetData) => void | Promise<void>
  onCancel?: () => void
  onSendCode?: (email: string) => void | Promise<void>
}

export interface UsePasswordResetReturn {
  fields: {
    email: Ref<string>
    code: Ref<string>
    password: Ref<string>
    confirmPassword: Ref<string>
  }
  errors: Ref<Record<string, string>>
  countdown: Ref<number>
  isSendingCode: Ref<boolean>
  isSubmitting: Ref<boolean>
  passwordMismatch: ComputedRef<boolean>
  canSendCode: ComputedRef<boolean>
  canSubmit: ComputedRef<boolean>
  sendCode: () => Promise<void>
  submit: () => Promise<void>
  reset: () => void
  cancel: () => void
  dispose: () => void
}

export function usePasswordReset(options: UsePasswordResetOptions = {}): UsePasswordResetReturn {
  const { t } = useI18n()

  const email = ref('')
  const code = ref('')
  const password = ref('')
  const confirmPassword = ref('')
  const errors = ref<Record<string, string>>({})
  const countdown = ref(0)
  const isSendingCode = ref(false)
  const isSubmitting = ref(false)

  const passwordMismatch = computed(
    () => confirmPassword.value !== '' && confirmPassword.value !== password.value,
  )

  const canSendCode = computed(() => email.value !== '' && countdown.value === 0 && !isSendingCode.value)

  const canSubmit = computed(
    () =>
      email.value !== '' &&
      code.value !== '' &&
      password.value !== '' &&
      !passwordMismatch.value &&
      !isSubmitting.value,
  )

  let timer: ReturnType<typeof setInterval> | null = null

  function startCountdown(): void {
    // Read lazily so callers can drive the duration from a reactive source.
    countdown.value = options.countdownSeconds ?? 60
    timer = setInterval(() => {
      countdown.value -= 1
      if (countdown.value <= 0 && timer) {
        clearInterval(timer)
        timer = null
      }
    }, 1000)
  }

  async function sendCode(): Promise<void> {
    if (!canSendCode.value) return
    isSendingCode.value = true
    try {
      await options.onSendCode?.(email.value)
      startCountdown()
    } finally {
      isSendingCode.value = false
    }
  }

  async function submit(): Promise<void> {
    const newErrors: Record<string, string> = {}
    if (!email.value) newErrors.email = t('auth.pleaseEnter', { field: t('auth.email') })
    if (!code.value) newErrors.code = t('auth.pleaseEnter', { field: t('auth.verificationCode') })
    if (!password.value) newErrors.password = t('auth.pleaseEnter', { field: t('auth.newPassword') })
    if (passwordMismatch.value) newErrors.confirmPassword = t('auth.passwordMismatch')
    errors.value = newErrors
    if (Object.keys(newErrors).length > 0) return

    if (!options.onSubmit) return
    isSubmitting.value = true
    try {
      await options.onSubmit({
        email: email.value,
        code: code.value,
        password: password.value,
      })
    } catch (err) {
      errors.value = { _form: err instanceof Error ? err.message : String(err) }
    } finally {
      isSubmitting.value = false
    }
  }

  function reset(): void {
    email.value = ''
    code.value = ''
    password.value = ''
    confirmPassword.value = ''
    errors.value = {}
    countdown.value = 0
    dispose()
  }

  function cancel(): void { options.onCancel?.() }

  function dispose(): void {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  return {
    fields: { email, code, password, confirmPassword },
    errors,
    countdown,
    isSendingCode,
    isSubmitting,
    passwordMismatch,
    canSendCode,
    canSubmit,
    sendCode,
    submit,
    reset,
    cancel,
    dispose,
  }
}
