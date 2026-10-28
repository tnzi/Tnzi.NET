import { describe, it, expect, vi } from 'vitest'
import { useCrudPage } from '../../src/headless/useCrudPage'
import { useDetail } from '../../src/headless/useDetail'
import type { ColumnDef } from '../../src/headless/useColumnSettings'

/**
 * One Confirm, one write. The engines read the mode and the record before the
 * request and close the form only after it answers, so a second click while a
 * slow POST is in flight passes every check the first one did: two customers,
 * two pay-run rows, two settings.
 */

interface Row {
  id: string
  name: string
}

const columns: ColumnDef[] = [{ key: 'name', title: 'Name' }]

/** A promise the test settles by hand, standing in for a slow request. */
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function makeCrud(createData: (data: Partial<Row>) => Promise<Row>, updateData?: (id: string, data: Partial<Row>) => Promise<Row>) {
  return useCrudPage<Row, string>({
    pageId: 'in-flight',
    columns,
    rowKey: (r) => r.id,
    fetchData: async () => ({ items: [], totalCount: 0, pageIndex: 1, pageSize: 20, totalPages: 0, hasPreviousPage: false, hasNextPage: false }),
    createData,
    updateData,
    autoLoad: false,
    successToast: false,
    detailUrl: false,
  })
}

describe('useCrudPage.submit - in-flight guard', () => {
  it('★ a second submit while the create is in flight does not post again', async () => {
    const pending = deferred<Row>()
    const createData = vi.fn(() => pending.promise)
    const crud = makeCrud(createData)
    crud.openCreate({ name: 'Acme' })

    const first = crud.submit()
    const second = crud.submit()
    expect(createData).toHaveBeenCalledTimes(1)
    expect(await second).toBeNull()

    pending.resolve({ id: 'c1', name: 'Acme' })
    expect(await first).toEqual({ id: 'c1', name: 'Acme' })
    expect(createData).toHaveBeenCalledTimes(1)
  })

  it('★ the same guard holds for an edit', async () => {
    const pending = deferred<Row>()
    const updateData = vi.fn(() => pending.promise)
    const crud = makeCrud(vi.fn(), updateData)
    crud.openEdit({ id: 'r1', name: 'Old' })

    const first = crud.submit()
    void crud.submit()
    void crud.submit()
    expect(updateData).toHaveBeenCalledTimes(1)
    pending.resolve({ id: 'r1', name: 'New' })
    await first
  })

  it('exposes the in-flight state and releases it when the write answers', async () => {
    const pending = deferred<Row>()
    const crud = makeCrud(() => pending.promise)
    crud.openCreate({ name: 'Acme' })
    expect(crud.submitting.value).toBe(false)

    const first = crud.submit()
    expect(crud.submitting.value).toBe(true)
    expect(crud.formModal.submitting.value).toBe(true)

    pending.resolve({ id: 'c1', name: 'Acme' })
    await first
    expect(crud.submitting.value).toBe(false)
  })

  it('releases the guard when the write fails, so the operator can retry', async () => {
    const createData = vi.fn().mockRejectedValueOnce(new Error('boom')).mockResolvedValueOnce({ id: 'c1', name: 'Acme' })
    const crud = makeCrud(createData)
    crud.openCreate({ name: 'Acme' })

    await expect(crud.submit()).rejects.toThrow('boom')
    expect(crud.submitting.value).toBe(false)
    expect(crud.formModal.visible.value).toBe(true)

    await crud.submit()
    expect(createData).toHaveBeenCalledTimes(2)
  })

  it('★ a form reopened while the first save was in flight is not closed by that save', async () => {
    const pending = deferred<Row>()
    const crud = makeCrud(() => pending.promise)
    crud.openCreate({ name: 'First' })
    const first = crud.submit()

    // The operator gives up on waiting, closes the modal and starts another.
    crud.formModal.close()
    crud.openCreate({ name: 'Second, half typed' })

    pending.resolve({ id: 'c1', name: 'First' })
    await first
    expect(crud.formModal.visible.value).toBe(true)
    expect((crud.formModal.formData.value as Row).name).toBe('Second, half typed')
  })
})

describe('useDetail.submit - in-flight guard', () => {
  it('★ a second submit while submitData is in flight does not write again', async () => {
    const pending = deferred<void>()
    const submitData = vi.fn(() => pending.promise)
    const d = useDetail<Row>({ mode: 'modal', submitData })
    await d.open('create', { id: '', name: 'Acme' })

    const first = d.submit()
    const second = d.submit()
    expect(submitData).toHaveBeenCalledTimes(1)
    expect(d.submitting.value).toBe(true)
    await second

    pending.resolve()
    await first
    expect(submitData).toHaveBeenCalledTimes(1)
    expect(d.submitting.value).toBe(false)
    expect(d.visible.value).toBe(false)
  })

  it('releases the guard when submitData fails', async () => {
    const submitData = vi.fn().mockRejectedValueOnce(new Error('nope')).mockResolvedValueOnce(undefined)
    const d = useDetail<Row>({ mode: 'modal', submitData })
    await d.open('edit', { id: 'r1', name: 'Acme' })

    await expect(d.submit()).rejects.toThrow('nope')
    expect(d.submitting.value).toBe(false)
    expect(d.visible.value).toBe(true)

    await d.submit()
    expect(submitData).toHaveBeenCalledTimes(2)
  })
})
