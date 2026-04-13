import { describe, it, expect, vi } from 'vitest'
import { useRegisterForm } from '../headless/useRegisterForm'

describe('useRegisterForm', () => {
  it('initializes with empty fields', () => {
    const { fields, errors, isSubmitting } = useRegisterForm()
    expect(fields.email.value).toBe('')
    expect(fields.userName.value).toBe('')
    expect(fields.phoneNumber.value).toBe('')
    expect(fields.password.value).toBe('')
    expect(fields.confirmPassword.value).toBe('')
    expect(errors.value).toEqual({})
    expect(isSubmitting.value).toBe(false)
  })

  it('passwordMismatch is false when confirmPassword is empty', () => {
    const { passwordMismatch } = useRegisterForm()
    expect(passwordMismatch.value).toBe(false)
  })

  it('passwordMismatch is true when passwords differ', () => {
    const { fields, passwordMismatch } = useRegisterForm()
    fields.password.value = 'abc'
    fields.confirmPassword.value = 'xyz'
    expect(passwordMismatch.value).toBe(true)
  })

  it('passwordMismatch is false when passwords match', () => {
    const { fields, passwordMismatch } = useRegisterForm()
    fields.password.value = 'abc'
    fields.confirmPassword.value = 'abc'
    expect(passwordMismatch.value).toBe(false)
  })

  it('canSubmit requires email and password with no mismatch', () => {
    const { fields, canSubmit } = useRegisterForm()
    expect(canSubmit.value).toBe(false)
    fields.email.value = 'test@test.com'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    expect(canSubmit.value).toBe(true)
    fields.confirmPassword.value = 'wrong'
    expect(canSubmit.value).toBe(false)
  })

  it('validate sets errors for missing required fields', () => {
    const { validate, errors } = useRegisterForm()
    const result = validate()
    expect(result).toBe(false)
    expect(errors.value.email).toBeTruthy()
    expect(errors.value.password).toBeTruthy()
  })

  it('validate requires username when showUsername is true', () => {
    const { fields, validate, errors } = useRegisterForm({ showUsername: true })
    fields.email.value = 'test@test.com'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    const result = validate()
    expect(result).toBe(false)
    expect(errors.value.userName).toBeTruthy()
  })

  it('validate requires phone when showPhone is true', () => {
    const { fields, validate, errors } = useRegisterForm({ showPhone: true })
    fields.email.value = 'test@test.com'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    const result = validate()
    expect(result).toBe(false)
    expect(errors.value.phoneNumber).toBeTruthy()
  })

  it('submit calls onSubmit with correct data', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    const { fields, submit } = useRegisterForm({ onSubmit, showUsername: true, showPhone: true })
    fields.email.value = 'alice@test.com'
    fields.userName.value = 'alice'
    fields.phoneNumber.value = '13800000000'
    fields.password.value = 'pass123'
    fields.confirmPassword.value = 'pass123'
    await submit()
    expect(onSubmit).toHaveBeenCalledWith({
      email: 'alice@test.com',
      userName: 'alice',
      phoneNumber: '13800000000',
      password: 'pass123',
      confirmPassword: 'pass123',
    })
  })

  it('submit does not call onSubmit if validation fails', async () => {
    const onSubmit = vi.fn()
    const { submit } = useRegisterForm({ onSubmit })
    await submit()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submit captures error from onSubmit rejection', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error('Email taken'))
    const { fields, errors, submit } = useRegisterForm({ onSubmit })
    fields.email.value = 'test@test.com'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    await submit()
    expect(errors.value._form).toBe('Email taken')
  })

  it('reset clears all fields', async () => {
    const { fields, reset } = useRegisterForm()
    fields.email.value = 'test@test.com'
    fields.password.value = 'pass'
    fields.confirmPassword.value = 'pass'
    reset()
    expect(fields.email.value).toBe('')
    expect(fields.password.value).toBe('')
    expect(fields.confirmPassword.value).toBe('')
  })

  it('login calls onLogin', () => {
    const onLogin = vi.fn()
    const { login } = useRegisterForm({ onLogin })
    login()
    expect(onLogin).toHaveBeenCalledOnce()
  })
})
