import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent, ref } from 'vue'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useDetail } from '../../src/headless/useDetail'

interface Foo { id: number; name: string }

describe('useDetail (modal/drawer state)', () => {
  it('opens in create mode with empty data and closes', async () => {
    const d = useDetail<Foo>({ mode: 'modal' })
    expect(d.visible.value).toBe(false)
    await d.open('create')
    expect(d.visible.value).toBe(true)
    expect(d.action.value).toBe('create')
    d.close()
    expect(d.visible.value).toBe(false)
    expect(d.action.value).toBe(null)
  })

  it('opens in edit mode with cloned payload (no mutation of source)', async () => {
    const d = useDetail<Foo>({ mode: 'drawer' })
    const src: Foo = { id: 1, name: 'a' }
    await d.open('edit', src)
    expect(d.data.value).toEqual(src)
    d.data.value!.name = 'b'
    expect(src.name).toBe('a') // cloned, not referenced
  })

  it('loads data via loadData when opened with an id', async () => {
    const loadData = vi.fn(async (id: number | string) => ({ id: Number(id), name: 'loaded' }))
    const d = useDetail<Foo>({ mode: 'modal', loadData })
    await d.open('view', 7)
    expect(loadData).toHaveBeenCalledWith(7)
    expect(d.data.value).toEqual({ id: 7, name: 'loaded' })
  })

  it('submit calls submitData with action + data', async () => {
    const submitData = vi.fn(async () => undefined)
    const d = useDetail<Foo>({ mode: 'modal', submitData })
    await d.open('edit', { id: 1, name: 'a' })
    await d.submit()
    expect(submitData).toHaveBeenCalledWith('edit', { id: 1, name: 'a' })
  })

  it('tracks the active section and defaults to defaultSection', () => {
    const d = useDetail<Foo>({
      sections: [{ key: 'basic', label: 'Basic' }, { key: 'perms', label: 'Perms' }],
      defaultSection: 'perms',
    })
    expect(d.activeSection.value).toBe('perms')
    d.setSection('basic')
    expect(d.activeSection.value).toBe('basic')
  })

  it('does not close an editable detail when submitData is missing', async () => {
    const d = useDetail<Foo>({ mode: 'modal' }) // no submitData
    await d.open('edit', { id: 1, name: 'a' })
    await d.submit()
    expect(d.visible.value).toBe(true) // stayed open, no fake success
  })

  it('closes a view detail on submit even without submitData', async () => {
    const d = useDetail<Foo>({ mode: 'modal' })
    await d.open('view', { id: 1, name: 'a' })
    await d.submit()
    expect(d.visible.value).toBe(false)
  })
})

describe('useDetail (overlay ⇄ URL query)', () => {
  async function harness(
    opts: Parameters<typeof useDetail<Foo>>[0],
    initialPath = '/list',
  ) {
    let api: ReturnType<typeof useDetail<Foo>> = null as never
    const Comp = defineComponent({
      setup() {
        api = useDetail<Foo>(opts)
        return () => null
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/list', name: 'list', component: { template: '<div/>' } }],
    })
    router.push(initialPath)
    await router.isReady()
    mount(Comp, { global: { plugins: [router] } })
    await flushPromises()
    return { api: () => api, router }
  }

  it('mirrors an opened edit overlay into ?<key>=edit:<id> and clears on close', async () => {
    const { api, router } = await harness({ mode: 'modal', url: 'roles' })
    await api().open('edit', { id: 5, name: 'a' })
    await flushPromises()
    expect(router.currentRoute.value.query.roles).toBe('edit:5')

    api().close()
    await flushPromises()
    expect(router.currentRoute.value.query.roles).toBeUndefined()
    expect(api().visible.value).toBe(false)
  })

  it('url: true claims the default key `detail`', async () => {
    const { api, router } = await harness({ mode: 'drawer', url: true })
    await api().open('view', { id: 8, name: 'a' })
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBe('view:8')
  })

  it('deep-links ?<key>=edit:<id> on first paint, hydrating via loadData', async () => {
    const loadData = vi.fn(async (id: number | string) => ({ id: Number(id), name: 'Loaded' }))
    const { api } = await harness(
      { mode: 'modal', url: 'roles', loadData },
      '/list?roles=edit:9',
    )
    await flushPromises()
    expect(loadData).toHaveBeenCalledWith('9')
    expect(api().visible.value).toBe(true)
    expect(api().action.value).toBe('edit')
    expect(api().data.value?.id).toBe(9)
  })

  it('resolves a deep link from `source` once its items arrive', async () => {
    const items = ref<Foo[]>([])
    const loading = ref(true)
    const { api, router } = await harness(
      { mode: 'modal', url: 'roles', source: { items, loading } },
      '/list?roles=edit:9',
    )
    await flushPromises()
    // Items not loaded yet — the deep link is KEPT (busy), not self-wiped.
    expect(api().visible.value).toBe(false)
    expect(router.currentRoute.value.query.roles).toBe('edit:9')

    items.value = [{ id: 9, name: 'from-list' }]
    loading.value = false
    await flushPromises()
    expect(api().visible.value).toBe(true)
    expect(api().data.value?.name).toBe('from-list')
  })

  it('falls back to source.loadById when the id is beyond the loaded items', async () => {
    const items = ref<Foo[]>([{ id: 1, name: 'other' }])
    const loadById = vi.fn(async (id: string) => ({ id: Number(id), name: 'by-id' }))
    const { api } = await harness(
      { mode: 'modal', url: 'roles', source: { items, loadById } },
      '/list?roles=view:42',
    )
    await flushPromises()
    expect(loadById).toHaveBeenCalledWith('42')
    expect(api().data.value?.name).toBe('by-id')
  })

  it('Back closes an open overlay (push history)', async () => {
    const { api, router } = await harness({ mode: 'drawer', url: 'manage' })
    await api().open('view', { id: 3, name: 'b' })
    await flushPromises()
    expect(api().visible.value).toBe(true)

    router.back()
    await flushPromises()
    expect(router.currentRoute.value.query.manage).toBeUndefined()
    expect(api().visible.value).toBe(false)
  })

  it('drops a dangling deep-link key when loadData resolves nothing', async () => {
    const loadData = vi.fn(async () => null)
    const { api, router } = await harness(
      { mode: 'modal', url: 'roles', loadData },
      '/list?roles=view:ghost',
    )
    await flushPromises()
    expect(api().visible.value).toBe(false)
    expect(router.currentRoute.value.query.roles).toBeUndefined()
  })

  it('ignores the URL in page mode (the route IS the open-state)', async () => {
    const { api, router } = await harness({ mode: 'page', url: 'detail' })
    api().form.open('edit', { id: 1, name: 'x' })
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBeUndefined()
  })

  it('opt-out: no `url` leaves the query untouched', async () => {
    const { api, router } = await harness({ mode: 'modal' })
    await api().open('edit', { id: 5, name: 'a' })
    await flushPromises()
    expect(api().visible.value).toBe(true)
    expect(router.currentRoute.value.query).toEqual({})
  })
})

describe('useDetail (page mode routing)', () => {
  it('open() derives the route id from an object payload via getId', async () => {
    let api: ReturnType<typeof useDetail<Foo>> | null = null
    const Comp = defineComponent({
      setup() {
        api = useDetail<Foo>({
          mode: 'page',
          pageRoute: { name: 'detail' },
          getId: (r) => r.id,
        })
        return () => null
      },
    })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/list', name: 'list', component: { template: '<div/>' } },
        { path: '/detail/:id', name: 'detail', component: { template: '<div/>' } },
      ],
    })
    router.push('/list')
    await router.isReady()
    mount(Comp, { global: { plugins: [router] } })
    await api!.open('edit', { id: 5, name: 'x' })
    await flushPromises()
    expect(router.currentRoute.value.params.id).toBe('5')
    expect(router.currentRoute.value.query.action).toBe('edit')
  })
})
