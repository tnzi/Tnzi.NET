import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TContainer from '../../../src/components/layout/TContainer.vue'

describe('TContainer', () => {
  it('renders children via default slot', () => {
    const wrapper = mount(TContainer, {
      slots: { default: '<p class="inner">hello</p>' },
    })
    expect(wrapper.find('.inner').exists()).toBe(true)
    expect(wrapper.text()).toContain('hello')
  })

  it('applies default maxWidth xl (1280px)', () => {
    const wrapper = mount(TContainer)
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('max-width: 1280px')
  })

  it('accepts custom maxWidth as prop', () => {
    const wrapper = mount(TContainer, { props: { maxWidth: '960px' } })
    expect(wrapper.attributes('style')).toContain('max-width: 960px')
  })

  it('accepts maxWidth size preset', () => {
    const wrapper = mount(TContainer, { props: { maxWidth: 'md' } })
    expect(wrapper.attributes('style')).toContain('max-width: 768px')
  })

  it('centers horizontally via margin auto', () => {
    const wrapper = mount(TContainer)
    expect(wrapper.attributes('style')).toContain('margin-left: auto')
    expect(wrapper.attributes('style')).toContain('margin-right: auto')
  })

  it('applies fluid prop to disable max-width', () => {
    const wrapper = mount(TContainer, { props: { fluid: true } })
    expect(wrapper.attributes('style')).not.toContain('max-width')
  })

  it('applies custom padding via padding prop', () => {
    const wrapper = mount(TContainer, { props: { padding: '32px' } })
    expect(wrapper.attributes('style')).toContain('padding: 32px')
  })

  it('default padding has zero vertical component', () => {
    const wrapper = mount(TContainer)
    // Default is '0 clamp(16px, 4vw, 32px)' — vertical 0, horizontal responsive.
    // jsdom may drop the shorthand containing clamp() from the serialized style
    // attribute, so assert on the resolved prop value instead.
    expect(wrapper.props('padding')).toMatch(/^0\s/)
  })
})
