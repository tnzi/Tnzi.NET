import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface PasswordResetData {
  email: string
  code: string
  password: string
}

export interface UsePasswordResetOptions {
  countdownSeconds?: number
  onSubmit?: (data: PasswordResetData) => void | Promise<void>
  onCancel?: () => void
  onSendCode?: (email: string) => void
}

export interface UsePasswordResetReturn {
  fields: {
    email: Ref<string>
    code: Ref<string>
    password: Ref<string>
    confirmPassword: Ref<string>
  }
  countdown: Ref<number>
  passwordMismatch: ComputedRef<boolean>
  canSendCode: ComputedRef<boolean>
  sendCode: () => void
  submit: () => void
  cancel: () => void
  dispose: () => void
}

export function usePasswordReset(options: UsePasswordResetOptions = {}): UsePasswordResetReturn {
  const countdownSeconds = options.countdownSeconds ?? 60

  const email = ref('')
  const code = ref('')
  const password = ref('')
  const confirmPassword = ref('')
  const countdown = ref(0)

  const passwordMismatch = computed(
    () => confirmPassword.value !== '' && confirmPassword.value !== password.value,
  )

  const canSendCode = computed(() => email.value !== '' && countdown.value === 0)

  let timer: ReturnType<typeof setInterval> | null = null

  function startCountdown(): void {
    countdown.value = countdownSeconds
    timer = setInterval(() => {
      countdown.value -= 1
      if (countdown.value <= 0 && timer) {
        clearInterval(timer)
        timer = null
      }
    }, 1000)
  }

  function sendCode(): void {
    if (!canSendCode.value) return
    options.onSendCode?.(email.value)
    startCountdown()
  }

  function submit(): void {
    if (passwordMismatch.value) return
    options.onSubmit?.({
      email: email.value,
      code: code.value,
      password: password.value,
    })
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
    countdown,
    passwordMismatch,
    canSendCode,
    sendCode,
    submit,
    cancel,
    dispose,
  }
}
