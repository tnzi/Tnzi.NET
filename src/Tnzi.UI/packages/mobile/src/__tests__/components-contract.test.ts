import { describe, it, expect } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import Vant, { Tabbar } from 'vant'
import type { Component } from 'vue'
import TLoginForm from '../components/auth/TLoginForm.vue'
import TRegisterForm from '../components/auth/TRegisterForm.vue'
import TPasswordReset from '../components/auth/TPasswordReset.vue'
import TStatCard from '../components/card/TStatCard.vue'
import TUserCard from '../components/card/TUserCard.vue'
import TForm from '../components/form/TForm.vue'
import TMenu from '../components/navigation/TMenu.vue'
import TNavBar from '../components/navigation/TNavBar.vue'
import TTabBar from '../components/navigation/TTabBar.vue'

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

    expect(wrapper.emitted('submit')?.[0]).toEqual([
      { userName: 'alice', password: 'secret', rememberMe: false, captchaId: undefined, captchaCode: undefined },
    ])
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

  it('TPasswordReset runs the resend countdown from usePasswordReset', async () => {
    const wrapper = mount(TPasswordReset, { props: { countdownSeconds: 30 }, global })

    await wrapper.findAll('input')[0]!.setValue('alice@example.com')
    await wrapper.find('.van-field__button button').trigger('click')
    await flushPromises()

    expect(wrapper.emitted('sendCode')?.[0]).toEqual(['alice@example.com'])
    expect(wrapper.text()).toContain('30s')
  })
})
