import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TAdminShell from '../../../src/components/layout/TAdminShell.vue'
import TAdminSidebar from '../../../src/components/layout/TAdminSidebar.vue'
import TAdminHeader from '../../../src/components/layout/TAdminHeader.vue'
import {
  useAdminRouteStore,
  type AdminRouteRecord,
  type AdminMenuItem,
} from '../../../src/stores/useAdminRouteStore'
import { useAdminAppStore } from '../../../src/stores/useAdminAppStore'
import { useAdminTabStore } from '../../../src/stores/useAdminTabStore'

function seedRoutes(): AdminRouteRecord[] {
  return [
    { name: 'home', path: '/', meta: { title: 'Home', order: 1 } },
    { name: 'users', path: '/users', meta: { title: 'Users', order: 2 } },
  ]
}

function seedHomeTab(): void {
  const tabStore = useAdminTabStore()
  tabStore.homeTab = {
    id: 'home',
    title: 'Home',
    fullPath: '/',
    meta: { keepAlive: false, fixedIndexInTab: 0 },
  }
}

const naiveStubs = {
  Menu: {
    props: ['options', 'collapsed', 'value'],
    emits: ['update:value'],
    template:
      '<ul class="n-menu-stub">' +
      '<li v-for="o in options" :key="o.key" :data-key="o.key" @click="$emit(\'update:value\', o.key)">{{ o.label }}</li>' +
      '</ul>',
  },
  Breadcrumb: { template: '<div class="n-breadcrumb-stub"><slot /></div>' },
  BreadcrumbItem: { template: '<span class="n-breadcrumb-item-stub"><slot /></span>' },
  Dropdown: { template: '<div class="n-dropdown-stub" />' },
  VueDraggable: {
    props: ['modelValue'],
    template: '<div class="vue-draggable-stub"><slot /></div>',
  },
}

function mountShell(props: Record<string, unknown> = {}) {
  return mount(TAdminShell, {
    props,
    global: {
      stubs: naiveStubs,
    },
    attachTo: document.body,
  })
}

function forceMobile(mobile: boolean): void {
  const app = useAdminAppStore()
  Object.defineProperty(app, 'isMobile', {
    get: () => mobile,
    configurable: true,
  })
}

describe('TAdminShell', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    const routeStore = useAdminRouteStore()
    routeStore.setConstantRoutes(seedRoutes())
    seedHomeTab()
  })

  it('renders vertical mode: sider + header + tabs + content + footer', () => {
    forceMobile(false)
    const wrapper = mountShell({ mode: 'vertical' })
    expect(wrapper.find('.t-admin-shell').exists()).toBe(true)
    expect(wrapper.find('.t-admin-sidebar').exists()).toBe(true)
    expect(wrapper.find('.t-admin-header').exists()).toBe(true)
    expect(wrapper.find('.t-admin-tabs').exists()).toBe(true)
    expect(wrapper.find('.t-admin-content').exists()).toBe(true)
    expect(wrapper.find('.t-admin-footer').exists()).toBe(true)
    expect(wrapper.attributes('data-mode')).toBe('vertical')
  })

  it('renders vertical-mix mode with two sidebars', () => {
    forceMobile(false)
    const wrapper = mountShell({ mode: 'vertical-mix' })
    const sidebars = wrapper.findAllComponents(TAdminSidebar)
    expect(sidebars.length).toBe(2)
    expect(wrapper.attributes('data-mode')).toBe('vertical-mix')
  })

  it('renders horizontal mode without main sider', () => {
    forceMobile(false)
    const wrapper = mountShell({ mode: 'horizontal' })
    expect(wrapper.find('.t-admin-shell__sider').exists()).toBe(false)
    expect(wrapper.find('.t-admin-header').exists()).toBe(true)
    expect(wrapper.attributes('data-mode')).toBe('horizontal')
  })

  it('hides tabs when tabs.visible is false', () => {
    forceMobile(false)
    const wrapper = mountShell({ tabs: { visible: false } })
    expect(wrapper.find('.t-admin-tabs').exists()).toBe(false)
  })

  it('hides footer when footer.visible is false', () => {
    forceMobile(false)
    const wrapper = mountShell({ footer: { visible: false } })
    expect(wrapper.find('.t-admin-footer').exists()).toBe(false)
  })

  it('renders mobile drawer via Teleport when appStore.isMobile', async () => {
    forceMobile(true)
    const app = useAdminAppStore()
    app.setSiderCollapse(false)
    const wrapper = mountShell({ mode: 'vertical-mix' })
    // Main sider should not render when mobile
    expect(wrapper.find('.t-admin-shell__sider').exists()).toBe(false)
    // Drawer attached to body via Teleport
    const drawer = document.body.querySelector('.t-admin-shell__drawer')
    expect(drawer).not.toBeNull()
    const panel = drawer as HTMLElement | null
    expect(panel?.style.width).toBe('260px')
    wrapper.unmount()
  })

  it('mobile mode respects sider.visible=false (no drawer rendered)', async () => {
    forceMobile(true)
    const wrapper = mountShell({ mode: 'vertical', sider: { visible: false } })
    expect(document.body.querySelector('.t-admin-shell__drawer')).toBeNull()
    wrapper.unmount()
  })

  it('forwards openSearch from header', async () => {
    forceMobile(false)
    const wrapper = mountShell()
    const header = wrapper.findComponent(TAdminHeader)
    header.vm.$emit('openSearch')
    expect(wrapper.emitted('openSearch')).toBeTruthy()
  })

  it('forwards openThemeDrawer from header', async () => {
    forceMobile(false)
    const wrapper = mountShell()
    const header = wrapper.findComponent(TAdminHeader)
    header.vm.$emit('openThemeDrawer')
    expect(wrapper.emitted('openThemeDrawer')).toBeTruthy()
  })

  it('forwards menuSelect from sidebar', async () => {
    forceMobile(false)
    const wrapper = mountShell()
    const sidebar = wrapper.findComponent(TAdminSidebar)
    const payload: AdminMenuItem = { key: 'users', label: 'Users' }
    sidebar.vm.$emit('menuSelect', payload)
    expect(wrapper.emitted('menuSelect')).toBeTruthy()
    expect(wrapper.emitted('menuSelect')![0]).toEqual([payload])
  })
})
