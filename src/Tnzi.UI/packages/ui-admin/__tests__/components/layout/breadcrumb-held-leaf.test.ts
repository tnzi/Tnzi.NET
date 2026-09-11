import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, h, ref } from 'vue'
import TAdminAutoBreadcrumb from '../../../src/components/layout/TAdminAutoBreadcrumb.vue'
import TAdminBreadcrumb from '../../../src/components/layout/TAdminBreadcrumb.vue'
import { useAdminBreadcrumbStore } from '../../../src/stores/useAdminBreadcrumbStore'
import {
  useBreadcrumbLabel,
  useBreadcrumbTrail,
  BREADCRUMB_PENDING_TIMEOUT_MS,
} from '../../../src/headless/use-breadcrumb'

/**
 * The held leaf.
 *
 * A detail route inherits its LIST's static title, so between "the page opened"
 * and "the record arrived" the breadcrumb used to read `Clients / Clients` and
 * then swap the second one for the person's name. These specs pin the three
 * pieces that replace that flash with a single loading -> record transition:
 * the store's pending flag, the composables that raise it, and the rendering
 * that turns it into a placeholder rather than a wrong name.
 */

const Blank = defineComponent({ render: () => h('div') })

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/admin',
        name: 'admin-root',
        component: Blank,
        children: [
          { path: 'clients', name: 'clients', component: Blank, meta: { title: 'Clients', icon: 'mdi:account-group' } },
          {
            path: 'clients/:id',
            name: 'clients.detail',
            component: Blank,
            meta: { title: 'Clients', hideInMenu: true, activeMenu: 'clients' },
          },
        ],
      },
    ],
  })
}

const stubs = {
  Breadcrumb: { template: '<ul class="nb"><slot /></ul>' },
  BreadcrumbItem: { template: '<li class="nbi"><slot /></li>' },
}

describe('useAdminBreadcrumbStore - pending leaf', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('holds and releases a key', () => {
    const store = useAdminBreadcrumbStore()
    expect(store.isPending('k')).toBe(false)
    store.markPending('k')
    expect(store.isPending('k')).toBe(true)
    store.resolvePending('k')
    expect(store.isPending('k')).toBe(false)
  })

  it('releases the hold as soon as a real label arrives', () => {
    const store = useAdminBreadcrumbStore()
    store.markPending('k')
    store.setLeafLabel('k', 'Jeremiah Gold')
    expect(store.isPending('k')).toBe(false)
  })

  it('releases the hold as soon as a real trail arrives', () => {
    const store = useAdminBreadcrumbStore()
    store.markPending('k')
    store.setTrail('k', [{ label: 'Clients' }])
    expect(store.isPending('k')).toBe(false)
  })

  it('drops the hold with the rest of the contribution', () => {
    const store = useAdminBreadcrumbStore()
    store.markPending('k')
    store.clear('k')
    expect(store.isPending('k')).toBe(false)
  })
})

describe('contributing composables - holding the leaf', () => {
  let pinia: Pinia
  let router: Router

  beforeEach(async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    router = makeRouter()
    await router.push('/admin/clients/7')
    await router.isReady()
  })

  it('holds the leaf while the record is still loading', async () => {
    const name = ref<string | null>(null)
    const Host = defineComponent({
      setup() {
        useBreadcrumbLabel(() => name.value)
        return () => h('div')
      },
    })
    mount(Host, { global: { plugins: [router, pinia] } })
    await flushPromises()
    const store = useAdminBreadcrumbStore()
    expect(store.isPending('/admin/clients/7')).toBe(true)

    name.value = 'Jeremiah Gold'
    await flushPromises()
    expect(store.isPending('/admin/clients/7')).toBe(false)
    expect(store.leafLabelFor('/admin/clients/7')).toBe('Jeremiah Gold')
  })

  it('holds the leaf for an empty trail too (empty is "not yet", not "nothing")', async () => {
    const Host = defineComponent({
      setup() {
        useBreadcrumbTrail(() => [])
        return () => h('div')
      },
    })
    mount(Host, { global: { plugins: [router, pinia] } })
    await flushPromises()
    expect(useAdminBreadcrumbStore().isPending('/admin/clients/7')).toBe(true)
  })

  it('gives up on a record that never arrives, rather than pulsing forever', async () => {
    vi.useFakeTimers()
    try {
      const Host = defineComponent({
        setup() {
          useBreadcrumbLabel(() => null)
          return () => h('div')
        },
      })
      mount(Host, { global: { plugins: [router, pinia] } })
      const store = useAdminBreadcrumbStore()
      expect(store.isPending('/admin/clients/7')).toBe(true)
      vi.advanceTimersByTime(BREADCRUMB_PENDING_TIMEOUT_MS + 1)
      expect(store.isPending('/admin/clients/7')).toBe(false)
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('TAdminAutoBreadcrumb - held leaf and contributed icons', () => {
  let pinia: Pinia
  let router: Router

  beforeEach(async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    router = makeRouter()
    await router.push('/admin/clients/7')
    await router.isReady()
  })

  it('renders the leaf as a placeholder instead of the list title it inherits', async () => {
    useAdminBreadcrumbStore().markPending('/admin/clients/7')
    const w = mount(TAdminAutoBreadcrumb, { global: { plugins: [router, pinia], stubs } })
    await flushPromises()
    const items = w.findComponent(TAdminBreadcrumb).props('items')
    const leaf = items[items.length - 1]
    expect(leaf.loading).toBe(true)
    // Dropped with the label: a bar you can click is a mis-click waiting to
    // happen, and it navigates to the page you are already on.
    expect(leaf.to).toBeUndefined()
  })

  it('gives a contributed trail the glyphs its route-derived twin had', async () => {
    useAdminBreadcrumbStore().setTrail('/admin/clients/7', [
      { label: 'Clients', to: '/admin/clients' },
      { label: 'Jeremiah Gold' },
    ])
    const w = mount(TAdminAutoBreadcrumb, { global: { plugins: [router, pinia], stubs } })
    await flushPromises()
    const items = w.findComponent(TAdminBreadcrumb).props('items')
    // Without this the glyphs vanish the instant the record lands, and every
    // label in the row shifts sideways.
    expect(items[0].icon).toBe('mdi:account-group')
    // The leaf is where you already are - no `to`, and so no glyph, which is
    // also what the route-derived walk produces for a detail route.
    expect(items[1].icon).toBeUndefined()
  })
})

describe('TAdminBreadcrumb - placeholder rendering', () => {
  it('renders a bar plus an accessible name, and never the pending label', () => {
    const w = mount(TAdminBreadcrumb, {
      props: {
        items: [
          { label: 'Clients', to: '/admin/clients' },
          { label: 'Clients', loading: true },
        ],
      },
      global: { stubs },
    })
    expect(w.find('.t-admin-breadcrumb__placeholder').exists()).toBe(true)
    // The bar itself is decorative; the name still reaches a screen reader.
    expect(w.find('.sr-only').text()).toBe('Clients')
    // The visible row must show one label, not two - the second "Clients" is
    // exactly the wrong name this exists to suppress.
    const visible = w.findAll('.nbi').map((n) => n.text())
    expect(visible[1]).not.toContain('Clients Clients')
  })

  it('renders the label normally once loading clears', () => {
    const w = mount(TAdminBreadcrumb, {
      props: { items: [{ label: 'Clients', to: '/admin/clients' }, { label: 'Jeremiah Gold' }] },
      global: { stubs },
    })
    expect(w.find('.t-admin-breadcrumb__placeholder').exists()).toBe(false)
    expect(w.findAll('.nbi')[1].text()).toBe('Jeremiah Gold')
  })
})
