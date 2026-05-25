import { describe, it, expect, beforeEach } from 'vitest'
import { defineComponent, h, nextTick } from 'vue'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter } from 'vue-router'
import { THEME_CONTEXT_KEY, createThemeContext, mergeThemeSettings } from '@tnzi/ui'

import TWidgetCard from '../../src/widgets/shell/TWidgetCard.vue'
import TWorkbenchLayout from '../../src/components/pages/TWorkbenchLayout.vue'
import { useWidget } from '../../src/widgets/shell/useWidget'
import { useWidgetData } from '../../src/widgets/shell/useWidgetData'
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
})
