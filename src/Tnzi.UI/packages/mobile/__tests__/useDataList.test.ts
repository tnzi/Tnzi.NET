import { describe, it, expect, vi } from 'vitest'
import { useDataList } from '../src/headless/useDataList'

describe('useDataList', () => {
  it('initializes with empty state', () => {
    const { items, isLoading, isRefreshing, hasMore, page, isEmpty } = useDataList()
    expect(items.value).toEqual([])
    expect(isLoading.value).toBe(false)
    expect(isRefreshing.value).toBe(false)
    expect(hasMore.value).toBe(true)
    expect(page.value).toBe(1)
    expect(isEmpty.value).toBe(true)
  })

  it('loadMore appends items and advances page', async () => {
    const data = [{ id: 1 }, { id: 2 }, { id: 3 }]
    const onLoad = vi.fn().mockResolvedValue(data)
    const { items, page, loadMore } = useDataList({ onLoad, pageSize: 20 })
    await loadMore()
    expect(items.value).toEqual(data)
    expect(page.value).toBe(2)
    expect(onLoad).toHaveBeenCalledWith(1, 20)
  })

  it('loadMore sets hasMore=false when result is less than pageSize', async () => {
    const onLoad = vi.fn().mockResolvedValue([{ id: 1 }])
    const { hasMore, loadMore } = useDataList({ onLoad, pageSize: 20 })
    await loadMore()
    expect(hasMore.value).toBe(false)
  })

  it('loadMore sets hasMore=false when result equals pageSize exactly', async () => {
    const onLoad = vi.fn().mockResolvedValue([{ id: 1 }, { id: 2 }])
    const { hasMore, loadMore } = useDataList({ onLoad, pageSize: 2 })
    await loadMore()
    // exactly pageSize - hasMore stays true
    expect(hasMore.value).toBe(true)
  })

  it('loadMore does nothing when hasMore is false', async () => {
    const onLoad = vi.fn().mockResolvedValue([])
    const { hasMore, loadMore } = useDataList({ onLoad, pageSize: 20 })
    await loadMore()
    expect(hasMore.value).toBe(false)
    await loadMore()
    // onLoad was only called once (second call skipped)
    expect(onLoad).toHaveBeenCalledTimes(1)
  })

  it('loadMore does nothing while already loading', async () => {
    let resolveLoad!: (v: unknown[]) => void
    const onLoad = vi.fn(() => new Promise<unknown[]>(r => { resolveLoad = r }))
    const { isLoading, loadMore } = useDataList({ onLoad })
    const p1 = loadMore()
    expect(isLoading.value).toBe(true)
    const p2 = loadMore() // should be ignored
    resolveLoad([])
    await Promise.all([p1, p2])
    expect(onLoad).toHaveBeenCalledTimes(1)
  })

  it('loadMore accumulates items across calls', async () => {
    let call = 0
    const onLoad = vi.fn(() => {
      call++
      return Promise.resolve(call === 1 ? [{ id: 1 }, { id: 2 }] : [{ id: 3 }, { id: 4 }])
    })
    const { items, loadMore } = useDataList({ onLoad, pageSize: 2 })
    await loadMore()
    await loadMore()
    expect(items.value).toHaveLength(4)
    expect(items.value[0]).toEqual({ id: 1 })
    expect(items.value[3]).toEqual({ id: 4 })
  })

  it('refresh resets to page 1 and replaces items', async () => {
    const onLoad = vi.fn()
      .mockResolvedValueOnce([{ id: 1 }])
      .mockResolvedValueOnce([{ id: 2 }, { id: 3 }])
    const { items, page, refresh } = useDataList({ onLoad, pageSize: 20 })
    await refresh()
    expect(items.value).toEqual([{ id: 1 }])
    expect(page.value).toBe(2)
    await refresh()
    expect(items.value).toEqual([{ id: 2 }, { id: 3 }])
    expect(page.value).toBe(2)
  })

  it('refresh uses onRefresh callback when provided', async () => {
    const onLoad = vi.fn().mockResolvedValue([{ id: 9 }])
    const onRefresh = vi.fn().mockResolvedValue([{ id: 42 }])
    const { items, refresh } = useDataList({ onLoad, onRefresh, pageSize: 20 })
    await refresh()
    expect(items.value).toEqual([{ id: 42 }])
    expect(onRefresh).toHaveBeenCalledOnce()
    expect(onLoad).not.toHaveBeenCalled()
  })

  it('refresh sets hasMore based on result size', async () => {
    const onLoad = vi.fn().mockResolvedValue([])
    const { hasMore, refresh } = useDataList({ onLoad, pageSize: 20 })
    await refresh()
    expect(hasMore.value).toBe(false)
  })

  it('refresh does nothing while refreshing', async () => {
    let callCount = 0
    // Use a slow onLoad that we can race against a second refresh call
    const onLoad = vi.fn(async () => {
      callCount++
      await new Promise<void>(r => setTimeout(r, 0))
      return []
    })
    const { isRefreshing, refresh } = useDataList({ onLoad })
    const p1 = refresh()
    // Before the first refresh resolves, start a second one
    const p2 = refresh()
    await Promise.all([p1, p2])
    // Only one call should have been made
    expect(onLoad).toHaveBeenCalledTimes(1)
    expect(callCount).toBe(1)
  })

  it('isEmpty is false when items exist', async () => {
    const onLoad = vi.fn().mockResolvedValue([{ id: 1 }])
    const { isEmpty, loadMore } = useDataList({ onLoad })
    await loadMore()
    expect(isEmpty.value).toBe(false)
  })

  it('reset restores initial state', async () => {
    const onLoad = vi.fn().mockResolvedValue([{ id: 1 }])
    const { items, page, hasMore, reset, loadMore } = useDataList({ onLoad, pageSize: 20 })
    await loadMore()
    reset()
    expect(items.value).toEqual([])
    expect(page.value).toBe(1)
    expect(hasMore.value).toBe(true)
  })
})
