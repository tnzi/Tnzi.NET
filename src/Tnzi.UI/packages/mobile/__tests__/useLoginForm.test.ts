import { describe, it, expect, vi } from 'vitest'
import { useLoginForm } from '../src/headless/useLoginForm'

describe('useLoginForm', () => {
  it('initializes with empty fields', () => {
    const { fields, errors, isSubmitting } = useLoginForm()
    expect(fields.username.value).toBe('')
    expect(fields.password.value).toBe('')
    expect(fields.rememberMe.value).toBe(false)
    expect(fields.captchaCode.value).toBe('')
    expect(errors.value).toEqual({})
    expect(isSubmitting.value).toBe(false)
  })

  it('isValid is false when fields are empty', () => {
    const { isValid } = useLoginForm()
    expect(isValid.value).toBe(false)
  })

  it('isValid is true when required fields are filled', () => {
    const { fields, isValid } = useLoginForm()
    fields.username.value = 'user'
    fields.password.value = 'pass'
    expect(isValid.value).toBe(true)
  })

  it('isValid respects captcha requirement', () => {
    const { fields, isValid } = useLoginForm({ showCaptcha: true })
    fields.username.value = 'user'
    fields.password.value = 'pass'
    expect(isValid.value).toBe(false)
    fields.captchaCode.value = '1234'
    expect(isValid.value).toBe(true)
  })

  it('canSubmit is false when submitting', async () => {
    let resolveSubmit!: () => void
    const blockingSubmit = vi.fn(() => new Promise<void>(r => { resolveSubmit = r }))
    const { fields, canSubmit, submit } = useLoginForm({ onSubmit: blockingSubmit })
    fields.username.value = 'user'
    fields.password.value = 'pass'
    expect(canSubmit.value).toBe(true)
    const p = submit()
    expect(canSubmit.value).toBe(false)
    resolveSubmit()
    await p
    expect(canSubmit.value).toBe(true)
  })

  it('validate sets field errors', () => {
    const { validate, errors } = useLoginForm()
    const result = validate()
    expect(result).toBe(false)
    expect(errors.value.username).toBeTruthy()
    expect(errors.value.password).toBeTruthy()
  })

  it('validate returns true when valid', () => {
    const { fields, validate, errors } = useLoginForm()
    fields.username.value = 'user'
    fields.password.value = 'pass'
    const result = validate()
    expect(result).toBe(true)
    expect(errors.value).toEqual({})
  })

  it('submit calls onSubmit with correct data', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined)
    const { fields, submit } = useLoginForm({ onSubmit, captchaId: 'cid' })
    fields.username.value = 'alice'
    fields.password.value = 'secret'
    fields.rememberMe.value = true
    fields.captchaCode.value = 'abc'
    await submit()
    expect(onSubmit).toHaveBeenCalledWith({
      userName: 'alice',
      password: 'secret',
      rememberMe: true,
      captchaId: 'cid',
      captchaCode: 'abc',
    })
  })

  it('submit does not call onSubmit if invalid', async () => {
    const onSubmit = vi.fn()
    const { submit } = useLoginForm({ onSubmit })
    await submit()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submit captures error on onSubmit rejection', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error('Bad credentials'))
    const { fields, errors, submit } = useLoginForm({ onSubmit })
    fields.username.value = 'user'
    fields.password.value = 'pass'
    await submit()
    expect(errors.value._form).toBe('Bad credentials')
  })

  it('reset clears all fields and errors', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error('err'))
    const { fields, errors, reset, submit } = useLoginForm({ onSubmit })
    fields.username.value = 'user'
    fields.password.value = 'pass'
    await submit()
    reset()
    expect(fields.username.value).toBe('')
    expect(fields.password.value).toBe('')
    expect(fields.rememberMe.value).toBe(false)
    expect(errors.value).toEqual({})
  })

  it('forgotPassword calls onForgotPassword', () => {
    const onForgotPassword = vi.fn()
    const { forgotPassword } = useLoginForm({ onForgotPassword })
    forgotPassword()
    expect(onForgotPassword).toHaveBeenCalledOnce()
  })

  it('refreshCaptcha calls onRefreshCaptcha', () => {
    const onRefreshCaptcha = vi.fn()
    const { refreshCaptcha } = useLoginForm({ onRefreshCaptcha })
    refreshCaptcha()
    expect(onRefreshCaptcha).toHaveBeenCalledOnce()
  })
})
