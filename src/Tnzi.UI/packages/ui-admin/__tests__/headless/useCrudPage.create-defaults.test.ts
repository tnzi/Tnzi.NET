import { describe, it, expect, vi } from 'vitest'
import { defineComponent } from 'vue'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import { useCrudPage, type UseCrudPageReturn } from '../../src/headless/useCrudPage'

/**
 * A switch the form does not seed renders OFF, while the request leaves the
 * field out and the server applies its own default (ON for "enabled" /
 * "active" flags). The operator saw "disabled" and created an enabled record.
 * `createDefaults` puts the server's default on the form, so what is shown is
 * what is created, however the create form was opened.
 */
interface Tenant {
  id: string
  name?: string
  isEnabled?: boolean
}

const ROWS: Tenant[] = [{ id: 't1', name: 'Acme', isEnabled: false }]

async function harness(initialPath = '/list') {
  let crud = null as unknown as UseCrudPageReturn<Tenant>
  const Comp = defineComponent({
    setup() {
      crud = useCrudPage<Tenant>({
        pageId: 'test.tenants',
        columns: [],
        rowKey: (r) => r.id,
        fetchData: vi.fn(async () => ({
          items: ROWS, totalCount: 1, pageIndex: 1, pageSize: 20, totalPages: 1, hasPreviousPage: false, hasNextPage: false,
        })),
        createData: vi.fn(async (d: Partial<Tenant>) => ({ id: 'new', ...d })),
        updateData: vi.fn(async (id: string, d: Partial<Tenant>) => ({ id, ...d })),
        createDefaults: { isEnabled: true },
        retryFetch: 0,
      })
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
  return crud
}

describe('useCrudPage createDefaults', () => {
  it('a new record starts with the defaults', async () => {
    const crud = await harness()
    crud.openCreate()
    expect(crud.formModal.formData.value).toEqual({ isEnabled: true })
  })

  it('a seed wins over a default', async () => {
    const crud = await harness()
    crud.openCreate({ isEnabled: false, name: 'Staged' })
    expect(crud.formModal.formData.value).toEqual({ isEnabled: false, name: 'Staged' })
  })

  it('a restored create deep link starts with the defaults too', async () => {
    const crud = await harness('/list?detail=new')
    expect(crud.formModal.mode.value).toBe('create')
    expect(crud.formModal.formData.value).toEqual({ isEnabled: true })
  })

  it('an edit keeps the record as it is', async () => {
    const crud = await harness()
    crud.openEdit({ id: 't1', name: 'Acme' })
    expect(crud.formModal.formData.value).toEqual({ id: 't1', name: 'Acme' })
  })
})
