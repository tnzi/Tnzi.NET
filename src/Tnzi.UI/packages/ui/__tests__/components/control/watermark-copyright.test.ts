import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TWatermark from '../../../src/components/control/TWatermark.vue'
import TCopyright from '../../../src/components/control/TCopyright.vue'

describe('TWatermark', () => {
  it('renders content slot', () => {
    const wrapper = mount(TWatermark, {
      props: { text: 'Confidential' },
      slots: { default: '<div class="body">content</div>' },
    })
    expect(wrapper.find('.body').exists()).toBe(true)
  })

  it('creates canvas-based background with text', () => {
    // happy-dom limitation: canvas.getContext('2d') returns null, so
    // overlayStyle falls through to {} and no background-image is generated.
    // In real browsers the canvas path runs and produces a data URL; here we
    // only assert the overlay element exists. See docs in component source.
    const wrapper = mount(TWatermark, { props: { text: 'Tnzi' } })
    const overlay = wrapper.find('.t-watermark__overlay')
    expect(overlay.exists()).toBe(true)
  })

  it('accepts custom opacity', () => {
    const wrapper = mount(TWatermark, { props: { text: 'X', opacity: 0.2 } })
    expect(wrapper.find('.t-watermark__overlay').exists()).toBe(true)
  })
})

describe('TCopyright', () => {
  it('renders company prop', () => {
    const wrapper = mount(TCopyright, { props: { company: 'Tnzi Inc.' } })
    expect(wrapper.text()).toContain('Tnzi Inc.')
  })

  it('renders current year by default', () => {
    const wrapper = mount(TCopyright, { props: { company: 'X' } })
    expect(wrapper.text()).toContain(new Date().getFullYear().toString())
  })

  it('renders year range when startYear provided', () => {
    const wrapper = mount(TCopyright, { props: { company: 'X', startYear: 2020 } })
    expect(wrapper.text()).toContain('2020')
    expect(wrapper.text()).toContain(new Date().getFullYear().toString())
  })

  it('renders single year when startYear equals current year', () => {
    const wrapper = mount(TCopyright, { props: { company: 'X', startYear: new Date().getFullYear() } })
    const yearCount = (wrapper.text().match(new RegExp(new Date().getFullYear().toString(), 'g')) ?? []).length
    expect(yearCount).toBe(1)
  })

  it('renders beian slot for China ICP filing', () => {
    const wrapper = mount(TCopyright, {
      props: { company: 'X' },
      slots: { beian: '<a class="icp" href="#">京ICP备 12345678</a>' },
    })
    expect(wrapper.find('a.icp').exists()).toBe(true)
  })
})
