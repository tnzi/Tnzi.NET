import { describe, it, expect, beforeEach, vi } from 'vitest'
import { nextTick } from 'vue'
import { useCrudPage } from '../../src/headless/useCrudPage'
import type { ColumnDef } from '../../src/headless/useColumnSettings'

interface User {
  id: string
  name: string
}

const sampleUsers: User[] = [
  { id: '1', name: 'Alice' },
  { id: '2', name: 'Bob' },
  { id: '3', name: 'Carol' },
]

const columns: ColumnDef[] = [
  { key: 'id', title: 'ID' },
  { key: 'name', title: 'Name' },
]

function createFakeBridge() {
  return {
    fetchData: vi.fn(async () => ({
      items: sampleUsers,
      totalCount: sampleUsers.length,
      pageIndex: 1,
      pageSize: 20,
    })),
    createData: vi.fn(async (data: Partial<User>) => ({ id: 'new', name: 'x', ...data }) as User),
    updateData: vi.fn(async (id: string, data: Partial<User>) => ({ id, name: 'x', ...data }) as User),
    deleteData: vi.fn(async () => {}),
  }
}

function makeCrud(
  overrides: Partial<ReturnType<typeof createFakeBridge>> = {},
  extraOptions: { retryFetch?: number; retryDelayMs?: number; onError?: (err: Error, op: string) => boolean | void } = {},
) {
  const bridge = { ...createFakeBridge(), ...overrides }
  const crud = useCrudPage<User>({
    pageId: 'users',
    columns,
    rowKey: (row) => row.id,
    fetchData: bridge.fetchData,
    createData: bridge.createData,
    updateData: bridge.updateData,
    deleteData: bridge.deleteData,
    // Default to no retries in tests so existing assertions about call
    // counts stay stable; tests that need retry behavior pass their own.
    retryFetch: 0,
    ...extraOptions,
  })
  return { crud, bridge }
}

describe('useCrudPage', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('fetches data on refresh and exposes items/total', async () => {
    const { crud, bridge } = makeCrud()
    await crud.refresh()
    expect(bridge.fetchData).toHaveBeenCalledTimes(1)
    expect(crud.items.value).toHaveLength(3)
    expect(crud.total.value).toBe(3)
    expect(crud.hasData.value).toBe(true)
  })

  it('refetches when page index changes', async () => {
    // setPage auto-triggers refresh (NDataTable @update:page emit fires once
    // per click — without the auto-refresh the table content stayed on
    // page 1 even though the pagination index moved).
    const { crud, bridge } = makeCrud()
    await crud.refresh()
    crud.setPage(2)
    // Let the queued microtask complete.
    await Promise.resolve()
    await Promise.resolve()
    expect(bridge.fetchData).toHaveBeenCalledTimes(2)
    const secondCall = bridge.fetchData.mock.calls[1][0]
    expect(secondCall.pageIndex).toBe(2)
  })

  it('setSearch updates query.searchText and resets page to 1', async () => {
    const { crud, bridge } = makeCrud()
    await crud.refresh()
    crud.setPage(3)
    crud.setSearch('alice')
    await crud.refresh()
    expect(crud.query.value.searchText).toBe('alice')
    expect(crud.query.value.pageIndex).toBe(1)
    const lastCall = bridge.fetchData.mock.calls.at(-1)![0]
    expect(lastCall.searchText).toBe('alice')
    expect(lastCall.pageIndex).toBe(1)
  })

  it('resetQuery clears query state', async () => {
    const { crud } = makeCrud()
    crud.setSearch('foo')
    crud.setPage(5)
    crud.resetQuery()
    expect(crud.query.value.searchText).toBe('')
    expect(crud.query.value.pageIndex).toBe(1)
  })

  it('openCreate opens modal in create mode', () => {
    const { crud } = makeCrud()
    crud.openCreate()
    expect(crud.formModal.visible.value).toBe(true)
    expect(crud.formModal.mode.value).toBe('create')
  })

  it('openEdit opens modal in edit mode with row data', () => {
    const { crud } = makeCrud()
    crud.openEdit(sampleUsers[0])
    expect(crud.formModal.visible.value).toBe(true)
    expect(crud.formModal.mode.value).toBe('edit')
    expect(crud.formModal.formData.value).toEqual(sampleUsers[0])
  })

  it('submit in create mode calls createData then refreshes + closes', async () => {
    const { crud, bridge } = makeCrud()
    crud.openCreate()
    crud.formModal.formData.value = { id: '', name: 'New' } as User
    await crud.submit()
    expect(bridge.createData).toHaveBeenCalledTimes(1)
    expect(bridge.fetchData).toHaveBeenCalled()
    expect(crud.formModal.visible.value).toBe(false)
  })

  it('submit in edit mode calls updateData(rowKey, data)', async () => {
    const { crud, bridge } = makeCrud()
    crud.openEdit({ id: '7', name: 'Old' })
    crud.formModal.formData.value = { id: '7', name: 'Renamed' }
    await crud.submit()
    expect(bridge.updateData).toHaveBeenCalledTimes(1)
    expect(bridge.updateData).toHaveBeenCalledWith('7', { id: '7', name: 'Renamed' })
    expect(crud.formModal.visible.value).toBe(false)
  })

  it('handleDelete uses selected ids then clears selection', async () => {
    const { crud, bridge } = makeCrud()
    crud.batchActions.select('1')
    crud.batchActions.select('2')
    await crud.handleDelete()
    expect(bridge.deleteData).toHaveBeenCalledWith(['1', '2'])
    expect(crud.batchActions.selectedCount.value).toBe(0)
  })

  it('handleDelete with explicit ids bypasses selection', async () => {
    const { crud, bridge } = makeCrud()
    await crud.handleDelete(['9'])
    expect(bridge.deleteData).toHaveBeenCalledWith(['9'])
  })

  it('loading is true during fetch and false after', async () => {
    let resolveFetch: (v: { items: User[]; totalCount: number; pageIndex: number; pageSize: number }) => void = () => {}
    const pending = new Promise<{ items: User[]; totalCount: number; pageIndex: number; pageSize: number }>((resolve) => {
      resolveFetch = resolve
    })
    const fetchData = vi.fn(() => pending)
    const { crud } = makeCrud({ fetchData: fetchData as any })
    const promise = crud.refresh()
    await nextTick()
    expect(crud.loading.value).toBe(true)
    resolveFetch({ items: sampleUsers, totalCount: 3, pageIndex: 1, pageSize: 20 })
    await promise
    expect(crud.loading.value).toBe(false)
  })

  it('error captures fetch exceptions', async () => {
    const fetchData = vi.fn(async () => {
      throw new Error('boom')
    })
    const { crud } = makeCrud({ fetchData: fetchData as any })
    await crud.refresh().catch(() => {})
    expect(crud.error.value).toBeInstanceOf(Error)
    expect(crud.error.value?.message).toBe('boom')
  })

  // ── B2 0.2.72+ —— retry / onError / dismissError ──

  it('retries fetchData on transient failure and succeeds before exhausting', async () => {
    let calls = 0
    const fetchData = vi.fn(async () => {
      calls++
      if (calls < 3) throw new Error('flaky')
      return { items: sampleUsers, totalCount: 3, pageIndex: 1, pageSize: 20 }
    })
    const { crud } = makeCrud({ fetchData: fetchData as any }, { retryFetch: 3, retryDelayMs: 0 })
    await crud.refresh()
    expect(fetchData).toHaveBeenCalledTimes(3)
    expect(crud.error.value).toBeNull()
    expect(crud.items.value).toHaveLength(3)
  })

  it('surfaces the error after exhausting all retries', async () => {
    const fetchData = vi.fn(async () => {
      throw new Error('always-fails')
    })
    const onError = vi.fn()
    const { crud } = makeCrud(
      { fetchData: fetchData as any },
      { retryFetch: 2, retryDelayMs: 0, onError },
    )
    await crud.refresh().catch(() => {})
    expect(fetchData).toHaveBeenCalledTimes(3) // initial + 2 retries
    expect(crud.error.value?.message).toBe('always-fails')
    expect(onError).toHaveBeenCalledTimes(1)
    expect(onError.mock.calls[0][1]).toBe('fetch')
  })

  it('onError returning false suppresses the default toast', async () => {
    let toastCalls = 0
    const fakeMessage = {
      error: () => {
        toastCalls++
      },
    }
    ;(window as unknown as { $message: typeof fakeMessage }).$message = fakeMessage

    const fetchData = vi.fn(async () => {
      throw new Error('nope')
    })
    const onError = vi.fn(() => false as const)
    const { crud } = makeCrud(
      { fetchData: fetchData as any },
      { retryFetch: 0, retryDelayMs: 0, onError },
    )
    await crud.refresh().catch(() => {})
    expect(onError).toHaveBeenCalledOnce()
    expect(toastCalls).toBe(0)
    ;(window as unknown as { $message?: unknown }).$message = undefined
  })

  it('onError fires for write operations (create/update/delete) and they do not retry', async () => {
    const createData = vi.fn(async () => {
      throw new Error('create-failed')
    })
    const deleteData = vi.fn(async () => {
      throw new Error('delete-failed')
    })
    const onError = vi.fn(() => false as const)
    const { crud } = makeCrud(
      { createData: createData as any, deleteData: deleteData as any },
      { retryFetch: 5, retryDelayMs: 0, onError },
    )
    crud.openCreate()
    crud.formModal.formData.value = { id: '', name: 'x' } as User
    await crud.submit().catch(() => {})
    expect(createData).toHaveBeenCalledTimes(1)
    expect(onError.mock.calls.at(-1)?.[1]).toBe('create')

    await crud.handleDelete(['1']).catch(() => {})
    expect(deleteData).toHaveBeenCalledTimes(1)
    expect(onError.mock.calls.at(-1)?.[1]).toBe('delete')
  })

  it('dismissError clears the error ref without re-fetching', async () => {
    const fetchData = vi.fn(async () => {
      throw new Error('boom')
    })
    const { crud, bridge } = makeCrud(
      { fetchData: fetchData as any },
      { retryFetch: 0, retryDelayMs: 0 },
    )
    await crud.refresh().catch(() => {})
    expect(crud.error.value?.message).toBe('boom')
    crud.dismissError()
    expect(crud.error.value).toBeNull()
    // No new fetch was triggered.
    expect(bridge.fetchData).toHaveBeenCalledTimes(1)
  })

  it('canCreate/canUpdate/canDelete reflect which write callbacks were supplied', () => {
    const readOnly = useCrudPage<{ id: number }>({
      pageId: 'test.readonly',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    expect(readOnly.canCreate).toBe(false)
    expect(readOnly.canUpdate).toBe(false)
    expect(readOnly.canDelete).toBe(false)

    const full = useCrudPage<{ id: number }>({
      pageId: 'test.full',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
      createData: async (d) => ({ id: 1, ...d }) as { id: number },
      updateData: async (_id, d) => ({ id: 1, ...d }) as { id: number },
      deleteData: async () => undefined,
    })
    expect(full.canCreate).toBe(true)
    expect(full.canUpdate).toBe(true)
    expect(full.canDelete).toBe(true)
  })

  it('submit() is a no-op (returns null, no throw) when createData is absent', async () => {
    const state = useCrudPage<{ id: number; name: string }>({
      pageId: 'test.nocreate',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    state.openCreate()
    const result = await state.submit()
    expect(result).toBeNull()
    expect(state.formModal.visible.value).toBe(false)
  })

  it('submit() is a no-op (returns null, no throw) in edit mode when updateData is absent', async () => {
    const state = useCrudPage<{ id: number; name: string }>({
      pageId: 'test.noupdate',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    state.openEdit({ id: 5, name: 'Existing' })
    const result = await state.submit()
    expect(result).toBeNull()
    expect(state.formModal.visible.value).toBe(false)
  })

  it('handleDelete() is a no-op (no throw) when deleteData is absent', async () => {
    const state = useCrudPage<{ id: number }>({
      pageId: 'test.nodelete',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    await expect(state.handleDelete([1])).resolves.toBeUndefined()
  })

  it('openView opens the modal in view mode with a cloned copy', () => {
    const state = useCrudPage<{ id: number; name: string }>({
      pageId: 'test.view',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    const row = { id: 7, name: 'Vue' }
    state.openView(row)
    expect(state.formModal.visible.value).toBe(true)
    expect(state.formModal.mode.value).toBe('view')
    expect(state.formModal.formData.value).toEqual(row)
    // cloned, not the same reference (modal edits don't mutate the source row)
    expect(state.formModal.formData.value).not.toBe(row)
  })

  it('setSort updates sort fields without resetting the page', () => {
    const state = useCrudPage<{ id: number }>({
      pageId: 'test.sort',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    state.setPage(2)
    state.setSort('name', 'asc')
    expect(state.query.value.sortField).toBe('name')
    expect(state.query.value.sortOrder).toBe('asc')
    expect(state.query.value.pageIndex).toBe(2)
  })

  it('setFilters replaces filters and resets to page 1', () => {
    const state = useCrudPage<{ id: number }>({
      pageId: 'test.filters',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    state.setPage(3)
    state.setFilters({ status: 2 })
    expect(state.query.value.filters).toEqual({ status: 2 })
    expect(state.query.value.pageIndex).toBe(1)
  })

  it('setPageSize updates pageSize and resets to page 1', () => {
    const state = useCrudPage<{ id: number }>({
      pageId: 'test.pagesize',
      columns: [{ key: 'id', title: 'ID' }],
      rowKey: (r) => r.id,
      fetchData: async () => ({
        items: [], totalCount: 0, pageIndex: 1, pageSize: 20,
        totalPages: 0, hasPreviousPage: false, hasNextPage: false,
      }),
    })
    state.setPage(3)
    state.setPageSize(50)
    expect(state.query.value.pageSize).toBe(50)
    expect(state.query.value.pageIndex).toBe(1)
  })

  it('exposes the configured detailMode (default "modal")', () => {
    const base = {
      pageId: 't', columns: [], rowKey: (r: { id: number }) => r.id,
      fetchData: async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }),
    }
    expect(useCrudPage(base).detailMode.value).toBe('modal')
    expect(useCrudPage({ ...base, detailMode: 'drawer' as const }).detailMode.value).toBe('drawer')
  })
})
