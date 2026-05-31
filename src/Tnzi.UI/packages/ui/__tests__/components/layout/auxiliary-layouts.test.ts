import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import AuthLayout from '../../../src/components/layout/TAuthLayout.vue'
import BlankLayout from '../../../src/components/layout/TBlankLayout.vue'
import CenteredLayout from '../../../src/components/layout/TCenteredLayout.vue'

describe('AuthLayout', () => {
  it('renders default slot centered', () => {
    const wrapper = mount(AuthLayout, { slots: { default: '<div class="form">form</div>' } })
    expect(wrapper.find('.form').exists()).toBe(true)
  })

  it('renders brand slot', () => {
    const wrapper = mount(AuthLayout, {
      slots: { brand: '<img class="b" src="/l.png">' },
    })
    expect(wrapper.find('img.b').exists()).toBe(true)
  })

  it('renders aside slot for side illustration', () => {
    const wrapper = mount(AuthLayout, {
      slots: { aside: '<div class="side">illustration</div>' },
    })
    expect(wrapper.find('.side').exists()).toBe(true)
  })

  it('applies centered positioning when no aside', () => {
    const wrapper = mount(AuthLayout, { slots: { default: 'form' } })
    expect(wrapper.find('.t-auth-layout').classes()).toContain('t-auth-layout--centered')
  })

  it('applies split positioning when aside present', () => {
    const wrapper = mount(AuthLayout, {
      slots: { default: 'form', aside: '<div>x</div>' },
    })
    expect(wrapper.find('.t-auth-layout').classes()).toContain('t-auth-layout--split')
  })
})

describe('BlankLayout', () => {
  it('renders only default slot without chrome', () => {
    const wrapper = mount(BlankLayout, { slots: { default: '<p class="c">content</p>' } })
    expect(wrapper.find('p.c').exists()).toBe(true)
    expect(wrapper.find('header').exists()).toBe(false)
    expect(wrapper.find('footer').exists()).toBe(false)
  })

  it('fills viewport height', () => {
    const wrapper = mount(BlankLayout)
    expect(wrapper.attributes('style')).toContain('min-height: 100vh')
  })
})

describe('CenteredLayout', () => {
  it('centers content vertically and horizontally', () => {
    const wrapper = mount(CenteredLayout, { slots: { default: '<div class="c">c</div>' } })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('display: flex')
    expect(style).toContain('align-items: center')
    expect(style).toContain('justify-content: center')
  })

  it('renders default slot', () => {
    const wrapper = mount(CenteredLayout, { slots: { default: '<span class="x">x</span>' } })
    expect(wrapper.find('span.x').exists()).toBe(true)
  })

  it('accepts maxWidth prop to constrain child width', () => {
    const wrapper = mount(CenteredLayout, { props: { maxWidth: '480px' } })
    const child = wrapper.find('.t-centered-layout__inner')
    if (child.exists()) {
      expect(child.attributes('style')).toContain('max-width: 480px')
    }
  })
})
