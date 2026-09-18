import { describe, it, expect, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import Vant, { Tabbar } from 'vant'
import type { Component } from 'vue'
import { createTnziMobile } from '../src/plugin'
import TLoginForm from '../src/components/auth/TLoginForm.vue'
import TRegisterForm from '../src/components/auth/TRegisterForm.vue'
import TPasswordReset from '../src/components/auth/TPasswordReset.vue'
import TStatCard from '../src/components/card/TStatCard.vue'
import TUserCard from '../src/components/card/TUserCard.vue'
import TForm from '../src/components/form/TForm.vue'
import TMenu from '../src/components/navigation/TMenu.vue'
import TNavBar from '../src/components/navigation/TNavBar.vue'
import TTabBar from '../src/components/navigation/TTabBar.vue'

const global = { plugins: [Vant] }

describe('class fallthrough', () => {
  // Declaring `class` as a prop removes it from $attrs, so a component that
  // declares it without re-binding drops it silently. None of these may.
  const cases: Array<[string, Component, Record<string, unknown>]> = [
    ['TStatCard', TStatCard, { title: 'Revenue', value: 42 }],
    ['TUserCard', TUserCard, { user: { id: 1, name: 'Alice' } }],
    ['TForm', TForm, { model: {} }],
    ['TMenu', TMenu, { items: [] }],
    ['TNavBar', TNavBar, { title: 'Home' }],
    ['TTabBar', TTabBar, { tabs: [] }],
  ]

  it.each(cases)('%s forwards a consumer class to its root', (_name, component, props) => {
    const wrapper = mount(component, { props, attrs: { class: 'consumer-class' }, global })
    expect(wrapper.classes()).toContain('consumer-class')
  })
})

describe('TStatCard', () => {
  it('renders the translated loading label, not a hardcoded string', () => {
    const wrapper = mount(TStatCard, { props: { title: 'Revenue', value: 1, loading: true }, global })
    expect(wrapper.text()).toContain('Loading')
  })

  it('scales the value with the size prop', () => {
    const small = mount(TStatCard, { props: { title: 'A', value: 1, size: 'small' }, global })
    const large = mount(TStatCard, { props: { title: 'A', value: 1, size: 'large' }, global })
    expect(small.html()).toContain('text-xl')
    expect(large.html()).toContain('text-4xl')
  })

  it('tints the value with the color prop', () => {
    const wrapper = mount(TStatCard, { props: { title: 'A', value: 1, color: 'green' }, global })
    expect(wrapper.html()).toContain('var(--van-green)')
  })
})

describe('TUserCard', () => {
  it('falls back to the name initial when there is no avatar', () => {
    const wrapper = mount(TUserCard, { props: { user: { id: 1, name: 'alice' } }, global })
    expect(wrapper.text()).toContain('A')
  })

  it('prefers an explicit avatarFallback', () => {
    const wrapper = mount(TUserCard, {
      props: { user: { id: 1, name: 'alice' }, avatarFallback: 'ZZ' },
      global,
    })
    expect(wrapper.text()).toContain('ZZ')
  })
})

describe('TNavBar', () => {
  it('applies backgroundColor and textColor through Vant custom properties', () => {
    const wrapper = mount(TNavBar, {
      props: { title: 'Home', backgroundColor: 'rgb(1, 2, 3)', textColor: 'rgb(4, 5, 6)' },
      global,
    })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('--van-nav-bar-background: rgb(1, 2, 3)')
    expect(style).toContain('--van-nav-bar-title-text-color: rgb(4, 5, 6)')
  })
})

describe('TTabBar', () => {
  it('drives activeKey through the model, not a shadowing prop', async () => {
    const wrapper = mount(TTabBar, {
      props: { tabs: [{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }], activeKey: 'a' },
      global,
    })

    expect(wrapper.findComponent(Tabbar).props('modelValue')).toBe('a')

    wrapper.findComponent(Tabbar).vm.$emit('change', 'b')
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('update:activeKey')?.[0]).toEqual(['b'])
    expect(wrapper.emitted('change')?.[0]?.[0]).toBe('b')
  })
})

describe('auth forms wired to the headless layer', () => {
  it('TLoginForm submits the credentials held by useLoginForm', async () => {
    const wrapper = mount(TLoginForm, { global })

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice')
    await inputs[1]!.setValue('secret')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    // No "remember me" by default: nothing in the framework reads the flag
    // (AuthStateManager persists the session the same way either way), so a
    // ticked box that changes nothing is a consumer-owned opt-in, not a default.
    expect(wrapper.find('.van-checkbox').exists()).toBe(false)
    expect(wrapper.emitted('submit')?.[0]).toEqual([
      { userName: 'alice', password: 'secret', rememberMe: undefined, captchaId: undefined, captchaCode: undefined },
    ])
  })

  it('TLoginForm forwards the remember-me choice only when the consumer opts in', async () => {
    const wrapper = mount(TLoginForm, { props: { showRememberMe: true }, global })
    expect(wrapper.find('.van-checkbox').exists()).toBe(true)

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice')
    await inputs[1]!.setValue('secret')
    await wrapper.find('.van-checkbox').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')?.[0]?.[0]).toMatchObject({ userName: 'alice', rememberMe: true })
  })

  it('TLoginForm shows a translated message instead of a hardcoded one', async () => {
    const wrapper = mount(TLoginForm, { global })

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(wrapper.text()).toContain('Please enter Username')
  })

  it('TRegisterForm keeps the terms gate and reports password mismatch', async () => {
    const wrapper = mount(TRegisterForm, { props: { showUsername: false }, global })

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice@example.com')
    await inputs[1]!.setValue('secret1')
    await inputs[2]!.setValue('secret2')
    await flushPromises()

    // Mismatch plus un-agreed terms both keep the submit button disabled.
    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('TRegisterForm does not submit while the terms are unchecked', async () => {
    const wrapper = mount(TRegisterForm, { props: { showUsername: false }, global })

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice@example.com')
    await inputs[1]!.setValue('secret1')
    await inputs[2]!.setValue('secret1')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
  })

  // The headless composables refuse an incomplete submit and write `errors`,
  // but nothing in the SFCs rendered those, so a tap on Submit with a blank
  // field did nothing visible. Every field the composable checks must carry a
  // Vant rule so the refusal is shown where the user is looking.
  it('TPasswordReset shows a message for every blank required field instead of doing nothing', async () => {
    const wrapper = mount(TPasswordReset, { global })

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
    const messages = wrapper.findAll('.van-field__error-message').map((el) => el.text())
    expect(messages).toEqual(
      expect.arrayContaining([
        'Please enter Email',
        'Please enter Verification Code',
        'Please enter New Password',
      ]),
    )
  })

  it('TPasswordReset submits once every required field is filled', async () => {
    const wrapper = mount(TPasswordReset, { global })

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice@example.com')
    await inputs[1]!.setValue('123456')
    await inputs[2]!.setValue('secret1')
    await inputs[3]!.setValue('secret1')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')?.[0]).toEqual([
      { email: 'alice@example.com', code: '123456', password: 'secret1' },
    ])
  })

  it('TLoginForm with a captcha shows a message when the captcha is blank', async () => {
    const wrapper = mount(TLoginForm, { props: { showCaptcha: true, captchaId: 'c1' }, global })

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice')
    await inputs[1]!.setValue('secret')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(wrapper.text()).toContain('Please enter Verification Code')
  })

  it('TRegisterForm with a captcha shows a message when the captcha is blank', async () => {
    const wrapper = mount(TRegisterForm, { props: { showUsername: false, showCaptcha: true, captchaId: 'c1' }, global })

    const inputs = wrapper.findAll('input')
    await inputs[0]!.setValue('alice@example.com')
    await inputs[1]!.setValue('secret1')
    await inputs[2]!.setValue('secret1')
    await wrapper.find('.van-checkbox').trigger('click')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(wrapper.text()).toContain('Please enter Verification Code')
  })

  it('TPasswordReset runs the resend countdown from usePasswordReset', async () => {
    const wrapper = mount(TPasswordReset, { props: { countdownSeconds: 30 }, global })

    await wrapper.findAll('input')[0]!.setValue('alice@example.com')
    await wrapper.find('.van-field__button button').trigger('click')
    await flushPromises()

    expect(wrapper.emitted('sendCode')?.[0]).toEqual(['alice@example.com'])
    expect(wrapper.text()).toContain('30s')
  })
})

describe('plugin install', () => {
  // The T* templates use <van-*> global tags. A consumer who follows the README
  // installs only createTnziMobile(); if that did not register Vant, every
  // component rendered as unknown custom elements with no <input> inside.
  it('registers Vant so a T* component works with the plugin alone', () => {
    const wrapper = mount(TLoginForm, { global: { plugins: [createTnziMobile()] } })
    expect(wrapper.find('form').exists()).toBe(true)
    expect(wrapper.findAll('input').length).toBeGreaterThanOrEqual(2)
  })

  it('does not re-install Vant when the consumer already did', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const wrapper = mount(TLoginForm, { global: { plugins: [Vant, createTnziMobile()] } })
    expect(wrapper.find('form').exists()).toBe(true)
    expect(warn.mock.calls.map((c) => String(c[0]))).not.toContainEqual(expect.stringContaining('already been applied'))
    warn.mockRestore()
  })

  it('leaves Vant alone when component registration is switched off', () => {
    const wrapper = mount(TLoginForm, { global: { plugins: [createTnziMobile({ registerComponents: false })] } })
    expect(wrapper.find('form').exists()).toBe(false)
  })
})
