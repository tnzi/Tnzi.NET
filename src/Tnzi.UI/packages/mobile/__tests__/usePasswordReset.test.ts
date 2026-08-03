import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { usePasswordReset } from '../src/headless/usePasswordReset'

describe('usePasswordReset', () => {
  beforeEach(() => { vi.useFakeTimers() })
  afterEach(() => { vi.useRealTimers() })

  it('initializes with empty fields', () => {
    const { fields, countdown, isSendingCode, isSubmitting } = usePasswordReset()
    expect(fields.email.value).toBe('')
    expect(fields.code.value).toBe('')
    expect(fields.password.value).toBe('')
    expect(fields.confirmPassword.value).toBe('')
    expect(countdown.value).toBe(0)
    expect(isSendingCode.value).toBe(false)
    expect(isSubmitting.value).toBe(false)
  })

  it('canSendCode requires email and zero countdown', () => {
    const { fields, canSendCode } = usePasswordReset()
    expect(canSendCode.value).toBe(false)
    fields.email.value = 'test@test.com'
    expect(canSendCode.value).toBe(true)
  })

  it('sendCode starts countdown', async () => {
    const onSendCode = vi.fn().mockResolvedValue(undefined)
    const { fields, countdown, sendCode } = usePasswordReset({ onSendCode, countdownSeconds: 30 })
    fields.email.value = 'test@test.com'
    await sendCode()
    expect(countdown.value).toBe(30)
    expect(onSendCode).toHaveBeenCalledWith('test@test.com')
  })

  it('sendCode decrements countdown every second', async () => {
    const onSendCode = vi.fn().mockResolvedValue(undefined)
    const { fields, countdown, sendCode } = usePasswordReset({ onSendCode, countdownSeconds: 5 })
    fields.email.value = 'test@test.com'
    await sendCode()
    expect(countdown.value).toBe(5)
    vi.advanceTimersByTime(1000)
    expect(countdown.value).toBe(4)
    vi.advanceTimersByTime(4000)
    expect(countdown.value).toBe(0)
  })

  it('canSendCode is false during countdown', async () => {
    const onSendCode = vi.fn().mockResolvedValue(undefined)
    const { fields, canSendCode, sendCode } = usePasswordReset({ onSendCode, countdownSeconds: 10 })
    fields.email.value = 'test@test.com'
    await sendCode()
    expect(canSendCode.value).toBe(false)
    vi.advanceTimersByTime(10000)
    expect(canSendCode.value).toBe(true)
  })

  it('passwordMismatch detects mismatched passwords', () => {
    const { fields, passwordMismatch } = usePasswordReset()
    fields.password.value = 'abc'
    fields.confirmPassword.value = 'xyz'
    expect(passwordMismatch.value).toBe(true)
    fields.confirmPassword.value = 'abc'
    expect(passwordMismatch.value).toBe(false)
  })

  it('canSubmit requires all fields filled and matching passwords', () => {
    const { fields, canSubmit } = usePasswordReset()
    expect(canSubmit.value).toBe(false)
    fields.email.value = 'test@test.com'
    fields.code.value = '123456'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    expect(canSubmit.value).toBe(true)
    fields.confirmPassword.value = 'wrong'
    expect(canSubmit.value).toBe(false)
  })

  it('submit calls onSubmit with correct data', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    const { fields, submit } = usePasswordReset({ onSubmit })
    fields.email.value = 'test@test.com'
    fields.code.value = '123456'
    fields.password.value = 'newpass'
    fields.confirmPassword.value = 'newpass'
    await submit()
    expect(onSubmit).toHaveBeenCalledWith({
      email: 'test@test.com',
      code: '123456',
      password: 'newpass',
    })
  })

  it('submit validates and sets errors for missing fields', async () => {
    const onSubmit = vi.fn()
    const { errors, submit } = usePasswordReset({ onSubmit })
    await submit()
    expect(onSubmit).not.toHaveBeenCalled()
    expect(errors.value.email).toBeTruthy()
    expect(errors.value.code).toBeTruthy()
    expect(errors.value.password).toBeTruthy()
  })

  it('submit captures onSubmit rejection', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error('Code expired'))
    const { fields, errors, submit } = usePasswordReset({ onSubmit })
    fields.email.value = 'test@test.com'
    fields.code.value = '123456'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    await submit()
    expect(errors.value._form).toBe('Code expired')
  })

  it('reset clears fields and stops countdown', async () => {
    const onSendCode = vi.fn().mockResolvedValue(undefined)
    const { fields, countdown, reset, sendCode } = usePasswordReset({ onSendCode, countdownSeconds: 30 })
    fields.email.value = 'test@test.com'
    await sendCode()
    expect(countdown.value).toBe(30)
    reset()
    expect(fields.email.value).toBe('')
    expect(countdown.value).toBe(0)
    // Advance time: timer should be cleared, no further updates
    vi.advanceTimersByTime(5000)
    expect(countdown.value).toBe(0)
  })

  it('cancel calls onCancel', () => {
    const onCancel = vi.fn()
    const { cancel } = usePasswordReset({ onCancel })
    cancel()
    expect(onCancel).toHaveBeenCalledOnce()
  })

  it('dispose clears countdown timer', async () => {
    const onSendCode = vi.fn().mockResolvedValue(undefined)
    const { fields, countdown, sendCode, dispose } = usePasswordReset({ onSendCode, countdownSeconds: 30 })
    fields.email.value = 'test@test.com'
    await sendCode()
    dispose()
    vi.advanceTimersByTime(10000)
    expect(countdown.value).toBe(30)
  })
})
