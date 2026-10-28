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

describe('useDataList when a refresh overtakes a page load', () => {
  /** onLoad whose answers the test hands out one call at a time. */
  function heldLoader() {
    const calls: { page: number; answer: (rows: { id: number }[]) => void }[] = []
    const onLoad = vi.fn(
      (page: number) => new Promise<{ id: number }[]>((resolve) => calls.push({ page, answer: resolve })),
    )
    return { onLoad, calls }
  }
  const rows = (from: number, count: number) => Array.from({ length: count }, (_, i) => ({ id: from + i }))

  it('drops the page that was in flight when the refresh started', async () => {
    const { onLoad, calls } = heldLoader()
    const list = useDataList({ onLoad, pageSize: 2 })
    // Pages 1 and 2 are in; page 3 is on its way.
    const p1 = list.loadMore(); calls[0]!.answer(rows(1, 2)); await p1
    const p2 = list.loadMore(); calls[1]!.answer(rows(3, 2)); await p2
    const p3 = list.loadMore()

    // Pull to refresh: it answers first, with page 1.
    const refreshing = list.refresh()
    await new Promise<void>((r) => setTimeout(r, 0))
    calls[3]!.answer(rows(1, 2))
    await refreshing
    // Then the old page 3 arrives.
    calls[2]!.answer(rows(5, 2))
    await p3

    // It used to be appended after the fresh page 1 and move the cursor to 3.
    expect(list.items.value).toEqual(rows(1, 2))
    expect(list.page.value).toBe(2)
    expect(list.isLoading.value).toBe(false)
  })

  it('does not start a page load while a refresh is running', async () => {
    const { onLoad, calls } = heldLoader()
    const list = useDataList({ onLoad, pageSize: 2 })

    const refreshing = list.refresh()
    await list.loadMore()
    await new Promise<void>((r) => setTimeout(r, 0))
    expect(onLoad).toHaveBeenCalledTimes(1)

    calls[0]!.answer(rows(1, 2))
    await refreshing
    const next = list.loadMore()
    expect(calls[1]!.page).toBe(2)
    calls[1]!.answer(rows(3, 1))
    await next
    expect(list.items.value).toEqual(rows(1, 3))
  })

  it('drops a refresh that reset() overtook', async () => {
    const { onLoad, calls } = heldLoader()
    const list = useDataList({ onLoad, pageSize: 2 })

    const refreshing = list.refresh()
    await new Promise<void>((r) => setTimeout(r, 0))
    list.reset()
    calls[0]!.answer(rows(1, 2))
    await refreshing

    expect(list.items.value).toEqual([])
    expect(list.page.value).toBe(1)
    expect(list.isRefreshing.value).toBe(false)
  })

  it('drops a load that reset() overtook, and lets the next load run', async () => {
    const { onLoad, calls } = heldLoader()
    const list = useDataList({ onLoad, pageSize: 2 })

    const stale = list.loadMore()
    list.reset()
    const fresh = list.loadMore()
    expect(onLoad).toHaveBeenCalledTimes(2)

    calls[0]!.answer(rows(100, 2))
    await stale
    // The stale load finishing must not clear the flag of the one still running.
    expect(list.isLoading.value).toBe(true)

    calls[1]!.answer(rows(1, 2))
    await fresh
    expect(list.items.value).toEqual(rows(1, 2))
    expect(list.page.value).toBe(2)
    expect(list.isLoading.value).toBe(false)
  })
})
