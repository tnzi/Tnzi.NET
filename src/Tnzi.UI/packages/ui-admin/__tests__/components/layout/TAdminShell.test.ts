import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TAdminShell from '../../../src/components/layout/TAdminShell.vue'
import TAdminSidebar from '../../../src/components/layout/TAdminSidebar.vue'
import TAdminHeader from '../../../src/components/layout/TAdminHeader.vue'
import TChatHost from '../../../src/components/chat/TChatHost.vue'
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
  Tooltip: {
    template: '<div class="n-tooltip-stub"><slot name="trigger" /><slot /></div>',
  },
  Modal: { template: '<div class="n-modal-stub"><slot /></div>' },
  Input: { template: '<input class="n-input-stub" />' },
  VueDraggable: {
    props: ['modelValue'],
    template: '<div class="vue-draggable-stub"><slot /></div>',
  },
}

function mountShell(props: Record<string, unknown> = {}) {
  return mount(TAdminShell, {
    props: {
      // Phase H2 B4: default-mounted TAdminUserAvatar uses useDialog()
      // which needs an NDialogProvider that the test harness doesn't
      // set up. Opt out for these layout-structure tests.
      builtinUserAvatar: false,
      // Phase H1 I1: same for TGlobalSearch (uses NModal). The keyboard-
      // shortcut behaviour is exercised by a dedicated test elsewhere
      // when we add one; here we want a minimal mount.
      builtinSearch: false,
      // Phase H4 L6: TBackTop wraps NBackTop which uses scroll
      // listeners — not needed for layout-structure assertions.
      builtinBackTop: false,
      ...props,
    },
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

  it('renders vertical-mix mode with TAdminMixNavRail (90px first-level rail) + sub-sider container', () => {
    forceMobile(false)
    const wrapper = mountShell({ mode: 'vertical-mix' })
    // Phase G follow-up #4: vertical-mix uses TAdminMixNavRail (custom
    // div-based rail ported from soybean's first-level-menu.vue) instead
    // of TAdminSidebar — NMenu can't render the icon-on-top + label-below
    // geometry cleanly. So no TAdminSidebar instance is rendered in this
    // mode. The second-level drawer still lives as `.t-admin-shell__sub-sider`.
    const sidebars = wrapper.findAllComponents(TAdminSidebar)
    expect(sidebars.length).toBe(0)
    expect(wrapper.find('.t-admin-mix-rail').exists()).toBe(true)
    expect(wrapper.find('.t-admin-shell__sub-sider').exists()).toBe(true)
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

  it('starts with the mobile drawer closed on a fresh mount even if siderCollapse was persisted open', () => {
    forceMobile(true)
    const app = useAdminAppStore()
    // Simulate a persisted desktop "expanded" value carried into a phone load.
    app.setSiderCollapse(false)
    const wrapper = mountShell({ mode: 'vertical' })
    // onMounted forces the drawer closed so it doesn't cover the content.
    expect(app.siderCollapse).toBe(true)
    wrapper.unmount()
  })

  it('closes the mobile drawer when a nav item is selected', () => {
    forceMobile(true)
    const app = useAdminAppStore()
    const wrapper = mountShell({ mode: 'vertical' })
    // Open the drawer, then select a menu item — it should auto-dismiss.
    app.setSiderCollapse(false)
    const sidebar = wrapper.findComponent(TAdminSidebar)
    sidebar.vm.$emit('menuSelect', { key: 'users', label: 'Users' } as AdminMenuItem)
    expect(app.siderCollapse).toBe(true)
    expect(wrapper.emitted('menuSelect')).toBeTruthy()
    wrapper.unmount()
  })

  it('passes TChatHost via the dedicated #chat slot on TAdminHeader (not via #user)', () => {
    // Regression: previously TChatHost was placed inside the #user slot default,
    // so consumers overriding #header-user (like AdminShellRoot) would hide it.
    // Now it lives in TAdminHeader's #chat slot which AdminShellRoot never overrides.
    forceMobile(false)
    const wrapper = mountShell({ mode: 'vertical' })
    const header = wrapper.findComponent(TAdminHeader)
    // TChatHost self-gates when no admin client is provided (test environment)
    // but the component node must still be rendered inside the header's chat region.
    const chatHost = header.findComponent(TChatHost)
    expect(chatHost.exists()).toBe(true)
    // The chat region must be inside the header's right section, not the #user wrapper.
    const chatContainer = header.find('.t-admin-header__chat')
    expect(chatContainer.exists()).toBe(true)
    expect(chatContainer.findComponent(TChatHost).exists()).toBe(true)
  })

  it('does not render TChatHost when builtinChat is false', () => {
    forceMobile(false)
    const wrapper = mountShell({ mode: 'vertical', builtinChat: false })
    const header = wrapper.findComponent(TAdminHeader)
    expect(header.findComponent(TChatHost).exists()).toBe(false)
  })
})
