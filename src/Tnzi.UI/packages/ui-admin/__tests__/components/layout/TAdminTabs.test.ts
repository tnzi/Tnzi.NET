import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import TAdminTabs from '../../../src/components/layout/TAdminTabs.vue'
import { useAdminTabStore } from '../../../src/stores/useAdminTabStore'

const draggableStub = {
  name: 'VueDraggable',
  props: ['modelValue'],
  emits: ['update:modelValue'],
  template: '<div class="draggable-stub"><slot /></div>',
}

// Naive UI components are registered internally without the N prefix; stub by base name.
const dropdownStub = {
  name: 'Dropdown',
  props: ['options', 'show', 'x', 'y'],
  emits: ['select', 'clickoutside'],
  template: '<div class="n-dropdown-stub"></div>',
}

function seed() {
  const store = useAdminTabStore()
  store.homeTab = { id: 'home', title: 'Home', fullPath: '/', meta: { keepAlive: false } }
  store.tabs = [
    { id: 'users', title: 'Users', fullPath: '/users', meta: { keepAlive: true } },
    { id: 'roles', title: 'Roles', fullPath: '/roles', meta: { keepAlive: true } },
  ]
  store.activeTabId = 'users'
  return store
}

describe('TAdminTabs', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('renders home tab + store tabs in order', () => {
    seed()
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    const tabs = wrapper.findAll('.t-admin-tabs__tab')
    expect(tabs).toHaveLength(3)
    expect(tabs[0].text()).toContain('Home')
    expect(tabs[1].text()).toContain('Users')
    expect(tabs[2].text()).toContain('Roles')
  })

  it('highlights the active tab', () => {
    seed()
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    const tabs = wrapper.findAll('.t-admin-tabs__tab')
    expect(tabs[1].classes()).toContain('t-admin-tabs__tab--active')
    expect(tabs[0].classes()).not.toContain('t-admin-tabs__tab--active')
  })

  it('left click on a tab calls store.switchRouteByTab and emits tabClick', async () => {
    const store = seed()
    let called: unknown = null
    store.switchRouteByTab = (tab) => {
      called = tab
    }
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    await wrapper.findAll('.t-admin-tabs__tab')[2].trigger('click')
    expect(called).toMatchObject({ id: 'roles' })
    expect(wrapper.emitted('tabClick')).toBeTruthy()
  })

  it('middle-click closes a non-home tab when closeByMiddleClick=true', async () => {
    const store = seed()
    let removed: string | null = null
    store.removeTab = (id: string) => {
      removed = id
      return null
    }
    const wrapper = mount(TAdminTabs, {
      props: { closeByMiddleClick: true },
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    const usersTab = wrapper.findAll('.t-admin-tabs__tab')[1]
    await usersTab.trigger('mousedown', { button: 1 })
    expect(removed).toBe('users')
  })

  it('middle-click is a no-op when closeByMiddleClick=false', async () => {
    const store = seed()
    let removed: string | null = null
    store.removeTab = (id: string) => {
      removed = id
      return null
    }
    const wrapper = mount(TAdminTabs, {
      props: { closeByMiddleClick: false },
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    await wrapper.findAll('.t-admin-tabs__tab')[1].trigger('mousedown', { button: 1 })
    expect(removed).toBeNull()
  })

  it('middle-click never closes the home tab', async () => {
    const store = seed()
    let removed: string | null = null
    store.removeTab = (id: string) => {
      removed = id
      return null
    }
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    await wrapper.findAll('.t-admin-tabs__tab')[0].trigger('mousedown', { button: 1 })
    expect(removed).toBeNull()
  })

  it('close button on a non-home tab calls store.removeTab', async () => {
    const store = seed()
    let removed: string | null = null
    store.removeTab = (id: string) => {
      removed = id
      return null
    }
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    const closeBtns = wrapper.findAll('.t-admin-tabs__close')
    // Home tab has no close button, so 2 buttons total
    expect(closeBtns).toHaveLength(2)
    await closeBtns[0].trigger('click')
    expect(removed).toBe('users')
  })

  it('home tab has no close button', () => {
    seed()
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    const homeTab = wrapper.findAll('.t-admin-tabs__tab')[0]
    expect(homeTab.find('.t-admin-tabs__close').exists()).toBe(false)
  })

  it('right-click shows context menu with the tab as target', async () => {
    seed()
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    await wrapper
      .findAll('.t-admin-tabs__tab')[1]
      .trigger('contextmenu', { clientX: 100, clientY: 50 })
    const state = wrapper.vm as unknown as {
      contextTarget: { id: string } | null
      contextVisible: boolean
    }
    expect(state.contextTarget?.id).toBe('users')
    expect(state.contextVisible).toBe(true)
  })

  it('context menu action "close-others" calls store.removeOtherTabs', async () => {
    const store = seed()
    let otherOf: string | null = null
    store.removeOtherTabs = (id: string) => {
      otherOf = id
    }
    const wrapper = mount(TAdminTabs, {
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    await wrapper
      .findAll('.t-admin-tabs__tab')[1]
      .trigger('contextmenu', { clientX: 100, clientY: 50 })
    ;(wrapper.vm as unknown as { onContextSelect: (k: string) => void }).onContextSelect(
      'close-others',
    )
    expect(otherOf).toBe('users')
  })

  it('reload button triggers appStore.reloadPage', async () => {
    seed()
    const { useAdminAppStore } = await import('../../../src/stores/useAdminAppStore')
    const app = useAdminAppStore()
    let called = false
    app.reloadPage = async () => {
      called = true
    }
    const wrapper = mount(TAdminTabs, {
      props: { showReload: true },
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    await wrapper.find('.t-admin-tabs__reload').trigger('click')
    expect(called).toBe(true)
  })

  it('respects draggable=false by rendering tabs without VueDraggable wrapper', () => {
    seed()
    const wrapper = mount(TAdminTabs, {
      props: { draggable: false },
      global: { stubs: { VueDraggable: draggableStub, Dropdown: dropdownStub } },
    })
    expect(wrapper.find('.draggable-stub').exists()).toBe(false)
    expect(wrapper.findAll('.t-admin-tabs__tab')).toHaveLength(3)
    // Ensure no draggable-specific attrs fall through to the plain <div> wrapper
    const list = wrapper.find('.t-admin-tabs__draggable')
    expect(list.exists()).toBe(true)
    expect(list.attributes('handle')).toBeUndefined()
    expect(list.attributes('animation')).toBeUndefined()
    expect(list.attributes('model-value')).toBeUndefined()
  })
})
