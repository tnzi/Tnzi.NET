import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TDarkModeContainer from '../../../src/components/layout/TDarkModeContainer.vue'

describe('TDarkModeContainer', () => {
  it('renders default slot content inside a div by default', () => {
    const wrapper = mount(TDarkModeContainer, {
      slots: { default: '<span class="child">hi</span>' },
    })
    expect(wrapper.element.tagName).toBe('DIV')
    expect(wrapper.find('.child').text()).toBe('hi')
  })

  it('applies inverted class when inverted prop is true', () => {
    const wrapper = mount(TDarkModeContainer, {
      props: { inverted: true },
      slots: { default: 'x' },
    })
    expect(wrapper.classes()).toContain('t-dark-mode-container--inverted')
    expect(wrapper.classes()).not.toContain('t-dark-mode-container--brand')
  })

  it('applies brand class when brand prop is true', () => {
    const wrapper = mount(TDarkModeContainer, {
      props: { brand: true },
      slots: { default: 'x' },
    })
    expect(wrapper.classes()).toContain('t-dark-mode-container--brand')
  })

  it('applies both classes when both modifiers active', () => {
    const wrapper = mount(TDarkModeContainer, {
      props: { inverted: true, brand: true },
      slots: { default: 'x' },
    })
    expect(wrapper.classes()).toContain('t-dark-mode-container--inverted')
    expect(wrapper.classes()).toContain('t-dark-mode-container--brand')
  })

  it('uses custom tag when tag prop is given', () => {
    const wrapper = mount(TDarkModeContainer, {
      props: { tag: 'section' },
      slots: { default: 'x' },
    })
    expect(wrapper.element.tagName).toBe('SECTION')
  })

  it('renders neither modifier class by default', () => {
    const wrapper = mount(TDarkModeContainer, {
      slots: { default: 'x' },
    })
    expect(wrapper.classes()).not.toContain('t-dark-mode-container--inverted')
    expect(wrapper.classes()).not.toContain('t-dark-mode-container--brand')
    expect(wrapper.classes()).toContain('t-dark-mode-container')
  })

  it('applies the transition class by default (Phase I.6.7)', () => {
    const wrapper = mount(TDarkModeContainer, {
      slots: { default: 'x' },
    })
    expect(wrapper.classes()).toContain('t-dark-mode-container--transition')
  })

  it('omits the transition class when transition=none', () => {
    const wrapper = mount(TDarkModeContainer, {
      props: { transition: 'none' },
      slots: { default: 'x' },
    })
    expect(wrapper.classes()).not.toContain('t-dark-mode-container--transition')
  })
})
