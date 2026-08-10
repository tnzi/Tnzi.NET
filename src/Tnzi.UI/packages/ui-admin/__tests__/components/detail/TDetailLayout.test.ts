import { describe, it, expect } from 'vitest'
import { h } from 'vue'
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

  it('forwards #extra to the header, under the title in the identity column', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit' },
      slots: {
        default: '<div class="body" />',
        extra: '<span class="meta">FILE-2024-0912 - Litigation - Acme Corp</span>',
      },
      global: { stubs },
    })
    // Its own row inside the identity column (so it lines up with the title)...
    expect(w.find('.t-page-header__main > .t-page-header__extra .meta').exists()).toBe(true)
    // ...and NOT in the identity row itself, whose width sizes the title.
    expect(w.find('.t-page-header__left .meta').exists()).toBe(false)
  })

  it('renders no extra row when the page supplies no #extra', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'plain', title: 'Edit' },
      slots: { default: '<div class="body" />', actions: '<button class="act" />' },
      global: { stubs },
    })
    expect(w.find('.t-page-header__extra').exists()).toBe(false)
    expect(w.find('.t-page-header__bar .act').exists()).toBe(true)
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

  it('exposes the active section icon as a slot prop (menu icon ⇒ panel title icon)', () => {
    const iconSections = [
      { key: 'basic', label: 'Basic', icon: 'mdi:information-outline' },
      { key: 'perms', label: 'Perms', icon: 'mdi:shield-outline', group: 'security' },
    ]
    const w = mount(TDetailLayout, {
      props: { layout: 'side', sections: iconSections, activeSection: 'perms' },
      slots: {
        default: (p: { sectionIcon?: string }) => h('span', { class: 'icon-probe' }, p.sectionIcon ?? ''),
      },
      global: { stubs },
    })
    expect(w.find('.icon-probe').text()).toBe('mdi:shield-outline')
  })

  it('section icon slot prop is empty when the active section has no icon', () => {
    const w = mount(TDetailLayout, {
      props: { layout: 'side', sections, activeSection: 'basic' },
      slots: {
        default: (p: { sectionIcon?: string }) => h('span', { class: 'icon-probe' }, p.sectionIcon ?? ''),
      },
      global: { stubs },
    })
    expect(w.find('.icon-probe').text()).toBe('')
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
