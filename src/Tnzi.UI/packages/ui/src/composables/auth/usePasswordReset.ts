import { ref, onUnmounted, type Ref } from 'vue'
import type { PasswordResetState, PasswordResetProvider } from './types'

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export interface UsePasswordResetOptions {
  provider: PasswordResetProvider
  countdownSeconds?: number
  onSuccess?: () => void
  onError?: (error: Error) => void
}

export function usePasswordReset(options: UsePasswordResetOptions) {
  const countdownSeconds = options.countdownSeconds ?? 60

  const state: Ref<PasswordResetState> = ref({
    email: '',
    captcha: '',
    newPassword: '',
    confirmPassword: '',
    step: 'request',
    errors: {},
    countdown: 0,
  })

  const loading = ref(false)
  let timer: ReturnType<typeof setInterval> | null = null

  function clearTimer() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  function startCountdown() {
    clearTimer()
    state.value.countdown = countdownSeconds
    timer = setInterval(() => {
      if (state.value.countdown <= 1) {
        state.value.countdown = 0
        clearTimer()
      } else {
        state.value.countdown -= 1
      }
    }, 1000)
  }

  function clearErrors() {
    state.value.errors = {}
  }

  function validateRequest(): boolean {
    clearErrors()
    const errors: PasswordResetState['errors'] = {}
    if (!state.value.email.trim()) {
      errors.email = 'Email is required'
    } else if (!EMAIL_RE.test(state.value.email.trim())) {
      errors.email = 'Email is invalid'
    }
    state.value.errors = errors
    return Object.keys(errors).length === 0
  }

  function validateVerify(): boolean {
    clearErrors()
    const errors: PasswordResetState['errors'] = {}
    if (!state.value.captcha.trim()) {
      errors.captcha = 'Verification code is required'
    }
    if (!state.value.newPassword) {
      errors.newPassword = 'Password is required'
    } else if (state.value.newPassword.length < 8) {
      errors.newPassword = 'Password must be at least 8 characters'
    }
    if (!state.value.confirmPassword) {
      errors.confirmPassword = 'Please confirm your password'
    } else if (state.value.confirmPassword !== state.value.newPassword) {
      errors.confirmPassword = 'Passwords do not match'
    }
    state.value.errors = errors
    return Object.keys(errors).length === 0
  }

  async function sendCode(): Promise<void> {
    if (!validateRequest()) return
    loading.value = true
    try {
      await options.provider.requestReset(state.value.email.trim())
      startCountdown()
      state.value.step = 'verify'
    } catch (err) {
      options.onError?.(err instanceof Error ? err : new Error(String(err)))
    } finally {
      loading.value = false
    }
  }

  async function verifyAndReset(): Promise<void> {
    if (!validateVerify()) return
    loading.value = true
    try {
      await options.provider.resetPassword(
        state.value.email.trim(),
        state.value.captcha.trim(),
        state.value.newPassword,
      )
      state.value.step = 'done'
      clearTimer()
      options.onSuccess?.()
    } catch (err) {
      options.onError?.(err instanceof Error ? err : new Error(String(err)))
    } finally {
      loading.value = false
    }
  }

  function reset() {
    clearTimer()
    state.value = {
      email: '',
      captcha: '',
      newPassword: '',
      confirmPassword: '',
      step: 'request',
      errors: {},
      countdown: 0,
    }
  }

  onUnmounted(() => {
    clearTimer()
  })

  return {
    state,
    loading,
    sendCode,
    verifyAndReset,
    reset,
    clearErrors,
  }
}
