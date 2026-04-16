import { describe, it, expect, vi } from 'vitest'
import { useDataList } from '../../../src/composables/data/useDataList'

function makeFetcher(pages: Record<number, { items: string[]; totalCount: number }>) {
  return vi.fn(async ({ pageIndex }: { pageIndex: number }) => {
    return pages[pageIndex] ?? { items: [], totalCount: 0 }
  })
}

describe('useDataList', () => {
  it('defaults initial state and loads immediately', async () => {
    const fetcher = makeFetcher({ 1: { items: ['a', 'b'], totalCount: 2 } })
    const list = useDataList<string>({ fetcher })
    await vi.waitFor(() => expect(list.items.value.length).toBe(2))
    expect(list.pageSize.value).toBe(20)
    expect(list.totalCount.value).toBe(2)
    expect(list.loading.value).toBe(false)
    expect(list.hasMore.value).toBe(false)
    expect(list.error.value).toBeNull()
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('honors initialPageSize', async () => {
    const fetcher = makeFetcher({ 1: { items: [], totalCount: 0 } })
    const list = useDataList<string>({ fetcher, initialPageSize: 5, immediate: false })
    expect(list.pageSize.value).toBe(5)
  })

  it('does not fetch when immediate=false', () => {
    const fetcher = makeFetcher({ 1: { items: ['a'], totalCount: 1 } })
    useDataList<string>({ fetcher, immediate: false })
    expect(fetcher).not.toHaveBeenCalled()
  })

  describe('load()', () => {
    it('replaces items when not append mode', async () => {
      const fetcher = makeFetcher({ 1: { items: ['a'], totalCount: 1 } })
      const list = useDataList<string>({ fetcher, immediate: false })
      await list.load()
      expect(list.items.value).toEqual(['a'])
    })

    it('sets error and forwards to onError on rejection', async () => {
      const onError = vi.fn()
      const fetcher = vi.fn().mockRejectedValue(new Error('boom'))
      const list = useDataList<string>({ fetcher, immediate: false, onError })
      await list.load()
      expect(list.error.value?.message).toBe('boom')
      expect(onError).toHaveBeenCalled()
      expect(list.loading.value).toBe(false)
    })

    it('wraps non-Error rejections', async () => {
      const fetcher = vi.fn().mockRejectedValue('plain')
      const list = useDataList<string>({ fetcher, immediate: false })
      await list.load()
      expect(list.error.value?.message).toBe('plain')
    })

    it('clears previous error on success', async () => {
      const fetcher = vi.fn()
        .mockRejectedValueOnce(new Error('x'))
        .mockResolvedValueOnce({ items: ['a'], totalCount: 1 })
      const list = useDataList<string>({ fetcher, immediate: false })
      await list.load()
      expect(list.error.value).not.toBeNull()
      await list.load()
      expect(list.error.value).toBeNull()
    })
  })

  describe('loadMore() (append mode)', () => {
    it('appends subsequent pages and advances pageIndex', async () => {
      const fetcher = makeFetcher({
        1: { items: ['a', 'b'], totalCount: 4 },
        2: { items: ['c', 'd'], totalCount: 4 },
      })
      const list = useDataList<string>({ fetcher, appendMode: true, immediate: false, initialPageSize: 2 })
      await list.load()
      expect(list.items.value).toEqual(['a', 'b'])
      expect(list.hasMore.value).toBe(true)
      await list.loadMore()
      expect(list.items.value).toEqual(['a', 'b', 'c', 'd'])
      expect(list.pageIndex.value).toBe(2)
      expect(list.hasMore.value).toBe(false)
    })

    it('returns early when no more items', async () => {
      const fetcher = makeFetcher({ 1: { items: ['a'], totalCount: 1 } })
      const list = useDataList<string>({ fetcher, appendMode: true, immediate: false })
      await list.load()
      await list.loadMore()
      expect(fetcher).toHaveBeenCalledTimes(1)
    })

    it('returns early when already loading', async () => {
      let resolveFirst: (v: any) => void = () => {}
      const fetcher = vi.fn()
        .mockImplementationOnce(() => new Promise((r) => { resolveFirst = r }))
        .mockResolvedValueOnce({ items: [], totalCount: 0 })
      const list = useDataList<string>({ fetcher, appendMode: true, immediate: false })
      const p = list.load()
      const p2 = list.loadMore()
      resolveFirst({ items: ['a'], totalCount: 10 })
      await Promise.all([p, p2])
      expect(fetcher).toHaveBeenCalledTimes(1)
    })
  })

  describe('refresh() & search()', () => {
    it('refresh resets to page 1', async () => {
      const fetcher = makeFetcher({ 1: { items: ['a'], totalCount: 1 } })
      const list = useDataList<string>({ fetcher, immediate: false })
      list.pageIndex.value = 5
      await list.refresh()
      expect(list.pageIndex.value).toBe(1)
    })

    it('search clears items, sets text, resets page', async () => {
      const fetcher = makeFetcher({ 1: { items: ['matched'], totalCount: 1 } })
      const list = useDataList<string>({ fetcher, immediate: false })
      list.items.value = ['old']
      list.pageIndex.value = 3
      await list.search('query')
      expect(list.searchText.value).toBe('query')
      expect(list.pageIndex.value).toBe(1)
      expect(list.items.value).toEqual(['matched'])
    })
  })
})
