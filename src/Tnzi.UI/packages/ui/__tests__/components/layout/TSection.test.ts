import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TSection from '../../../src/components/layout/TSection.vue'

describe('TSection', () => {
  it('renders default slot content', () => {
    const wrapper = mount(TSection, {
      slots: { default: '<p>body</p>' },
    })
    expect(wrapper.text()).toContain('body')
  })

  it('renders a title via prop', () => {
    const wrapper = mount(TSection, { props: { title: 'About us' } })
    expect(wrapper.text()).toContain('About us')
  })

  it('renders a title via slot (overrides prop)', () => {
    const wrapper = mount(TSection, {
      props: { title: 'Prop title' },
      slots: { title: '<span>Slot title</span>' },
    })
    expect(wrapper.text()).toContain('Slot title')
    expect(wrapper.text()).not.toContain('Prop title')
  })

  it('renders a subtitle slot', () => {
    const wrapper = mount(TSection, {
      slots: { subtitle: '<span>Subtitle here</span>' },
    })
    expect(wrapper.text()).toContain('Subtitle here')
  })

  it('omits header entirely when no title or subtitle', () => {
    const wrapper = mount(TSection, { slots: { default: 'content' } })
    expect(wrapper.find('header').exists()).toBe(false)
  })

  it('applies default padding y 64px', () => {
    const wrapper = mount(TSection)
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('padding-top: 64px')
    expect(style).toContain('padding-bottom: 64px')
  })

  it('accepts custom paddingY prop', () => {
    const wrapper = mount(TSection, { props: { paddingY: '32px' } })
    expect(wrapper.attributes('style')).toContain('padding-top: 32px')
  })

  it('renders actions slot in header', () => {
    const wrapper = mount(TSection, {
      props: { title: 'Title' },
      slots: { actions: '<button class="act">Action</button>' },
    })
    expect(wrapper.find('button.act').exists()).toBe(true)
  })
})
