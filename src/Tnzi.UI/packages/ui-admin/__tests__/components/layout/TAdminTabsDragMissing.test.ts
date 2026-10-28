import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'

/**
 * The drag library is not installed at all.
 *
 * `vue-draggable-plus` is an optional peer, and the tab bar drags by default.
 * Before the tab bar loaded it on demand, an app without the package did not
 * get a tab bar without dragging - it did not build. Now the tabs sit in a
 * plain container and everything except pointer dragging keeps working.
 */
vi.mock('vue-draggable-plus', () => {
  throw new Error("Cannot find module 'vue-draggable-plus'")
})

import TAdminTabs from '../../../src/components/layout/TAdminTabs.vue'
import { useAdminTabStore } from '../../../src/stores/useAdminTabStore'

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

describe('TAdminTabs when the drag library is not installed', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.spyOn(console, 'error').mockImplementation(() => {})
  })

  it('★ still renders every tab, in order', async () => {
    seed()
    const wrapper = mount(TAdminTabs, { global: { stubs: { Dropdown: dropdownStub } } })
    await flushPromises()

    const tabs = wrapper.findAll('.t-admin-tabs__tab')
    expect(tabs).toHaveLength(3)
    expect(tabs.map((tab) => tab.text())).toEqual([
      expect.stringContaining('Home'),
      expect.stringContaining('Users'),
      expect.stringContaining('Roles'),
    ])
  })

  it('keeps the list container class the layout hangs on', async () => {
    seed()
    const wrapper = mount(TAdminTabs, { global: { stubs: { Dropdown: dropdownStub } } })
    await flushPromises()

    expect(wrapper.find('.t-admin-tabs__draggable').exists()).toBe(true)
  })

  it('still switches tabs on click', async () => {
    const store = seed()
    let switched: unknown = null
    store.switchRouteByTab = (tab) => {
      switched = tab
    }
    const wrapper = mount(TAdminTabs, { global: { stubs: { Dropdown: dropdownStub } } })
    await flushPromises()

    await wrapper.findAll('.t-admin-tabs__tab')[2].trigger('click')
    expect(switched).toMatchObject({ id: 'roles' })
  })
})
