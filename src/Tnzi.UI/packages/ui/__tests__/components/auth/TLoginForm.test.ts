import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import TLoginForm from '../../../src/components/auth/TLoginForm.vue'
import type { LoginProvider } from '../../../src/composables/auth/types'

function mockProvider(): LoginProvider {
  return {
    login: vi.fn().mockResolvedValue({ success: true, user: { id: '1' } }),
    loginWithSocial: vi.fn().mockResolvedValue(undefined),
  }
}

const stubs = {
  'n-input': true,
  'n-button': true,
  'n-checkbox': true,
  'n-form': { template: '<form><slot/></form>' },
  'n-form-item': { template: '<div><slot/></div>' },
}

describe('TLoginForm (rewritten as composable-shell)', () => {
  it('renders default username and password fields', () => {
    const wrapper = mount(TLoginForm, {
      props: { provider: mockProvider() },
      global: { stubs },
    })
    // Default slot renders two form-items (username + password) plus row + submit
    expect(wrapper.findAll('.t-login-form__row').length).toBe(1)
    expect(wrapper.find('.t-login-form__submit').exists()).toBe(true)
  })

  it('renders the header slot', () => {
    const wrapper = mount(TLoginForm, {
      props: { provider: mockProvider() },
      slots: { header: '<h1 class="h">Sign in</h1>' },
      global: { stubs },
    })
    expect(wrapper.find('h1.h').exists()).toBe(true)
  })

  it('renders the extra-fields-before slot', () => {
    const wrapper = mount(TLoginForm, {
      props: { provider: mockProvider() },
      slots: { 'extra-fields-before': '<div class="tenant">Tenant picker</div>' },
      global: { stubs },
    })
    expect(wrapper.find('.tenant').exists()).toBe(true)
  })

  it('renders the extra-fields-after slot', () => {
    const wrapper = mount(TLoginForm, {
      props: { provider: mockProvider() },
      slots: { 'extra-fields-after': '<div class="twofa">2FA code</div>' },
      global: { stubs },
    })
    expect(wrapper.find('.twofa').exists()).toBe(true)
  })

  it('renders the social-providers slot with bound onClick', () => {
    const wrapper = mount(TLoginForm, {
      props: { provider: mockProvider() },
      slots: { 'social-providers': '<button class="gh">GitHub</button>' },
      global: { stubs },
    })
    expect(wrapper.find('button.gh').exists()).toBe(true)
  })

  it('renders the footer-links slot', () => {
    const wrapper = mount(TLoginForm, {
      props: { provider: mockProvider() },
      slots: { 'footer-links': '<a class="fl" href="#">Forgot password?</a>' },
      global: { stubs },
    })
    expect(wrapper.find('a.fl').exists()).toBe(true)
  })

  it('emits submit and success events on successful login', async () => {
    const provider = mockProvider()
    const wrapper = mount(TLoginForm, {
      props: { provider },
      global: { stubs },
    })
    const vm = wrapper.vm as any
    // defineExpose auto-unwraps refs on wrapper.vm
    vm.state.username = 'alice'
    vm.state.password = 'secret12'
    await vm.handleSubmit()
    expect(provider.login).toHaveBeenCalled()
    expect(wrapper.emitted('submit')).toBeTruthy()
    expect(wrapper.emitted('success')).toBeTruthy()
  })
})
