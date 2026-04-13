import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import TAdminContent from '../../../src/components/layout/TAdminContent.vue'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'

describe('TAdminContent', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders default slot content', () => {
    const wrapper = mount(TAdminContent, {
      slots: { default: '<div class="page">hello</div>' },
    })
    expect(wrapper.find('.page').exists()).toBe(true)
    expect(wrapper.find('.page').text()).toBe('hello')
  })

  it('applies transition name prop to inner transition', () => {
    const wrapper = mount(TAdminContent, {
      props: { transitionName: 'slide-left' },
      slots: { default: '<div>x</div>' },
    })
    expect((wrapper.vm as unknown as { currentTransition: string }).currentTransition).toBe('slide-left')
  })

  it('maps "none" transition to empty string (disables animation)', () => {
    const wrapper = mount(TAdminContent, {
      props: { transitionName: 'none' },
      slots: { default: '<div>x</div>' },
    })
    expect((wrapper.vm as unknown as { currentTransition: string }).currentTransition).toBe('')
  })

  it('sets data-full-content attribute when store.fullContent is true', async () => {
    const store = useAdminAppStore()
    store.toggleFullContent()
    const wrapper = mount(TAdminContent, { slots: { default: 'x' } })
    await nextTick()
    expect(wrapper.attributes('data-full-content')).toBe('true')
  })

  it('hides content when reloadFlag is false then restores it', async () => {
    const store = useAdminAppStore()
    const wrapper = mount(TAdminContent, {
      slots: { default: '<div class="page">hi</div>' },
    })
    expect(wrapper.find('.page').exists()).toBe(true)
    const promise = store.reloadPage()
    await nextTick()
    expect(wrapper.find('.page').exists()).toBe(false)
    await promise
    await nextTick()
    expect(wrapper.find('.page').exists()).toBe(true)
  })

  it('default transitionName is "fade"', () => {
    const wrapper = mount(TAdminContent, {
      slots: { default: 'x' },
    })
    expect((wrapper.vm as unknown as { currentTransition: string }).currentTransition).toBe('fade')
  })
})
