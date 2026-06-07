import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { defineComponent } from 'vue'
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
