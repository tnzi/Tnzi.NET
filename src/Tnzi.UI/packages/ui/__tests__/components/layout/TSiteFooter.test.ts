import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TSiteFooter from '../../../src/components/layout/TSiteFooter.vue'

describe('TSiteFooter', () => {
  it('renders a footer element', () => {
    const wrapper = mount(TSiteFooter)
    expect(wrapper.find('footer').exists()).toBe(true)
  })

  it('renders default slot content', () => {
    const wrapper = mount(TSiteFooter, {
      slots: { default: '<div class="c">content</div>' },
    })
    expect(wrapper.find('div.c').exists()).toBe(true)
  })

  it('renders copyright slot', () => {
    const wrapper = mount(TSiteFooter, {
      slots: { copyright: '<span class="cp">©2026</span>' },
    })
    expect(wrapper.find('span.cp').exists()).toBe(true)
  })

  it('renders columns slot for multi-column layout', () => {
    const wrapper = mount(TSiteFooter, {
      slots: { columns: '<div class="cols">4 columns</div>' },
    })
    expect(wrapper.find('div.cols').exists()).toBe(true)
  })

  it('renders beian slot for China ICP filing', () => {
    const wrapper = mount(TSiteFooter, {
      slots: { beian: '<a class="icp">ICP 12345</a>' },
    })
    expect(wrapper.find('a.icp').exists()).toBe(true)
  })
})
