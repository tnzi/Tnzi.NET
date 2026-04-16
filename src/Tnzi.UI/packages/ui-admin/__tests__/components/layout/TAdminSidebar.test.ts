import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TAdminSidebar from '../../../src/components/layout/TAdminSidebar.vue'
import {
  useAdminRouteStore,
  type AdminRouteRecord,
  type AdminMenuItem,
} from '../../../src/stores/useAdminRouteStore'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'
import { useAdminTabStore } from '../../../src/stores/useAdminTabStore'

function seedRoutes(): AdminRouteRecord[] {
  return [
    {
      name: 'identity',
      path: '/identity',
      meta: { title: 'Identity', icon: 'user', order: 1 },
      children: [
        {
          name: 'identity.users',
          path: '/identity/users',
          meta: { title: 'Users', order: 1 },
        },
        {
          name: 'identity.roles',
          path: '/identity/roles',
          meta: { title: 'Roles', order: 2 },
        },
      ],
    },
    {
      name: 'system',
      path: '/system',
      meta: { title: 'System', icon: 'cog', order: 2 },
      children: [
        {
          name: 'system.menu',
          path: '/system/menu',
          meta: { title: 'Menu', order: 1 },
        },
      ],
    },
  ]
}

const menuStub = {
  name: 'Menu',
  props: [
    'options',
    'collapsed',
    'value',
    'mode',
    'indent',
    'collapsedWidth',
    'collapsedIconSize',
  ],
  emits: ['update:value'],
  template:
    '<ul class="n-menu-stub" :data-collapsed="collapsed" :data-value="value" :data-mode="mode">' +
    '<li v-for="o in options" :key="o.key" :data-key="o.key" @click="$emit(\'update:value\', o.key)">{{ o.label }}</li>' +
    '</ul>',
}

function mountSidebar(props: Record<string, unknown> = {}, slots: Record<string, string> = {}) {
  return mount(TAdminSidebar, {
    props,
    slots,
    global: {
      stubs: { Menu: menuStub },
    },
  })
}

describe('TAdminSidebar', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders menu options derived from routeStore.menus', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    const wrapper = mountSidebar()
    expect(wrapper.find('[data-key="identity"]').exists()).toBe(true)
    expect(wrapper.find('[data-key="system"]').exists()).toBe(true)
    expect(wrapper.find('[data-key="identity"]').text()).toContain('Identity')
  })

  it('passes collapsed state from appStore.siderCollapse', async () => {
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    const appStore = useAdminAppStore()
    appStore.setSiderCollapse(true)
    const wrapper = mountSidebar()
    expect(wrapper.find('.n-menu-stub').attributes('data-collapsed')).toBe('true')
  })

  it('binds active value to tabStore.activeTabId', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    const tabStore = useAdminTabStore()
    tabStore.addTab({
      name: 'identity',
      path: '/identity',
      fullPath: '/identity',
      meta: { title: 'Identity' },
    })
    tabStore.setActiveTab('identity')
    const wrapper = mountSidebar()
    expect(wrapper.find('.n-menu-stub').attributes('data-value')).toBe('identity')
  })

  it('emits menuSelect with resolved AdminMenuItem on selection', async () => {
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    const wrapper = mountSidebar()
    await wrapper.find('[data-key="identity"]').trigger('click')
    const emitted = wrapper.emitted('menuSelect')
    expect(emitted).toBeTruthy()
    const payload = emitted?.[0]?.[0] as AdminMenuItem
    expect(payload.key).toBe('identity')
    expect(payload.label).toBe('Identity')
    expect(payload.path).toBe('/identity')
  })

  it('mode="vertical-mix" renders only first-level entries (no children)', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    const wrapper = mountSidebar({ mode: 'vertical-mix' })
    expect(wrapper.find('.t-admin-sidebar').attributes('data-mode')).toBe('vertical-mix')
    // First-level entries still rendered
    expect(wrapper.find('[data-key="identity"]').exists()).toBe(true)
    // But child entries must not appear as options in the stub
    expect(wrapper.find('[data-key="identity.users"]').exists()).toBe(false)
  })

  it('renders header and footer slots', () => {
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    const wrapper = mountSidebar(
      {},
      {
        header: '<div class="slot-header">HEAD</div>',
        footer: '<div class="slot-footer">FOOT</div>',
      },
    )
    expect(wrapper.find('.slot-header').exists()).toBe(true)
    expect(wrapper.find('.slot-footer').exists()).toBe(true)
  })
})
