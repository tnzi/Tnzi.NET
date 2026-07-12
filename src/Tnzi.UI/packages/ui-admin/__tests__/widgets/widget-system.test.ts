import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'

import TWidgetCard from '../../src/widgets/shell/TWidgetCard.vue'
import TWorkbenchLayout from '../../src/components/pages/TWorkbenchLayout.vue'
import TWidgetQuickActions from '../../src/widgets/builtin/TWidgetQuickActions.vue'
import TWidgetList from '../../src/widgets/builtin/TWidgetList.vue'
import TKpiCard from '../../src/components/data/TKpiCard.vue'
import { useWidget } from '../../src/widgets/shell/useWidget'
import { useWidgetData } from '../../src/widgets/shell/useWidgetData'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'
import { useAdminRouteStore } from '../../src/stores/useAdminRouteStore'
import {
  defaultKpiCards,
  defaultQuickActions,
  defaultTimelineItems,
  defaultWorkbenchWidgets,
} from '../../src/widgets/presets'
import type { WidgetDef } from '../../src/widgets/types'

function themeProvide() {
  const ctx = createThemeContext(mergeThemeSettings({}))
  return { [THEME_CONTEXT_KEY as unknown as symbol]: ctx }
}

function mockRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/admin/:catchAll(.*)*', component: { template: '<div />' } }],
  })
}

const Probe = defineComponent({
  name: 'WidgetProbe',
  setup() {
    const w = useWidget()
    return () => h('div', { class: 'probe' }, w.id)
  },
})

describe('TWidgetCard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    if (typeof window !== 'undefined') window.localStorage.clear()
  })

  it('renders title and provides widget context to children', () => {
    const wrapper = mount(TWidgetCard, {
      props: { id: 'demo', title: 'Demo' },
      slots: { default: () => h(Probe) },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-widget-card__title').text()).toBe('Demo')
    expect(wrapper.find('.probe').text()).toBe('demo')
  })

  it('emits refresh when the toolbar button is clicked', async () => {
    const wrapper = mount(TWidgetCard, {
      props: { id: 'r', title: 'R' },
      slots: { default: () => h('div', 'body') },
      global: { provide: themeProvide() },
    })
    const btn = wrapper.find('button')
    expect(btn.exists()).toBe(true)
    await btn.trigger('click')
    expect(wrapper.emitted('refresh')?.length).toBe(1)
  })

  it('hides chrome in bare mode', () => {
    const wrapper = mount(TWidgetCard, {
      props: { id: 'b', bare: true },
      slots: { default: () => h('div', { class: 'inner' }, 'x') },
      global: { provide: themeProvide() },
    })
    expect(wrapper.find('.t-widget-card--bare').exists()).toBe(true)
    expect(wrapper.find('.inner').text()).toBe('x')
  })

  it('fires onRefresh-registered callbacks when refresh button is clicked', async () => {
    let called = 0
    const Listener = defineComponent({
      setup() {
        const w = useWidget()
        w.onRefresh(() => {
          called += 1
        })
        return () => h('div')
      },
    })
    const wrapper = mount(TWidgetCard, {
      props: { id: 'l' },
      slots: { default: () => h(Listener) },
      global: { provide: themeProvide() },
    })
    await wrapper.find('button').trigger('click')
    await nextTick()
    expect(called).toBe(1)
  })
})

describe('useWidgetData', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('calls loader on mount and reloads on refresh', async () => {
    let loadCount = 0
    const Widget = defineComponent({
      setup() {
        useWidgetData(async () => {
          loadCount += 1
        })
        return () => h('div', 'widget')
      },
    })
    const wrapper = mount(TWidgetCard, {
      props: { id: 'data' },
      slots: { default: () => h(Widget) },
      global: { provide: themeProvide() },
    })
    // useWidgetData's onMounted callback fires void run() which is async;
    // wait two microtask flushes so both the click handler and the loader
    // resolve before we read the counter.
    await new Promise<void>((r) => setTimeout(r, 5))
    await nextTick()
    expect(loadCount).toBe(1)
    await wrapper.find('button').trigger('click')
    await new Promise<void>((r) => setTimeout(r, 5))
    await nextTick()
    expect(loadCount).toBe(2)
  })

  it('reports loader errors via the widget context', async () => {
    const Widget = defineComponent({
      setup() {
        useWidgetData(async () => {
          throw new Error('boom')
        })
        return () => h('div')
      },
    })
    const wrapper = mount(TWidgetCard, {
      props: { id: 'e' },
      slots: { default: () => h(Widget) },
      global: { provide: themeProvide() },
    })
    // Wait a microtask + one tick for the async loader.
    await new Promise<void>((r) => setTimeout(r, 5))
    await nextTick()
    expect(wrapper.text()).toContain('boom')
  })
})

describe('TWorkbenchLayout', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    if (typeof window !== 'undefined') window.localStorage.clear()
  })

  it('renders every supplied widget in fixed layout', () => {
    const widgets: WidgetDef[] = [
      { id: 'a', component: { template: '<div class="a">A</div>' }, span: 12 },
      { id: 'b', component: { template: '<div class="b">B</div>' }, span: 12 },
    ]
    const router = mockRouter()
    const wrapper = mount(TWorkbenchLayout, {
      props: { widgets },
      global: { plugins: [router], provide: themeProvide() },
    })
    expect(wrapper.find('.a').exists()).toBe(true)
    expect(wrapper.find('.b').exists()).toBe(true)
    expect(wrapper.findAll('.t-widget-card').length).toBe(2)
  })

  it('filters widgets by permission callback', () => {
    const widgets: WidgetDef[] = [
      { id: 'allowed', component: { template: '<div class="x">X</div>' }, permission: 'ok' },
      { id: 'denied', component: { template: '<div class="y">Y</div>' }, permission: 'nope' },
    ]
    const router = mockRouter()
    const wrapper = mount(TWorkbenchLayout, {
      props: { widgets, hasPermission: (k: string) => k === 'ok' },
      global: { plugins: [router], provide: themeProvide() },
    })
    expect(wrapper.find('.x').exists()).toBe(true)
    expect(wrapper.find('.y').exists()).toBe(false)
  })

  it('honors bare flag and omits the card chrome', () => {
    const widgets: WidgetDef[] = [
      { id: 'bare', component: { template: '<div class="raw">raw</div>' }, bare: true, span: 24 },
    ]
    const router = mockRouter()
    const wrapper = mount(TWorkbenchLayout, {
      props: { widgets },
      global: { plugins: [router], provide: themeProvide() },
    })
    expect(wrapper.find('.t-widget-card--bare').exists()).toBe(true)
    expect(wrapper.find('.raw').exists()).toBe(true)
  })
})

describe('TWidgetQuickActions permission filtering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    if (typeof window !== 'undefined') window.localStorage.clear()
  })

  const actions = [
    { key: 'open', icon: 'mdi:folder', label: 'Open', to: '/admin/a', permission: 'a.view' },
    { key: 'settings', icon: 'mdi:cog', label: 'Settings', to: '/admin/s', permission: 'system.parameter.view' },
    { key: 'always', icon: 'mdi:star', label: 'Always', to: '/admin/x' },
  ]

  function mountActions() {
    return mount(TWidgetQuickActions, {
      props: { actions },
      global: { plugins: [mockRouter()], provide: themeProvide() },
    })
  }

  it('hides tiles whose permission the signed-in user lacks', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: '1', username: 'biz', roles: [], permissions: ['a.view'] })
    const wrapper = mountActions()
    const tiles = wrapper.findAll('.t-widget-quick-actions__tile')
    // 'a.view' granted + the permission-less 'always' tile; Settings hidden.
    expect(tiles.length).toBe(2)
    expect(wrapper.text()).toContain('Open')
    expect(wrapper.text()).toContain('Always')
    expect(wrapper.text()).not.toContain('Settings')
  })

  it('fails open (shows every tile) before permissions load', () => {
    // userInfo === null — permissions not yet loaded, so don't hide anything.
    const wrapper = mountActions()
    expect(wrapper.findAll('.t-widget-quick-actions__tile').length).toBe(3)
  })

  it('shows every tile for a super user regardless of permissions', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: '1', username: 'root', roles: [], permissions: [] })
    auth.setSuperUser(true)
    const wrapper = mountActions()
    expect(wrapper.findAll('.t-widget-quick-actions__tile').length).toBe(3)
  })
})

describe('TWidgetQuickActions module filtering', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    if (typeof window !== 'undefined') window.localStorage.clear()
  })

  const actions = [
    { key: 'chat', icon: 'mdi:forum', label: 'Chat', to: '/admin/chat', module: 'chat' },
    { key: 'users', icon: 'mdi:account', label: 'Users', to: '/admin/identity/users', module: 'identity' },
    { key: 'always', icon: 'mdi:star', label: 'Always', to: '/admin/x' },
  ]

  function mountActions() {
    return mount(TWidgetQuickActions, {
      props: { actions },
      global: { plugins: [mockRouter()], provide: themeProvide() },
    })
  }

  it('hides tiles whose module the backend did not load — even for super users', () => {
    const auth = useAdminAuthStore()
    auth.setUserInfo({ id: '1', username: 'root', roles: [], permissions: [] })
    auth.setSuperUser(true)
    useAdminRouteStore().setAvailableModules(new Set(['identity']))
    const wrapper = mountActions()
    const tiles = wrapper.findAll('.t-widget-quick-actions__tile')
    expect(tiles.length).toBe(2)
    expect(wrapper.text()).toContain('Users')
    expect(wrapper.text()).toContain('Always')
    expect(wrapper.text()).not.toContain('Chat')
  })

  it('fails open (shows every tile) while the module signal is unavailable', () => {
    const wrapper = mountActions()
    expect(wrapper.findAll('.t-widget-quick-actions__tile').length).toBe(3)
  })
})

describe('TWidgetList', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    if (typeof window !== 'undefined') window.localStorage.clear()
  })

  const rows = Array.from({ length: 7 }, (_, i) => ({ id: i + 1, name: `Row ${i + 1}` }))

  function mountList(props: Record<string, unknown> = {}) {
    return mount(TWidgetList, {
      props: { items: rows, pageSize: 3, ...props },
      slots: { row: ({ item }: { item: { name: string } }) => h('span', { class: 'row-name' }, item.name) },
      global: { plugins: [mockRouter()], provide: themeProvide() },
    })
  }

  it('paginates to pageSize rows and shows a pager for the overflow', () => {
    const wrapper = mountList()
    expect(wrapper.findAll('.t-widget-list__row').length).toBe(3)
    expect(wrapper.find('.t-widget-list__pager').exists()).toBe(true)
    // 7 items / pageSize 3 → 3 pages.
    expect(wrapper.find('.t-widget-list__pager-info').text()).toBe('1 / 3')
  })

  it('advances the page and re-slices the rows', async () => {
    const wrapper = mountList()
    const next = wrapper.findAll('.t-widget-list__pager-btn')[1]
    await next.trigger('click')
    expect(wrapper.find('.t-widget-list__pager-info').text()).toBe('2 / 3')
    expect(wrapper.findAll('.row-name')[0].text()).toBe('Row 4')
  })

  it('emits row-click with the item and its absolute index', async () => {
    const wrapper = mountList()
    await wrapper.findAll('.t-widget-list__row')[1].trigger('click')
    expect(wrapper.emitted('row-click')?.[0]).toEqual([rows[1], 1])
  })

  it('emits link when the header link is clicked', async () => {
    const wrapper = mountList({ title: 'Files', linkText: 'View all' })
    await wrapper.find('.t-widget-list__link').trigger('click')
    expect(wrapper.emitted('link')?.length).toBe(1)
  })

  it('renders the empty state when there are no items', () => {
    const wrapper = mountList({ items: [], emptyText: 'Nothing here' })
    expect(wrapper.find('.t-widget-list__row').exists()).toBe(false)
    expect(wrapper.text()).toContain('Nothing here')
  })

  it('bare mode drops the NCard chrome so it nests inside a TWidgetCard', () => {
    const wrapper = mountList({ bare: true, title: 'Ignored' })
    expect(wrapper.find('.t-widget-list--bare').exists()).toBe(true)
    // No card header rendered in bare mode.
    expect(wrapper.find('.t-widget-list__head').exists()).toBe(false)
  })
})

describe('TKpiCard clickable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  function mountCard(props: Record<string, unknown>) {
    return mount(TKpiCard, {
      props: { label: 'Files', value: 12, ...props },
      global: { plugins: [mockRouter()], provide: themeProvide() },
    })
  }

  it('is not interactive by default', () => {
    const wrapper = mountCard({})
    expect(wrapper.find('.t-stat-card--clickable').exists()).toBe(false)
  })

  it('becomes a button and emits click when `to` is set', async () => {
    const wrapper = mountCard({ to: '/admin/matters' })
    const card = wrapper.find('.t-stat-card--clickable')
    expect(card.exists()).toBe(true)
    expect(card.attributes('role')).toBe('button')
    await card.trigger('click')
    expect(wrapper.emitted('click')?.length).toBe(1)
  })

  it('is interactive via the `interactive` flag without a router target', () => {
    const wrapper = mountCard({ interactive: true })
    expect(wrapper.find('.t-stat-card--clickable').exists()).toBe(true)
  })
})

describe('preset helpers', () => {
  it('defaultKpiCards returns 4 gradient cards', () => {
    const kpis = defaultKpiCards()
    expect(kpis.length).toBe(4)
    expect(kpis.every((k) => !!k.gradient)).toBe(true)
  })

  it('defaultQuickActions returns 4 router-pinned actions', () => {
    const actions = defaultQuickActions()
    expect(actions.length).toBe(4)
    expect(actions.every((a) => !!a.to)).toBe(true)
  })

  it('defaultTimelineItems returns 4 items', () => {
    expect(defaultTimelineItems().length).toBe(4)
  })

  it('defaultWorkbenchWidgets leads with the KPI hero strip', () => {
    const deck = defaultWorkbenchWidgets()
    expect(deck.length).toBeGreaterThan(5)
    // KPI strip pinned to row 1 per UX feedback — users see the headline
    // numbers immediately, then quick actions and detail cards.
    expect(deck[0]?.id).toBe('kpi')
    expect(deck.find((w) => w.id === 'banner')).toBeUndefined()
  })

  it('every default widget has a unique id', () => {
    const ids = defaultWorkbenchWidgets().map((w) => w.id)
    expect(new Set(ids).size).toBe(ids.length)
  })

  it('module-coupled default widgets carry a module tag', () => {
    const deck = defaultWorkbenchWidgets()
    const byId = new Map(deck.map((w) => [w.id, w]))
    expect(byId.get('chat-stats')?.module).toBe('chat')
    expect(byId.get('identity-stats')?.module).toBe('identity')
    expect(byId.get('ai-usage')?.module).toBe('ai')
    expect(byId.get('storage-usage')?.module).toBe('storage')
    expect(byId.get('notification-stats')?.module).toBe('notification')
    expect(byId.get('audit-recent')?.module).toBe('audit')
    expect(byId.get('activity')?.module).toBe('audit')
    // Module-agnostic tiles stay untagged.
    expect(byId.get('quick-actions')?.module).toBeUndefined()
  })

  it('no longer ships the retired tips widget', () => {
    expect(defaultWorkbenchWidgets().some((w) => w.id === 'tips')).toBe(false)
  })

  it('every default quick action carries a module tag', () => {
    const actions = defaultQuickActions()
    expect(actions.every((a) => !!a.module)).toBe(true)
  })
})
