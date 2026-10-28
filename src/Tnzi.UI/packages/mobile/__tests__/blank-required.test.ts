import { describe, it, expect, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import Vant from 'vant'
import { isBlank } from '../src/headless/blank'
import { useLoginForm } from '../src/headless/useLoginForm'
import { useRegisterForm } from '../src/headless/useRegisterForm'
import { usePasswordReset } from '../src/headless/usePasswordReset'
import TLoginForm from '../src/components/auth/TLoginForm.vue'
import TRegisterForm from '../src/components/auth/TRegisterForm.vue'
import TPasswordReset from '../src/components/auth/TPasswordReset.vue'
import TForm from '../src/components/form/TForm.vue'

/**
 * A whitespace-only value is as empty as a blank one. The backend's [Required]
 * refuses it, so a form that counts it as filled only trades the field message
 * for a failed request. Values with real text in them are submitted untrimmed.
 */

const BLANK = ' \t '
const global = { plugins: [Vant] }

describe('isBlank', () => {
  it.each(['', ' ', '\t\n ', null, undefined])('counts %j as blank', (value) => {
    expect(isBlank(value)).toBe(true)
  })

  it.each(['a', ' a ', '0'])('counts %j as filled', (value) => {
    expect(isBlank(value)).toBe(false)
  })
})

describe('auth composables: whitespace-only fields are empty', () => {
  it('useLoginForm', async () => {
    const onSubmit = vi.fn(async () => undefined)
    const { fields, isValid, validate, errors, submit } = useLoginForm({ showCaptcha: true, onSubmit })
    fields.username.value = BLANK
    fields.password.value = BLANK
    fields.captchaCode.value = BLANK
    expect(isValid.value).toBe(false)
    expect(validate()).toBe(false)
    expect(Object.keys(errors.value).sort()).toEqual(['captchaCode', 'password', 'username'])
    await submit()
    expect(onSubmit).not.toHaveBeenCalled()

    fields.username.value = ' alice '
    fields.password.value = ' secret '
    fields.captchaCode.value = '1234'
    expect(isValid.value).toBe(true)
    await submit()
    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ userName: ' alice ', password: ' secret ' }))
  })

  it('useRegisterForm', () => {
    const { fields, canSubmit, validate, errors } = useRegisterForm({ showUsername: true, showPhone: true })
    fields.email.value = BLANK
    fields.userName.value = BLANK
    fields.phoneNumber.value = BLANK
    fields.password.value = BLANK
    fields.confirmPassword.value = BLANK
    expect(canSubmit.value).toBe(false)
    expect(validate()).toBe(false)
    expect(Object.keys(errors.value).sort()).toEqual(['confirmPassword', 'email', 'password', 'phoneNumber', 'userName'])

    fields.email.value = 'a@b.co'
    fields.userName.value = 'alice'
    fields.phoneNumber.value = '5551234'
    fields.password.value = 'secret1'
    fields.confirmPassword.value = 'secret1'
    expect(canSubmit.value).toBe(true)
    expect(validate()).toBe(true)
  })

  it('usePasswordReset', async () => {
    const onSubmit = vi.fn(async () => undefined)
    const { fields, canSendCode, canSubmit, submit, errors } = usePasswordReset({ onSubmit })
    fields.email.value = BLANK
    fields.code.value = BLANK
    fields.password.value = BLANK
    fields.confirmPassword.value = BLANK
    expect(canSendCode.value).toBe(false)
    expect(canSubmit.value).toBe(false)
    await submit()
    expect(onSubmit).not.toHaveBeenCalled()
    expect(Object.keys(errors.value).sort()).toEqual(['code', 'confirmPassword', 'email', 'password'])

    fields.email.value = 'a@b.co'
    expect(canSendCode.value).toBe(true)
  })
})

describe('auth forms: a whitespace-only field shows its required message', () => {
  it('TLoginForm', async () => {
    const wrapper = mount(TLoginForm, { global })
    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue(BLANK)
    await inputs[1]!.setValue(BLANK)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
    const messages = wrapper.findAll('.van-field__error-message').map((el) => el.text())
    expect(messages).toEqual(expect.arrayContaining(['Please enter Username', 'Please enter Password']))
  })

  it('TRegisterForm', async () => {
    const wrapper = mount(TRegisterForm, { props: { showUsername: false }, global })
    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue(BLANK)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const messages = wrapper.findAll('.van-field__error-message').map((el) => el.text())
    expect(messages).toContain('Please enter Email')
  })

  it('TPasswordReset', async () => {
    const wrapper = mount(TPasswordReset, { global })
    const inputs = wrapper.findAll('input')
    for (const input of inputs.slice(0, 3)) await input.setValue(BLANK)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
    const messages = wrapper.findAll('.van-field__error-message').map((el) => el.text())
    expect(messages).toEqual(
      expect.arrayContaining(['Please enter Email', 'Please enter Verification Code', 'Please enter New Password']),
    )
  })
})

describe('TForm: a required rule counts a whitespace-only value as missing', () => {
  it('refuses a whitespace-only value with the required message and submits a real one untrimmed', async () => {
    const model = { name: BLANK, note: BLANK, count: 0 }
    const wrapper = mount(TForm, {
      props: {
        model,
        rules: { name: [{ required: true, message: 'Name is required' }], note: [{ max: 10 }], count: [{ required: true }] },
      },
      global,
    })

    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')).toBeUndefined()
    // An optional field may hold spaces; 0 is a value, not a missing one.
    expect(wrapper.emitted('validateError')![0]).toEqual([{ name: ['Name is required'] }])

    model.name = ' Ada '
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(wrapper.emitted('submit')).toEqual([[expect.objectContaining({ name: ' Ada ' })]])
  })
})
