import { describe, it, expect, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, h, ref } from 'vue'
import {
  useBreadcrumbTrail,
  useBreadcrumbLabel,
  breadcrumbRouteKey,
} from '../../src/headless/useBreadcrumb'
import { useAdminBreadcrumbStore } from '../../src/stores/useAdminBreadcrumbStore'

function makeRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/matters/:id', name: 'm-detail', component: { template: '<div/>' } },
      { path: '/plain', name: 'plain', component: { template: '<div/>' } },
    ],
  })
}

describe('breadcrumbRouteKey', () => {
  it('keys a param (multi-instance) route by its path', () => {
    expect(
      breadcrumbRouteKey({ name: 'm-detail', path: '/matters/7', fullPath: '/matters/7?section=x', params: { id: '7' } }),
    ).toBe('/matters/7')
  })
  it('keys a plain route by its name', () => {
    expect(breadcrumbRouteKey({ name: 'plain', path: '/plain', fullPath: '/plain', params: {} })).toBe('plain')
  })
})

describe('useBreadcrumbLabel', () => {
  let pinia: Pinia
  let router: Router
  beforeEach(async () => {
    pinia = createPinia()
    setActivePinia(pinia)
    router = makeRouter()
    await router.push('/matters/7')
    await router.isReady()
  })

  it('writes the leaf label under the route key and tracks reactive changes', async () => {
    const name = ref<string | null>('Smith v. Jones')
    const Host = defineComponent({ setup() { useBreadcrumbLabel(() => name.value); return () => h('div') } })
    mount(Host, { global: { plugins: [router, pinia] } })
    await flushPromises()
    const store = useAdminBreadcrumbStore()
    expect(store.leafLabelFor('/matters/7')).toBe('Smith v. Jones')
    name.value = 'Doe v. Roe'
    await flushPromises()
    expect(store.leafLabelFor('/matters/7')).toBe('Doe v. Roe')
  })

  it('clears its contribution on unmount', async () => {
    const Host = defineComponent({ setup() { useBreadcrumbLabel(() => 'Temp'); return () => h('div') } })
    const w = mount(Host, { global: { plugins: [router, pinia] } })
    await flushPromises()
    const store = useAdminBreadcrumbStore()
    expect(store.leafLabelFor('/matters/7')).toBe('Temp')
    w.unmount()
    expect(store.leafLabelFor('/matters/7')).toBeUndefined()
  })

  it('ignores falsy values (keeps the route fallback until data loads)', async () => {
    const name = ref<string | null>(null)
    const Host = defineComponent({ setup() { useBreadcrumbLabel(() => name.value); return () => h('div') } })
    mount(Host, { global: { plugins: [router, pinia] } })
    await flushPromises()
    const store = useAdminBreadcrumbStore()
    expect(store.leafLabelFor('/matters/7')).toBeUndefined()
  })
})

describe('useBreadcrumbTrail', () => {
  it('writes a full trail under the route key', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const router = makeRouter()
    await router.push('/matters/7')
    await router.isReady()
    const trail = ref([{ label: 'Clients', to: '/admin/clients' }, { label: 'File 9' }])
    const Host = defineComponent({ setup() { useBreadcrumbTrail(() => trail.value); return () => h('div') } })
    mount(Host, { global: { plugins: [router, pinia] } })
    await flushPromises()
    const store = useAdminBreadcrumbStore()
    expect(store.trailFor('/matters/7')).toEqual([{ label: 'Clients', to: '/admin/clients' }, { label: 'File 9' }])
  })

  it('no-ops without a router / pinia (bare unit mount)', () => {
    const Host = defineComponent({ setup() { useBreadcrumbTrail(() => [{ label: 'x' }]); return () => h('div') } })
    // No router/pinia plugins - the composable swallows and renders cleanly.
    expect(() => mount(Host)).not.toThrow()
  })
})
