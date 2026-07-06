import { describe, it, expect, vi } from 'vitest'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory, type Router } from 'vue-router'
import { useCrudPage, type UseCrudPageReturn } from '../../src/headless/useCrudPage'

interface Foo {
  id: string
  name: string
}

const ROWS: Foo[] = [
  { id: 'a1', name: 'Alpha' },
  { id: 'b2', name: 'Beta' },
]

function makeOptions(extra: Partial<Parameters<typeof useCrudPage<Foo>>[0]> = {}) {
  return {
    pageId: 'test.foo',
    columns: [],
    rowKey: (r: Foo) => r.id,
    fetchData: vi.fn(async () => ({
      items: ROWS,
      totalCount: ROWS.length,
      pageIndex: 1,
      pageSize: 20,
      totalPages: 1,
      hasPreviousPage: false,
      hasNextPage: false,
    })),
    createData: vi.fn(async (d: Partial<Foo>) => ({ id: 'new', name: 'x', ...d })),
    updateData: vi.fn(async (id: string, d: Partial<Foo>) => ({ id, name: 'x', ...d })),
    deleteData: vi.fn(async () => undefined),
    retryFetch: 0,
    ...extra,
  }
}

async function harness(
  extra: Partial<Parameters<typeof useCrudPage<Foo>>[0]> = {},
  initialPath = '/list',
) {
  let crud: UseCrudPageReturn<Foo> = null as unknown as UseCrudPageReturn<Foo>
  const Comp = defineComponent({
    setup() {
      crud = useCrudPage<Foo>(makeOptions(extra))
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
  return { crud: () => crud, router }
}

describe('useCrudPage — detail overlay ⇄ URL query', () => {
  it('writes ?detail=edit:<id> when an edit overlay opens, and clears it on close', async () => {
    const { crud, router } = await harness()
    crud().openEdit(ROWS[1])
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBe('edit:b2')
    expect(crud().formModal.visible.value).toBe(true)

    crud().formModal.close()
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBeUndefined()
    expect(crud().formModal.visible.value).toBe(false)
  })

  it('writes ?detail=new for create and ?detail=view:<id> for view', async () => {
    const { crud, router } = await harness()
    crud().openCreate()
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBe('new')
    crud().formModal.close()
    await flushPromises()

    crud().openView(ROWS[0])
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBe('view:a1')
  })

  it('Back closes an open overlay (push history)', async () => {
    const { crud, router } = await harness()
    crud().openView(ROWS[0])
    await flushPromises()
    expect(crud().formModal.visible.value).toBe(true)

    router.back()
    await flushPromises()
    expect(router.currentRoute.value.query.detail).toBeUndefined()
    expect(crud().formModal.visible.value).toBe(false)
  })

  it('deep-links ?detail=view:<id> on first paint, hydrating via loadDetailById', async () => {
    const loadDetailById = vi.fn(async (id: string) => ({ id, name: 'Loaded ' + id }))
    const { crud } = await harness({ loadDetailById }, '/list?detail=view:zz9')
    await flushPromises()
    expect(loadDetailById).toHaveBeenCalledWith('zz9')
    expect(crud().formModal.visible.value).toBe(true)
    expect(crud().formModal.mode.value).toBe('view')
    expect(crud().formModal.formData.value?.id).toBe('zz9')
  })

  it('deep-links from the loaded list when no loadDetailById is given', async () => {
    const { crud } = await harness({}, '/list?detail=edit:a1')
    await crud().refresh()
    await flushPromises()
    expect(crud().formModal.visible.value).toBe(true)
    expect(crud().formModal.mode.value).toBe('edit')
    expect(crud().formModal.formData.value?.name).toBe('Alpha')
  })

  it('drops a dangling ?detail key when the id cannot be resolved after load', async () => {
    const { crud, router } = await harness({}, '/list?detail=view:ghost')
    await crud().refresh()
    await flushPromises()
    expect(crud().formModal.visible.value).toBe(false)
    expect(router.currentRoute.value.query.detail).toBeUndefined()
  })

  it('renames the key via detailUrl: <string>', async () => {
    const { crud, router } = await harness({ detailUrl: 'record' })
    crud().openEdit(ROWS[0])
    await flushPromises()
    expect(router.currentRoute.value.query.record).toBe('edit:a1')
    expect(router.currentRoute.value.query.detail).toBeUndefined()
  })

  it('opt-out: detailUrl=false leaves the query untouched', async () => {
    const { crud, router } = await harness({ detailUrl: false })
    crud().openEdit(ROWS[0])
    await flushPromises()
    expect(crud().formModal.visible.value).toBe(true)
    expect(router.currentRoute.value.query).toEqual({})
  })
})
