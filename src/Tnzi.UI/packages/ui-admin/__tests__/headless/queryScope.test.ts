import { describe, it, expect } from 'vitest'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useQueryScope, type UseQueryScopeReturn } from '../../src/headless/queryScope'

async function harness(key: string, initialPath = '/d/1') {
  let api: UseQueryScopeReturn = null as unknown as UseQueryScopeReturn
  const Comp = defineComponent({
    setup() {
      api = useQueryScope(key)
      return () => null
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/d/:id', name: 'd', component: { template: '<div/>' } }],
  })
  router.push(initialPath)
  await router.isReady()
  mount(Comp, { global: { plugins: [router] } })
  await flushPromises()
  return { api: () => api, router }
}

describe('useQueryScope', () => {
  it('reads its key from the URL query', async () => {
    const { api } = await harness('detail', '/d/1?detail=view:42')
    expect(api().value.value).toBe('view:42')
    expect(api().read()).toBe('view:42')
    expect(api().active()).toBe(true)
  })

  it('push adds history so Back undoes the write; replace does not', async () => {
    const { api, router } = await harness('detail', '/d/1')
    expect(api().value.value).toBe(null)

    api().set('view:42', 'push')
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBe('view:42')

    // Back closes it - value follows the URL back to null.
    router.back()
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBeUndefined()
    expect(api().value.value).toBe(null)
  })

  it('set(null, replace) removes only this key', async () => {
    const { api, router } = await harness('detail', '/d/1?section=overview&detail=edit:7')
    expect(api().value.value).toBe('edit:7')
    api().set(null, 'replace')
    await flushPromises()
    // detail dropped, the sibling section key survives.
    expect(router.currentRoute.value.query).toEqual({ section: 'overview' })
  })

  it('preserves sibling business query params and the #hash on write', async () => {
    const { api, router } = await harness('detail', '/d/1?page=2&kw=x#anchor')
    api().set('view:42', 'push')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ page: '2', kw: 'x', detail: 'view:42' })
    // The fragment stays free for real anchors - never cleared by a scope write.
    expect(router.currentRoute.value.hash).toBe('#anchor')
  })

  it('coexists with a sibling key written by another instance', async () => {
    let detail: UseQueryScopeReturn = null as never
    let section: UseQueryScopeReturn = null as never
    const Comp = defineComponent({
      setup() {
        detail = useQueryScope('detail')
        section = useQueryScope('section')
        return () => null
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/d/:id', name: 'd', component: { template: '<div/>' } }],
    })
    router.push('/d/1')
    await router.isReady()
    mount(Comp, { global: { plugins: [router] } })
    await flushPromises()

    section.set('overview', 'replace')
    await flushPromises()
    detail.set('view:42', 'push')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({ section: 'overview', detail: 'view:42' })
    // each instance only sees its own key
    expect(section.value.value).toBe('overview')
    expect(detail.value.value).toBe('view:42')
  })

  it('degrades to an inert local ref without a router', () => {
    const api = useQueryScope('detail')
    expect(api.active()).toBe(false)
    api.set('view:42', 'push')
    expect(api.value.value).toBe('view:42') // local only, no router to write to
  })
})
