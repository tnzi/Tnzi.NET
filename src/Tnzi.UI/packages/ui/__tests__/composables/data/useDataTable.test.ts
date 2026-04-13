import { describe, it, expect, vi } from 'vitest'
import { useDataTable } from '../../../src/composables/data/useDataTable'

describe('useDataTable (data/)', () => {
  const mockItems = Array.from({ length: 25 }, (_, i) => ({ id: i + 1, name: `Item ${i + 1}` }))

  function mockFetcher(items = mockItems) {
    return vi.fn().mockImplementation(async (query: any) => {
      const start = (query.pageIndex - 1) * query.pageSize
      const end = start + query.pageSize
      return {
        items: items.slice(start, end),
        totalCount: items.length,
      }
    })
  }

  it('initializes with default pageIndex 1 and pageSize 10', () => {
    const fetcher = mockFetcher()
    const { pageIndex, pageSize } = useDataTable({ fetcher, immediate: false })
    expect(pageIndex.value).toBe(1)
    expect(pageSize.value).toBe(10)
  })

  it('calls fetcher with current query on reload', async () => {
    const fetcher = mockFetcher()
    const { reload } = useDataTable({ fetcher, immediate: false })
    await reload()
    expect(fetcher).toHaveBeenCalledWith(expect.objectContaining({ pageIndex: 1, pageSize: 10 }))
  })

  it('populates items from fetcher result', async () => {
    const fetcher = mockFetcher()
    const { items, reload } = useDataTable({ fetcher, immediate: false })
    await reload()
    expect(items.value).toHaveLength(10)
    expect(items.value[0]).toEqual({ id: 1, name: 'Item 1' })
  })

  it('populates totalCount from fetcher result', async () => {
    const fetcher = mockFetcher()
    const { totalCount, reload } = useDataTable({ fetcher, immediate: false })
    await reload()
    expect(totalCount.value).toBe(25)
  })

  it('goToPage triggers new fetch with updated pageIndex', async () => {
    const fetcher = mockFetcher()
    const { goToPage, pageIndex, reload } = useDataTable({ fetcher, immediate: false })
    await reload()
    await goToPage(2)
    expect(pageIndex.value).toBe(2)
    expect(fetcher).toHaveBeenLastCalledWith(expect.objectContaining({ pageIndex: 2 }))
  })

  it('setPageSize resets pageIndex to 1 and refetches', async () => {
    const fetcher = mockFetcher()
    const { setPageSize, pageIndex, pageSize } = useDataTable({ fetcher, immediate: false })
    await setPageSize(20)
    expect(pageSize.value).toBe(20)
    expect(pageIndex.value).toBe(1)
  })

  it('sortBy sets sort state and refetches', async () => {
    const fetcher = mockFetcher()
    const { sortBy, sortField, sortOrder } = useDataTable({ fetcher, immediate: false })
    await sortBy('name', 'desc')
    expect(sortField.value).toBe('name')
    expect(sortOrder.value).toBe('desc')
    expect(fetcher).toHaveBeenLastCalledWith(expect.objectContaining({ sortField: 'name', sortOrder: 'desc' }))
  })

  it('toggleSelection adds/removes row id', () => {
    const fetcher = mockFetcher()
    const { toggleSelection, selectedIds } = useDataTable({ fetcher, immediate: false })
    toggleSelection(5)
    expect(selectedIds.value).toContain(5)
    toggleSelection(5)
    expect(selectedIds.value).not.toContain(5)
  })

  it('selectAll adds all current page items', async () => {
    const fetcher = mockFetcher()
    const { selectAll, selectedIds, reload } = useDataTable({ fetcher, immediate: false })
    await reload()
    selectAll()
    expect(selectedIds.value).toHaveLength(10)
  })

  it('clearSelection empties selection', () => {
    const fetcher = mockFetcher()
    const { toggleSelection, clearSelection, selectedIds } = useDataTable({ fetcher, immediate: false })
    toggleSelection(1)
    toggleSelection(2)
    clearSelection()
    expect(selectedIds.value).toHaveLength(0)
  })

  it('loading is true during fetch', async () => {
    let resolveFetch: (v: any) => void = () => {}
    const fetcher = vi.fn().mockImplementation(() => new Promise(resolve => { resolveFetch = resolve }))
    const { loading, reload } = useDataTable({ fetcher, immediate: false })
    const p = reload()
    expect(loading.value).toBe(true)
    resolveFetch({ items: [], totalCount: 0 })
    await p
    expect(loading.value).toBe(false)
  })

  it('handles fetcher error gracefully', async () => {
    const fetcher = vi.fn().mockRejectedValue(new Error('Network error'))
    const onError = vi.fn()
    const { reload, error } = useDataTable({ fetcher, onError, immediate: false })
    await reload()
    expect(onError).toHaveBeenCalled()
    expect(error.value).toBeTruthy()
  })
})
