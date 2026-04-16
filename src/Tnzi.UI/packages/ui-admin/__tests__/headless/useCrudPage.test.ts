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

function makeCrud(overrides: Partial<ReturnType<typeof createFakeBridge>> = {}) {
  const bridge = { ...createFakeBridge(), ...overrides }
  const crud = useCrudPage<User>({
    pageId: 'users',
    columns,
    rowKey: (row) => row.id,
    fetchData: bridge.fetchData,
    createData: bridge.createData,
    updateData: bridge.updateData,
    deleteData: bridge.deleteData,
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
    const { crud, bridge } = makeCrud()
    await crud.refresh()
    crud.setPage(2)
    await crud.refresh()
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
})
