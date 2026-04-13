import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TSiteHeader from '../../../src/components/layout/TSiteHeader.vue'

describe('TSiteHeader', () => {
  it('renders a header element', () => {
    const wrapper = mount(TSiteHeader)
    expect(wrapper.find('header').exists()).toBe(true)
  })

  it('renders logo slot', () => {
    const wrapper = mount(TSiteHeader, {
      slots: { logo: '<img class="lg" src="/l.png">' },
    })
    expect(wrapper.find('img.lg').exists()).toBe(true)
  })

  it('renders nav slot', () => {
    const wrapper = mount(TSiteHeader, {
      slots: { nav: '<nav class="n">links</nav>' },
    })
    expect(wrapper.find('nav.n').exists()).toBe(true)
  })

  it('renders actions slot', () => {
    const wrapper = mount(TSiteHeader, {
      slots: { actions: '<button class="a">Sign in</button>' },
    })
    expect(wrapper.find('button.a').exists()).toBe(true)
  })

  it('emits hamburger-click when mobile menu button clicked', async () => {
    const wrapper = mount(TSiteHeader, { props: { showHamburger: true } })
    await wrapper.find('.t-site-header__hamburger').trigger('click')
    expect(wrapper.emitted('hamburger-click')).toBeTruthy()
    expect(wrapper.emitted('hamburger-click')).toHaveLength(1)
  })

  it('hides hamburger when showHamburger is false', () => {
    const wrapper = mount(TSiteHeader, { props: { showHamburger: false } })
    expect(wrapper.find('.t-site-header__hamburger').exists()).toBe(false)
  })

  it('applies sticky prop via position style', () => {
    const wrapper = mount(TSiteHeader, { props: { sticky: true } })
    expect(wrapper.attributes('style')).toContain('position: sticky')
  })

  it('applies transparent variant (no background)', () => {
    const wrapper = mount(TSiteHeader, { props: { variant: 'transparent' } })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('background-color: transparent')
  })
})
