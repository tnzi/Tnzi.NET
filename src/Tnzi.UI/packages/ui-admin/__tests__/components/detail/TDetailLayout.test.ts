import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TDetailLayout from '../../../src/components/detail/TDetailLayout.vue'

const stubs = {
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Tabs: { name: 'Tabs', props: ['value'], template: '<div class="n-tabs-stub"><slot /></div>' },
  TabPane: { name: 'TabPane', props: ['name', 'tab'], template: '<div class="n-tabpane-stub"><slot /></div>' },
  Menu: { name: 'Menu', props: ['value', 'options'], template: '<div class="n-menu-stub" />' },
  SvgIcon: true,
}
const sections = [
  { key: 'basic', label: 'Basic' },
  { key: 'perms', label: 'Perms', group: 'security' },
]

describe('TDetailLayout', () => {
  it('plain layout renders header + body + footer slots', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit' },
      slots: { default: '<div class="body">B</div>', footer: '<div class="ft">F</div>' },
      global: { stubs },
    })
    expect(w.find('.t-page-header__title').text()).toBe('Edit')
    expect(w.find('.body').text()).toBe('B')
    expect(w.find('.ft').text()).toBe('F')
  })

  it('tabs layout renders an NTabs from sections', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'tabs', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.n-tabs-stub').exists()).toBe(true)
  })

  it('side layout renders the left NMenu', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.n-menu-stub').exists()).toBe(true)
    expect(w.find('.t-detail-layout--side').exists()).toBe(true)
  })

  it('emits update:activeSection when a section is selected', async () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    ;(w.vm as unknown as { onSection: (k: string) => void }).onSection('perms')
    await w.vm.$nextTick()
    expect(w.emitted('update:activeSection')?.[0]).toEqual(['perms'])
  })

  it('omits the header when showHeader=false', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit', showHeader: false },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    expect(w.find('.t-page-header').exists()).toBe(false)
    expect(w.find('.body').exists()).toBe(true)
  })

  it('exposes scrollToSection that activates the target section', async () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', title: 'X', sections, activeSection: 'basic' },
      slots: { default: '<div class="body" />' },
      global: { stubs },
    })
    const vm = w.vm as unknown as { scrollToSection?: (k: string) => Promise<void> }
    expect(typeof vm.scrollToSection).toBe('function')
    await vm.scrollToSection?.('perms')
    expect(w.emitted('update:activeSection')?.[0]).toEqual(['perms'])
  })
})
