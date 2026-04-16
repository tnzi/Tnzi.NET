import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminTabStore } from '../../src/stores/useAdminTabStore'

describe('useAdminTabStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  const sampleRoute = (name: string, title: string) => ({
    name,
    fullPath: `/${name}`,
    path: `/${name}`,
    meta: { title },
    query: {},
    params: {},
  })

  it('starts with empty tabs', () => {
    const store = useAdminTabStore()
    expect(store.tabs).toHaveLength(0)
    expect(store.activeTabId).toBe('')
  })

  it('addTab adds a tab and sets it active', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('users', 'Users'))
    expect(store.tabs).toHaveLength(1)
    expect(store.activeTabId).toBe('users')
  })

  it('addTab does not duplicate existing tabs', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('users', 'Users'))
    store.addTab(sampleRoute('users', 'Users'))
    expect(store.tabs).toHaveLength(1)
  })

  it('removeTab removes a tab and switches to neighbor', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.addTab(sampleRoute('c', 'C'))
    expect(store.activeTabId).toBe('c')
    store.removeTab('c')
    expect(store.tabs).toHaveLength(2)
    expect(store.activeTabId).toBe('b')
  })

  it('removeLeftTabs removes all tabs before the given id', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.addTab(sampleRoute('c', 'C'))
    store.removeLeftTabs('c')
    expect(store.tabs.map((t) => t.id)).toEqual(['c'])
  })

  it('removeRightTabs removes all tabs after the given id', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.addTab(sampleRoute('c', 'C'))
    store.removeRightTabs('a')
    expect(store.tabs.map((t) => t.id)).toEqual(['a'])
  })

  it('removeOtherTabs keeps only the given tab', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.addTab(sampleRoute('c', 'C'))
    store.removeOtherTabs('b')
    expect(store.tabs.map((t) => t.id)).toEqual(['b'])
  })

  it('clearAllTabs removes all non-fixed tabs', () => {
    const store = useAdminTabStore()
    store.setHomeTab({ id: 'home', title: 'Home', fullPath: '/', meta: { fixedIndexInTab: 0 } })
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.clearAllTabs()
    expect(store.tabs).toHaveLength(0)
    expect(store.homeTab?.id).toBe('home')
  })

  it('moveTab reorders tabs', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.addTab(sampleRoute('c', 'C'))
    store.moveTab(0, 2)
    expect(store.tabs.map((t) => t.id)).toEqual(['b', 'c', 'a'])
  })

  it('updateTabTitle updates a tab title', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'Old'))
    store.updateTabTitle('a', 'New')
    expect(store.tabs[0].title).toBe('New')
  })

  it('switchRouteByTab sets activeTabId to the target tab id', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('a', 'A'))
    store.addTab(sampleRoute('b', 'B'))
    store.setActiveTab('a')
    expect(store.activeTabId).toBe('a')
    store.switchRouteByTab({ id: 'b', title: 'B', fullPath: '/b' })
    expect(store.activeTabId).toBe('b')
  })
})
