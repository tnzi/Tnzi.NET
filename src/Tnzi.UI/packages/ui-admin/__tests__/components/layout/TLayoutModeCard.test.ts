import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TLayoutModeCard from '../../../src/components/layout/TLayoutModeCard.vue'

describe('TLayoutModeCard', () => {
  it('renders label when provided', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'vertical', label: 'Vertical' },
    })
    expect(wrapper.text()).toContain('Vertical')
  })

  it('applies active class when active=true', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'horizontal', active: true },
    })
    expect(wrapper.classes()).toContain('t-layout-card--active')
  })

  it('does not apply active class when active=false', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'horizontal', active: false },
    })
    expect(wrapper.classes()).not.toContain('t-layout-card--active')
  })

  it('emits select with mode on click', async () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'vertical-mix' },
    })
    await wrapper.trigger('click')
    expect(wrapper.emitted('select')).toBeTruthy()
    expect(wrapper.emitted('select')![0]).toEqual(['vertical-mix'])
  })

  it('renders preview canvas with header + primary sider for vertical mode', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'vertical' },
    })
    expect(wrapper.find('.t-layout-card__header').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__sider--primary').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__main').exists()).toBe(true)
    // No tertiary sider in plain vertical (only vertical-mix uses one)
    expect(wrapper.find('.t-layout-card__sider--tertiary').exists()).toBe(false)
  })

  it('renders primary header + main + no sider for horizontal mode', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'horizontal' },
    })
    expect(wrapper.find('.t-layout-card__header--primary').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__sider').exists()).toBe(false)
    expect(wrapper.find('.t-layout-card__main').exists()).toBe(true)
  })

  it('renders 8px primary rail + 16px tertiary drawer for vertical-mix', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'vertical-mix' },
    })
    // Two siders: primary 8px + tertiary 16px (matches soybean's
    // layout-mode.vue:25-32 vertical-mix template).
    expect(wrapper.find('.t-layout-card__sider--primary.t-layout-card__sider--w8').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__sider--tertiary.t-layout-card__sider--w16').exists()).toBe(true)
  })

  it('vertical-hybrid-header-first: 8px+16px siders + primary header', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'vertical-hybrid-header-first' },
    })
    expect(wrapper.find('.t-layout-card__sider--primary.t-layout-card__sider--w8').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__sider--tertiary.t-layout-card__sider--w16').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__header--primary').exists()).toBe(true)
  })

  it('top-hybrid-sidebar-first: tertiary header + 18px primary sider', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'top-hybrid-sidebar-first' },
    })
    expect(wrapper.find('.t-layout-card__header--tertiary').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__sider--primary.t-layout-card__sider--w18').exists()).toBe(true)
  })

  it('top-hybrid-header-first: primary header + neutral 18px sider', () => {
    const wrapper = mount(TLayoutModeCard, {
      props: { mode: 'top-hybrid-header-first' },
    })
    expect(wrapper.find('.t-layout-card__header--primary').exists()).toBe(true)
    expect(wrapper.find('.t-layout-card__sider--w18').exists()).toBe(true)
    // Neutral sider: not primary-tinted (matches soybean's plain `w-18px` class)
    expect(wrapper.find('.t-layout-card__sider--w18.t-layout-card__sider--primary').exists()).toBe(false)
  })

  it('has aria-pressed reflecting active state', () => {
    const inactive = mount(TLayoutModeCard, {
      props: { mode: 'vertical', active: false },
    })
    expect(inactive.attributes('aria-pressed')).toBe('false')
    const active = mount(TLayoutModeCard, {
      props: { mode: 'vertical', active: true },
    })
    expect(active.attributes('aria-pressed')).toBe('true')
  })
})
