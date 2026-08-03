import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { useCrudPage, type UseCrudPageOptions } from '../../src/headless/useCrudPage'
import type { ColumnDef } from '../../src/headless/useColumnSettings'

/**
 * Write-confirmation toasts.
 *
 * A list-form save otherwise gives no feedback at all: the modal closes and the
 * row lands somewhere in a refreshed list, which on page 3 of a sorted list is
 * indistinguishable from "nothing happened". Consumers were wrapping their own
 * write callbacks to add this, which is the shape of a framework gap.
 */

interface User {
  id: string
  name: string
}

const columns: ColumnDef[] = [{ key: 'id', title: 'ID' }]

function makeCrud(extra: Partial<UseCrudPageOptions<User>> = {}) {
  return useCrudPage<User>({
    pageId: 'users',
    columns,
    rowKey: (row) => row.id,
    fetchData: async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20 }),
    createData: async (data) => ({ id: 'new', name: 'x', ...data }) as User,
    updateData: async (id, data) => ({ id: String(id), name: 'x', ...data }) as User,
    deleteData: async () => {},
    retryFetch: 0,
    autoLoad: false,
    ...extra,
  })
}

let success: ReturnType<typeof vi.fn>
let error: ReturnType<typeof vi.fn>

beforeEach(() => {
  success = vi.fn()
  error = vi.fn()
  ;(window as unknown as { $message: unknown }).$message = { success, error }
})

afterEach(() => {
  delete (window as unknown as { $message?: unknown }).$message
})

describe('useCrudPage success toasts', () => {
  it('confirms a create by default', async () => {
    const crud = makeCrud()
    crud.openCreate()
    crud.formModal.formData.value = { name: 'Alice' } as User
    await crud.submit()

    expect(success).toHaveBeenCalledTimes(1)
    expect(error).not.toHaveBeenCalled()
  })

  it('confirms an update by default', async () => {
    const crud = makeCrud()
    crud.openEdit({ id: '1', name: 'Alice' })
    await crud.submit()

    expect(success).toHaveBeenCalledTimes(1)
  })

  it('confirms a delete by default', async () => {
    const crud = makeCrud()
    await crud.handleDelete(['1'])

    expect(success).toHaveBeenCalledTimes(1)
  })

  it('stays silent when successToast is false', async () => {
    const crud = makeCrud({ successToast: false })
    crud.openCreate()
    crud.formModal.formData.value = { name: 'Alice' } as User
    await crud.submit()
    await crud.handleDelete(['1'])

    expect(success).not.toHaveBeenCalled()
  })

  it('honours a per-operation opt-out', async () => {
    // A page that renders its own removal confirmation but wants the
    // create/update ones.
    const crud = makeCrud({ successToast: { delete: false } })

    await crud.handleDelete(['1'])
    expect(success).not.toHaveBeenCalled()

    crud.openCreate()
    crud.formModal.formData.value = { name: 'Alice' } as User
    await crud.submit()
    expect(success).toHaveBeenCalledTimes(1)
  })

  it('does not confirm a write that failed', async () => {
    const crud = makeCrud({
      createData: async () => {
        throw new Error('boom')
      },
    })
    crud.openCreate()
    crud.formModal.formData.value = { name: 'Alice' } as User

    await expect(crud.submit()).rejects.toThrow('boom')
    expect(success).not.toHaveBeenCalled()
    expect(error).toHaveBeenCalledWith('boom')
  })

  it('no-ops when the host registered a handle without success()', async () => {
    // Apps that do not mount TAdminAppRoot may register a minimal handle.
    ;(window as unknown as { $message: unknown }).$message = { error }
    const crud = makeCrud()
    crud.openCreate()
    crud.formModal.formData.value = { name: 'Alice' } as User

    await expect(crud.submit()).resolves.toBeTruthy()
  })
})
