import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TEmpty from '../../../src/components/feedback/TEmpty.vue'
import TResult from '../../../src/components/feedback/TResult.vue'

describe('TEmpty', () => {
  it('renders default description', () => {
    const wrapper = mount(TEmpty)
    expect(wrapper.text()).toContain('No data')
  })
  it('overrides description via prop', () => {
    const wrapper = mount(TEmpty, { props: { description: 'Nothing here' } })
    expect(wrapper.text()).toContain('Nothing here')
  })
  it('renders custom icon via slot', () => {
    const wrapper = mount(TEmpty, { slots: { icon: '<svg class="custom" />' } })
    expect(wrapper.find('svg.custom').exists()).toBe(true)
  })
  it('renders action slot', () => {
    const wrapper = mount(TEmpty, { slots: { action: '<button class="a">Refresh</button>' } })
    expect(wrapper.find('button.a').exists()).toBe(true)
  })
})

describe('TResult', () => {
  it('renders success status with icon', () => {
    const wrapper = mount(TResult, { props: { status: 'success', title: 'Done' } })
    expect(wrapper.text()).toContain('Done')
    expect(wrapper.find('.t-result--success').exists()).toBe(true)
  })
  it('renders error status', () => {
    const wrapper = mount(TResult, { props: { status: 'error', title: 'Oops' } })
    expect(wrapper.find('.t-result--error').exists()).toBe(true)
  })
  it('renders warning status', () => {
    const wrapper = mount(TResult, { props: { status: 'warning', title: 'Careful' } })
    expect(wrapper.find('.t-result--warning').exists()).toBe(true)
  })
  it('renders info status', () => {
    const wrapper = mount(TResult, { props: { status: 'info', title: 'FYI' } })
    expect(wrapper.find('.t-result--info').exists()).toBe(true)
  })
  it('renders description slot', () => {
    const wrapper = mount(TResult, {
      props: { status: 'success', title: 'Done' },
      slots: { description: '<p class="d">All good</p>' },
    })
    expect(wrapper.find('p.d').exists()).toBe(true)
  })
  it('renders action slot', () => {
    const wrapper = mount(TResult, {
      props: { status: 'success', title: 'Done' },
      slots: { action: '<button class="a">Next</button>' },
    })
    expect(wrapper.find('button.a').exists()).toBe(true)
  })
})

// TSkeleton tests moved to __tests__/components/display/TSkeleton.test.ts
// when the component was relocated in 0.2.71+ (C2 sink).
