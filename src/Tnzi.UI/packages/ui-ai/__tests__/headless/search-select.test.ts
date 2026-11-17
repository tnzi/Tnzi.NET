/**
 * The options a `TSearchSelect` lists: filtered locally, or loaded a page at a time with the
 * query debounced, superseded requests aborted and their late answers dropped, and failures
 * retried for the page that failed.
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  createSearchSelectSource,
  searchSelectMatches,
  type SearchSelectLoadContext,
  type SearchSelectPage,
  type TSearchSelectOption,
} from '../../src/components/controls/search-select'

const PEOPLE: TSearchSelectOption<number>[] = [
  { value: 1, label: 'Zoë Martin', description: 'zoe@example.com' },
  { value: 2, label: 'Ada Lovelace' },
  { value: 3, label: 'Alan Turing', disabled: true },
]

describe('searchSelectMatches', () => {
  it('matches label or description, ignoring case and accents; an empty query matches all', () => {
    expect(searchSelectMatches(PEOPLE[0]!, 'zoe')).toBe(true)
    expect(searchSelectMatches(PEOPLE[0]!, 'EXAMPLE')).toBe(true)
    expect(searchSelectMatches(PEOPLE[1]!, 'turing')).toBe(false)
    expect(searchSelectMatches(PEOPLE[1]!, '  ')).toBe(true)
  })
})

describe('createSearchSelectSource, local', () => {
  it('filters the options it was given as the query changes', () => {
    const source = createSearchSelectSource({ options: () => PEOPLE, load: () => undefined, debounce: () => 0 })
    source.refresh()
    expect(source.items.value).toHaveLength(3)
    source.search('a')
    expect(source.items.value.map((o) => o.value)).toEqual([1, 2, 3])
    source.search('ada')
    expect(source.items.value.map((o) => o.value)).toEqual([2])
    expect(source.hasMore.value).toBe(false)
    source.loadMore()
    expect(source.items.value.map((o) => o.value)).toEqual([2])
  })
})

describe('createSearchSelectSource, loaded', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  type Call = { query: string; context: SearchSelectLoadContext; resolve: (page: SearchSelectPage<number>) => void; reject: (e: unknown) => void }

  function loaded(debounce = 200) {
    const calls: Call[] = []
    const load = vi.fn((query: string, context: SearchSelectLoadContext) =>
      new Promise<SearchSelectPage<number>>((resolve, reject) => calls.push({ query, context, resolve, reject })))
    const source = createSearchSelectSource<number>({ options: () => undefined, load: () => load, debounce: () => debounce })
    return { calls, load, source }
  }

  const page = (from: number, count: number, hasMore: boolean): SearchSelectPage<number> => ({
    options: Array.from({ length: count }, (_, i) => ({ value: from + i, label: `Row ${from + i}` })),
    hasMore,
  })

  it('loads the first page at once on refresh, then further pages from their offset', async () => {
    const { calls, source } = loaded()
    source.refresh()
    expect(source.loading.value).toBe(true)
    expect(calls[0]!.context.offset).toBe(0)
    calls[0]!.resolve(page(0, 20, true))
    await vi.runAllTimersAsync()
    expect(source.items.value).toHaveLength(20)
    expect(source.loading.value).toBe(false)
    expect(source.hasMore.value).toBe(true)

    source.loadMore()
    source.loadMore() // one page at a time
    expect(calls).toHaveLength(2)
    expect(source.loadingMore.value).toBe(true)
    expect(calls[1]!.context.offset).toBe(20)
    calls[1]!.resolve(page(20, 5, false))
    await vi.runAllTimersAsync()
    expect(source.items.value).toHaveLength(25)
    expect(source.hasMore.value).toBe(false)
    source.loadMore()
    expect(calls).toHaveLength(2)
  })

  it('waits for the typing to pause, aborts what it supersedes and drops its late answer', async () => {
    const { calls, load, source } = loaded(200)
    source.refresh()
    source.search('a')
    expect(source.loading.value).toBe(true)
    expect(calls[0]!.context.signal.aborted).toBe(true)
    source.search('ad')
    vi.advanceTimersByTime(199)
    expect(load).toHaveBeenCalledTimes(1)
    vi.advanceTimersByTime(1)
    expect(load).toHaveBeenCalledTimes(2)
    expect(calls[1]!.query).toBe('ad')
    source.search('ada')
    await vi.advanceTimersByTimeAsync(200)
    expect(calls[1]!.context.signal.aborted).toBe(true)

    calls[1]!.resolve(page(100, 3, false)) // late: dropped
    calls[0]!.resolve(page(200, 3, false)) // late: dropped
    calls[2]!.resolve(page(1, 1, false))
    await vi.runAllTimersAsync()
    expect(source.items.value.map((o) => o.value)).toEqual([1])
  })

  it('shows a failed first page as an error, and Retry asks again', async () => {
    const { calls, source } = loaded()
    source.refresh()
    calls[0]!.reject(new Error('offline'))
    await vi.runAllTimersAsync()
    expect(source.error.value).toBeInstanceOf(Error)
    expect(source.items.value).toEqual([])
    source.retry()
    expect(calls[1]!.context.offset).toBe(0)
    calls[1]!.resolve(page(0, 2, false))
    await vi.runAllTimersAsync()
    expect(source.error.value).toBeNull()
    expect(source.items.value).toHaveLength(2)
  })

  it('keeps the list when a further page fails, and Retry asks for that page', async () => {
    const { calls, source } = loaded()
    source.refresh()
    calls[0]!.resolve(page(0, 10, true))
    await vi.runAllTimersAsync()
    source.loadMore()
    calls[1]!.reject(new Error('offline'))
    await vi.runAllTimersAsync()
    expect(source.items.value).toHaveLength(10)
    expect(source.error.value).not.toBeNull()
    source.loadMore() // not while the failure shows
    expect(calls).toHaveLength(2)
    source.retry()
    expect(calls[2]!.context.offset).toBe(10)
  })

  it('stops what is in flight and forgets the query on reset', () => {
    const { calls, load, source } = loaded()
    source.refresh()
    source.search('x')
    source.reset()
    expect(calls[0]!.context.signal.aborted).toBe(true)
    vi.advanceTimersByTime(1000)
    expect(load).toHaveBeenCalledTimes(1)
    expect(source.query.value).toBe('')
    expect(source.loading.value).toBe(false)
  })
})
