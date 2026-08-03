import { describe, it, expect } from 'vitest'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useDetail } from '../../src/headless/useDetail'
import { useCrudPage, type UseCrudPageReturn } from '../../src/headless/useCrudPage'
import {
  ADMIN_DEEP_LINK_KEY,
  resolveDeepLinkConfig,
  type ResolvedDeepLinkConfig,
} from '../../src/plugin/deep-link-config'

interface Foo {
  id: string
  name: string
}

describe('resolveDeepLinkConfig', () => {
  it('normalises the consumer shapes', () => {
    expect(resolveDeepLinkConfig(undefined)).toEqual({ detail: true, section: true })
    expect(resolveDeepLinkConfig(true)).toEqual({ detail: true, section: true })
    expect(resolveDeepLinkConfig(false)).toEqual({ detail: false, section: false })
    expect(resolveDeepLinkConfig({ detail: false })).toEqual({ detail: false, section: true })
    expect(resolveDeepLinkConfig({ section: false })).toEqual({ detail: true, section: false })
  })
})

async function harness(provided: ResolvedDeepLinkConfig, setup: () => void, initialPath = '/list') {
  const Comp = defineComponent({
    setup() {
      setup()
      return () => null
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/list', name: 'list', component: { template: '<div/>' } }],
  })
  router.push(initialPath)
  await router.isReady()
  mount(Comp, {
    global: { plugins: [router], provide: { [ADMIN_DEEP_LINK_KEY as symbol]: provided } },
  })
  await flushPromises()
  return { router }
}

describe('app-wide deep-link kill switch', () => {
  it('deepLink detail=false suppresses an explicitly enabled overlay url', async () => {
    let api: ReturnType<typeof useDetail<Foo>> = null as never
    const { router } = await harness({ detail: false, section: true }, () => {
      api = useDetail<Foo>({ mode: 'modal', url: 'roles' })
    })
    await api.open('edit', { id: '5', name: 'a' })
    await flushPromises()
    expect(api.visible.value).toBe(true) // overlay still works locally
    expect(router.currentRoute.value.query).toEqual({}) // but never touches the URL
  })

  it('deepLink detail=false suppresses useCrudPage default deep-linking', async () => {
    let crud: UseCrudPageReturn<Foo> = null as never
    const { router } = await harness({ detail: false, section: true }, () => {
      crud = useCrudPage<Foo>({
        pageId: 'test.foo',
        columns: [],
        rowKey: (r) => r.id,
        fetchData: async () => ({
          items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
          totalPages: 0, hasPreviousPage: false, hasNextPage: false,
        }),
        retryFetch: 0,
      })
    })
    crud.openEdit({ id: 'a1', name: 'Alpha' })
    await flushPromises()
    expect(crud.formModal.visible.value).toBe(true)
    expect(router.currentRoute.value.query).toEqual({})
  })

  it('deepLink section=false suppresses section syncing but keeps the local nav working', async () => {
    let api: ReturnType<typeof useDetail<Foo>> = null as never
    const { router } = await harness({ detail: true, section: false }, () => {
      api = useDetail<Foo>({
        mode: 'page',
        sectionUrl: true,
        sections: [
          { key: 'a', label: 'A' },
          { key: 'b', label: 'B' },
        ],
        defaultSection: 'a',
      })
    })
    expect(api.activeSection.value).toBe('a')
    api.setSection('b')
    await flushPromises()
    expect(api.activeSection.value).toBe('b') // local nav intact
    expect(router.currentRoute.value.query).toEqual({}) // URL untouched
  })

  it('all-enabled config keeps the default behaviour', async () => {
    let api: ReturnType<typeof useDetail<Foo>> = null as never
    const { router } = await harness({ detail: true, section: true }, () => {
      api = useDetail<Foo>({ mode: 'modal', url: 'roles' })
    })
    await api.open('edit', { id: '5', name: 'a' })
    await flushPromises()
    expect(router.currentRoute.value.query.roles).toBe('edit:5')
  })
})
