import { describe, it, expect, vi } from 'vitest'
import { nextTick } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { useCrudPage } from '../../src/headless/useCrudPage'
import type { ColumnDef } from '../../src/headless/useColumnSettings'

/**
 * `loadEditRecord`: an edit form opens on the full record, not on the list row.
 *
 * The defect it removes: a list of summaries (no body, no tag ids, no nickname)
 * opened its edit form on the row, so those fields showed empty and the save
 * wrote them back empty. Pages that noticed hand-wrote the fix, each with its
 * own gaps; this is the one implementation.
 */
interface Post {
  id: string
  title: string
  body?: string
}

const columns: ColumnDef[] = [{ key: 'title', title: 'Title' }]
const row: Post = { id: 'p1', title: 'Hello' }

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function makeCrud(loadEditRecord?: (row: Post) => Promise<Post>, onError?: (err: Error, op: string) => boolean | void) {
  return useCrudPage<Post>({
    pageId: 'posts',
    columns,
    rowKey: (r) => r.id,
    fetchData: async () => ({ items: [row], totalCount: 1, pageIndex: 1, pageSize: 20 }),
    updateData: async (id, data) => ({ id: String(id), title: 'x', ...data }) as Post,
    autoLoad: false,
    detailUrl: false,
    retryFetch: 0,
    loadEditRecord,
    onError: onError ?? (() => false),
  })
}

describe('useCrudPage loadEditRecord', () => {
  it('without it, edit opens on the row as before', () => {
    const crud = makeCrud()
    crud.openEdit(row)

    expect(crud.formModal.mode.value).toBe('edit')
    expect(crud.formModal.formData.value).toEqual(row)
  })

  it('★ opens the edit form on the loaded record, once it has arrived', async () => {
    const load = deferred<Post>()
    const crud = makeCrud(() => load.promise)

    crud.openEdit(row)
    // Nothing to edit yet: a form on the row would show (and save) an empty body.
    expect(crud.formModal.visible.value).toBe(false)

    load.resolve({ ...row, body: 'the body' })
    await flushPromises()

    expect(crud.formModal.mode.value).toBe('edit')
    expect(crud.formModal.formData.value).toEqual({ id: 'p1', title: 'Hello', body: 'the body' })
  })

  it('loads once per opening, not again for the form it opened', async () => {
    const loader = vi.fn(async (r: Post) => ({ ...r, body: 'b' }))
    const crud = makeCrud(loader)

    crud.openEdit(row)
    await flushPromises()
    await nextTick()
    await flushPromises()

    expect(loader).toHaveBeenCalledTimes(1)
  })

  it('a slower, earlier load does not replace the form a later one opened', async () => {
    const first = deferred<Post>()
    const second = deferred<Post>()
    const loads = [first.promise, second.promise]
    const crud = makeCrud(() => loads.shift()!)

    crud.openEdit({ id: 'p1', title: 'One' })
    crud.openEdit({ id: 'p2', title: 'Two' })
    second.resolve({ id: 'p2', title: 'Two', body: 'second' })
    await flushPromises()
    first.resolve({ id: 'p1', title: 'One', body: 'first' })
    await flushPromises()

    expect(crud.formModal.formData.value).toEqual({ id: 'p2', title: 'Two', body: 'second' })
  })

  it('★ a form opened on the row (restored deep link) swaps in the record and cannot save meanwhile', async () => {
    const load = deferred<Post>()
    const crud = makeCrud(() => load.promise)

    // What a restored `?detail=edit:<id>` does: open straight on the list row.
    crud.formModal.open('edit', row)
    await nextTick()
    expect(crud.formModal.submitting.value).toBe(true)
    expect(await crud.submit()).toBeNull()

    load.resolve({ ...row, body: 'the body' })
    await flushPromises()

    expect(crud.formModal.formData.value).toEqual({ id: 'p1', title: 'Hello', body: 'the body' })
    expect(crud.formModal.submitting.value).toBe(false)
  })

  it('★ a failed load closes the form open on the incomplete row and reports op "load"', async () => {
    const onError = vi.fn(() => false)
    const crud = makeCrud(async () => {
      throw new Error('boom')
    }, onError)

    crud.formModal.open('edit', row)
    await nextTick()
    await flushPromises()

    expect(onError).toHaveBeenCalledWith(expect.objectContaining({ message: 'boom' }), 'load')
    expect(crud.formModal.visible.value).toBe(false)
    expect(crud.formModal.submitting.value).toBe(false)
  })

  it('a load still in flight when the form is closed does not reopen it', async () => {
    const load = deferred<Post>()
    const crud = makeCrud(() => load.promise)

    crud.formModal.open('edit', row)
    await nextTick()
    crud.formModal.close()
    await nextTick()

    load.resolve({ ...row, body: 'late' })
    await flushPromises()

    expect(crud.formModal.visible.value).toBe(false)
  })

  it('a create form opened while an edit loads is left alone', async () => {
    const load = deferred<Post>()
    const crud = makeCrud(() => load.promise)

    crud.openEdit(row)
    crud.openCreate({ title: 'New' })
    load.resolve({ ...row, body: 'late' })
    await flushPromises()

    expect(crud.formModal.mode.value).toBe('create')
    expect(crud.formModal.formData.value).toMatchObject({ title: 'New' })
  })
})
