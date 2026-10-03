import { describe, it, expect, vi } from 'vitest'
import { useViewedRecord } from '../../../src/pages/finance/viewed-record'

/**
 * The full record behind a finance list page's `#detail` drawer. The load that
 * matters is the LAST one: opening B while A is still in flight must not let
 * A's late answer (or A's failure) land in B's drawer.
 */

function deferred<T>() {
  let resolve!: (v: T) => void
  let reject!: (e: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('useViewedRecord', () => {
  it('holds the loaded record', async () => {
    const viewing = useViewedRecord(async (id: string) => ({ id }), vi.fn())
    await viewing.load('a')
    expect(viewing.record.value).toEqual({ id: 'a' })
  })

  it('clears the previous record while the next one loads', async () => {
    const next = deferred<{ id: string }>()
    const getById = vi.fn(async (id: string) => (id === 'a' ? { id } : next.promise))
    const viewing = useViewedRecord(getById, vi.fn())
    await viewing.load('a')

    const pending = viewing.load('b')
    expect(viewing.record.value).toBeNull()
    next.resolve({ id: 'b' })
    await pending
    expect(viewing.record.value).toEqual({ id: 'b' })
  })

  it('ignores a slower earlier load that settles after a later one', async () => {
    const first = deferred<{ id: string }>()
    const getById = vi.fn(async (id: string) => (id === 'a' ? first.promise : { id }))
    const onError = vi.fn()
    const viewing = useViewedRecord(getById, onError)

    const slow = viewing.load('a')
    await viewing.load('b')
    first.resolve({ id: 'a' })
    await slow

    expect(viewing.record.value).toEqual({ id: 'b' })
  })

  it('reports a failure of the current load but not of a superseded one', async () => {
    const first = deferred<{ id: string }>()
    const onError = vi.fn()
    const viewing = useViewedRecord(
      vi.fn(async (id: string) => {
        if (id === 'a') return first.promise
        throw new Error('not found')
      }),
      onError,
    )

    const stale = viewing.load('a')
    await viewing.load('b')
    expect(onError).toHaveBeenCalledWith('not found')

    first.reject(new Error('stale failure'))
    await stale
    expect(onError).toHaveBeenCalledTimes(1)
    expect(viewing.record.value).toBeNull()
  })

  it('does not call the loader for an empty id', async () => {
    const getById = vi.fn(async (id: string) => ({ id }))
    const viewing = useViewedRecord(getById, vi.fn())
    await viewing.load('')
    expect(getById).not.toHaveBeenCalled()
    expect(viewing.record.value).toBeNull()
  })
})
