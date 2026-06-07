import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import TDetailHost from '../../../src/components/detail/TDetailHost.vue'

const stubs = {
  Modal: { name: 'Modal', props: ['show'], template: '<div v-if="show" class="n-modal-stub"><slot /><slot name="footer"/></div>' },
  Drawer: { name: 'Drawer', props: ['show'], template: '<div v-if="show" class="n-drawer-stub"><slot /></div>' },
  DrawerContent: { name: 'DrawerContent', template: '<div class="n-drawer-content-stub"><slot /><slot name="footer"/></div>' },
  Tabs: { name: 'Tabs', template: '<div><slot /></div>' },
  TabPane: { name: 'TabPane', template: '<div><slot /></div>' },
  Menu: true,
  Popover: { name: 'Popover', template: '<div><slot name="trigger" /><slot /></div>' },
  Button: { name: 'Button', template: '<button @click="$emit(\'click\')"><slot /></button>' },
  SvgIcon: true,
}

function makeState(mode: 'modal' | 'drawer' | 'page', visible = true) {
  return {
    mode: ref(mode), action: ref('edit'), visible: ref(visible),
    data: ref({ id: 1, name: 'a' }), loading: ref(false), error: ref(null),
    activeSection: ref('basic'),
    open: async () => {}, close: () => {}, submit: async () => {}, setSection: () => {},
  }
}

describe('TDetailHost', () => {
  it('renders NModal in modal mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('modal') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.n-modal-stub').exists()).toBe(true)
    expect(w.find('.body').exists()).toBe(true)
  })

  it('renders NDrawer in drawer mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('drawer') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.n-drawer-stub').exists()).toBe(true)
  })

  it('renders bare TDetailLayout (no overlay) in page mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('page') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.n-modal-stub').exists()).toBe(false)
    expect(w.find('.n-drawer-stub').exists()).toBe(false)
    expect(w.find('.t-detail-layout').exists()).toBe(true)
  })

  it('suppresses the in-layout header in modal mode (overlay owns chrome)', () => {
    const w = mount(TDetailHost, { props: { state: makeState('modal') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.t-page-header').exists()).toBe(false)
  })

  it('shows the in-layout header in page mode', () => {
    const w = mount(TDetailHost, { props: { state: makeState('page') as any, title: 'Edit' }, slots: { default: '<div class="body" />' }, global: { stubs } })
    expect(w.find('.t-page-header').exists()).toBe(true)
  })
})
