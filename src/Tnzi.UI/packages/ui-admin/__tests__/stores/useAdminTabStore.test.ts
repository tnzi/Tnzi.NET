import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminTabStore, isMultiInstanceRoute, multiInstanceKey } from '../../src/stores/useAdminTabStore'

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

  // ── pruneTabs: drop tabs the current user can no longer open ────────────────
  it('pruneTabs removes tabs whose route name is denied and re-points active', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('users', 'Users'))
    store.addTab(sampleRoute('system.diagnostics', 'Diagnostics'))
    store.addTab(sampleRoute('system.dictionaries', 'Dictionaries'))
    expect(store.activeTabId).toBe('system.dictionaries')
    store.pruneTabs(new Set(['system.diagnostics', 'system.dictionaries']))
    expect(store.tabs.map((t) => t.id)).toEqual(['users'])
    // active pointed at a pruned tab → re-pointed to a survivor
    expect(store.activeTabId).toBe('users')
  })

  it('pruneTabs is a no-op for an empty deny set', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('users', 'Users'))
    store.pruneTabs(new Set())
    expect(store.tabs).toHaveLength(1)
    expect(store.activeTabId).toBe('users')
  })

  it('pruneTabs keeps the active pointer when the active tab survives', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('users', 'Users'))
    store.addTab(sampleRoute('secret', 'Secret'))
    store.setActiveTab('users')
    store.pruneTabs(new Set(['secret']))
    expect(store.tabs.map((t) => t.id)).toEqual(['users'])
    expect(store.activeTabId).toBe('users')
  })

  it('pruneTabs also drops a pinned tab that is now unauthorized', () => {
    const store = useAdminTabStore()
    store.addTab(sampleRoute('secret', 'Secret'))
    store.fixTab('secret')
    expect(store.isTabPinned('secret')).toBe(true)
    store.pruneTabs(new Set(['secret']))
    expect(store.tabs).toHaveLength(0)
    expect(store.fixedTabIds).not.toContain('secret')
  })

  // ── Multi-instance (detail / deep-link) routes ──────────────────────────────
  // `path` mirrors vue-router: the RESOLVED pathname (params filled, query
  // stripped); `fullPath` carries the query.
  const detailRoute = (id: string, section?: string) => ({
    name: 'ai.agents.detail',
    path: `/admin/ai/agents/${id}`,
    fullPath: `/admin/ai/agents/${id}${section ? `?section=${section}` : ''}`,
    meta: { title: 'Agent Detail' },
    query: section ? { section } : {},
    params: { id },
  })

  it('opens a separate tab per record for param-based detail routes', () => {
    const store = useAdminTabStore()
    store.addTab(detailRoute('A'))
    store.addTab(detailRoute('B'))
    // Two distinct tabs (A and B), keyed by the resolved path — not collapsed
    // onto the shared route name 'ai.agents.detail'.
    expect(store.tabs).toHaveLength(2)
    expect(store.tabs.map((t) => t.id)).toEqual([
      '/admin/ai/agents/A',
      '/admin/ai/agents/B',
    ])
    expect(store.activeTabId).toBe('/admin/ai/agents/B')
  })

  it('revisiting the same record reuses its tab', () => {
    const store = useAdminTabStore()
    store.addTab(detailRoute('A'))
    store.addTab(detailRoute('A'))
    expect(store.tabs).toHaveLength(1)
  })

  it('does NOT spawn a new tab when a param route changes only its query (?section=)', () => {
    const store = useAdminTabStore()
    store.addTab(detailRoute('A'))
    store.addTab(detailRoute('A', 'versions')) // user switched detail section
    // Still ONE tab (keyed by path, query-agnostic)…
    expect(store.tabs).toHaveLength(1)
    expect(store.tabs[0].id).toBe('/admin/ai/agents/A')
    // …but its stored location is refreshed to the latest url.
    expect(store.tabs[0].fullPath).toBe('/admin/ai/agents/A?section=versions')
  })

  it('keeps a dynamic tab title across query-only re-navigation', () => {
    const store = useAdminTabStore()
    store.addTab(detailRoute('A'))
    store.updateTabTitle('/admin/ai/agents/A', 'Foo') // useTabTitle wrote the record name
    store.addTab(detailRoute('A', 'versions'))
    expect(store.tabs[0].title).toBe('Foo') // not clobbered back to 'Agent Detail'
  })

  it('splits multiTab routes by query string', () => {
    const store = useAdminTabStore()
    const report = (type: string) => ({
      name: 'reports',
      fullPath: `/reports?type=${type}`,
      path: '/reports',
      meta: { title: 'Reports', multiTab: true },
      query: { type },
      params: {},
    })
    store.addTab(report('sales'))
    store.addTab(report('returns'))
    expect(store.tabs).toHaveLength(2)
    expect(store.tabs.map((t) => t.id)).toEqual([
      '/reports?type=sales',
      '/reports?type=returns',
    ])
  })

  it('isMultiInstanceRoute detects params and multiTab+query', () => {
    expect(isMultiInstanceRoute({ params: { id: 'A' }, query: {}, meta: {} })).toBe(true)
    expect(isMultiInstanceRoute({ params: {}, query: { type: 'x' }, meta: { multiTab: true } })).toBe(true)
    // query without the multiTab flag stays single-instance
    expect(isMultiInstanceRoute({ params: {}, query: { page: '2' }, meta: {} })).toBe(false)
    expect(isMultiInstanceRoute({ params: {}, query: {}, meta: {} })).toBe(false)
  })

  it('multiInstanceKey: param routes key by path, multiTab+query by fullPath', () => {
    // param route → path (query-agnostic, so ?section= doesn't fork the tab)
    expect(
      multiInstanceKey({ path: '/admin/ai/agents/A', fullPath: '/admin/ai/agents/A?section=x', params: { id: 'A' } }),
    ).toBe('/admin/ai/agents/A')
    // query-only multiTab route → fullPath (the query is the differentiator)
    expect(
      multiInstanceKey({ path: '/reports', fullPath: '/reports?type=sales', params: {} }),
    ).toBe('/reports?type=sales')
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
