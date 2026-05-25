import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TSkeleton from '../../../src/components/display/TSkeleton.vue'

describe('TSkeleton', () => {
  it('renders a single block by default with animated class', () => {
    const wrapper = mount(TSkeleton)
    expect(wrapper.classes()).toContain('t-skeleton')
    expect(wrapper.classes()).toContain('t-skeleton--animated')
  })

  it('omits animated class when animated=false', () => {
    const wrapper = mount(TSkeleton, { props: { animated: false } })
    expect(wrapper.classes()).not.toContain('t-skeleton--animated')
  })

  it('renders circular avatar variant with 50% border-radius', () => {
    const wrapper = mount(TSkeleton, { props: { variant: 'avatar' } })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('border-radius: 50%')
    expect(style).toContain('width: 40px')
    expect(style).toContain('height: 40px')
  })

  it('renders multiple lines when variant=text + lines>1', () => {
    const wrapper = mount(TSkeleton, {
      props: { variant: 'text', lines: 3 },
    })
    expect(wrapper.classes()).toContain('t-skeleton-stack')
    expect(wrapper.findAll('.t-skeleton')).toHaveLength(3)
  })

  it('shortens the last text line to 70% width', () => {
    const wrapper = mount(TSkeleton, {
      props: { variant: 'text', lines: 3, width: 200 },
    })
    const lines = wrapper.findAll('.t-skeleton')
    const lastStyle = lines[2]!.attributes('style') ?? ''
    expect(lastStyle).toContain('width: 70%')
  })

  it('passes numeric width/height through as px', () => {
    const wrapper = mount(TSkeleton, {
      props: { width: 120, height: 24, radius: 8 },
    })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('width: 120px')
    expect(style).toContain('height: 24px')
    expect(style).toContain('border-radius: 8px')
  })

  it('passes string width/height through unchanged', () => {
    const wrapper = mount(TSkeleton, {
      props: { width: '50%', height: '2em' },
    })
    const style = wrapper.attributes('style') ?? ''
    expect(style).toContain('width: 50%')
    expect(style).toContain('height: 2em')
  })

  it('is marked aria-hidden', () => {
    const wrapper = mount(TSkeleton)
    expect(wrapper.attributes('aria-hidden')).toBe('true')
  })
})
