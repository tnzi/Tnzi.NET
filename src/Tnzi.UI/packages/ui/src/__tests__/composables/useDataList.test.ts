import { describe, it, expect, vi } from 'vitest'
import { useDataList } from '../../composables/useDataList'

describe('useDataList', () => {
  it('should initialize with default values', () => {
    const list = useDataList()
    expect(list.currentPage.value).toBe(1)
    expect(list.pageSize.value).toBe(10)
    expect(list.total.value).toBe(0)
    expect(list.totalPages.value).toBe(0)
    expect(list.hasPager.value).toBe(false)
    expect(list.isEmpty.value).toBe(true)
    expect(list.isLoading.value).toBe(false)
  })

  it('should initialize from pager config', () => {
    const list = useDataList({
      pager: { pageIndex: 3, pageSize: 25, total: 100 },
    })
    expect(list.currentPage.value).toBe(3)
    expect(list.pageSize.value).toBe(25)
    expect(list.total.value).toBe(100)
    expect(list.hasPager.value).toBe(true)
  })

  it('should compute totalPages correctly', () => {
    const list = useDataList({
      pager: { pageIndex: 1, pageSize: 10, total: 45 },
    })
    expect(list.totalPages.value).toBe(5)
  })

  it('should clamp page on goPage (too high)', () => {
    const list = useDataList({
      pager: { pageIndex: 1, pageSize: 10, total: 30 },
    })
    list.goPage(999)
    expect(list.currentPage.value).toBe(3)
  })

  it('should clamp page on goPage (too low)', () => {
    const list = useDataList({
      pager: { pageIndex: 1, pageSize: 10, total: 30 },
    })
    list.goPage(0)
    expect(list.currentPage.value).toBe(1)
  })

  it('should fire onPageChange callback', () => {
    let changedPage = 0
    let changedSize = 0
    const list = useDataList({
      pager: { pageIndex: 1, pageSize: 10, total: 50 },
      onPageChange: (page, size) => { changedPage = page; changedSize = size },
    })
    list.goPage(3)
    expect(changedPage).toBe(3)
    expect(changedSize).toBe(10)
  })

  it('should fire onUpdateQuery callback on goPage', () => {
    const onUpdateQuery = vi.fn()
    const list = useDataList({
      query: { keyword: 'test' },
      pager: { pageIndex: 1, pageSize: 10, total: 50 },
      onUpdateQuery,
    })
    list.goPage(2)
    expect(onUpdateQuery).toHaveBeenCalledWith(
      expect.objectContaining({ pageIndex: 2, pageSize: 10, keyword: 'test' }),
    )
  })

  it('should resolve key with string itemKey', () => {
    const list = useDataList({ itemKey: 'id' })
    expect(list.resolveKey({ id: 'abc' } as any, 0)).toBe('abc')
  })

  it('should resolve key with function itemKey', () => {
    const list = useDataList({
      itemKey: (item: any, idx: number) => `${item.name}-${idx}`,
    })
    expect(list.resolveKey({ name: 'foo' } as any, 2)).toBe('foo-2')
  })

  it('should fallback to index when itemKey string field is missing', () => {
    const list = useDataList({ itemKey: 'id' })
    expect(list.resolveKey({ name: 'no-id' } as any, 5)).toBe('5')
  })

  it('should fallback to index when no itemKey specified', () => {
    const list = useDataList()
    expect(list.resolveKey({ id: 'x' } as any, 7)).toBe('7')
  })

  it('should compute isEmpty correctly', () => {
    const list = useDataList({ items: [] })
    expect(list.isEmpty.value).toBe(true)

    const list2 = useDataList({ items: [{ a: 1 }] })
    expect(list2.isEmpty.value).toBe(false)
  })

  it('should not be empty when loading', () => {
    const list = useDataList({ items: [], loadState: { loading: true } })
    expect(list.isEmpty.value).toBe(false)
    expect(list.isLoading.value).toBe(true)
  })

  it('should compute hasPager as false when pager is false', () => {
    const list = useDataList({ pager: false })
    expect(list.hasPager.value).toBe(false)
  })

  it('should compute hasPager as false when pager is undefined', () => {
    const list = useDataList()
    expect(list.hasPager.value).toBe(false)
  })

  it('should call onRefresh callback', () => {
    const onRefresh = vi.fn()
    const list = useDataList({ onRefresh })
    list.onRefresh()
    expect(onRefresh).toHaveBeenCalledOnce()
  })

  it('should use items length as total when no pager', () => {
    const list = useDataList({ items: [{ a: 1 }, { b: 2 }, { c: 3 }] })
    expect(list.total.value).toBe(3)
  })
})
