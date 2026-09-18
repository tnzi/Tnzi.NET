import { describe, it, expect, vi } from 'vitest'
import { fetchAllPages, fetchAllPagesDetailed } from '../../src/headless/fetchAllPages'
import type { CrudPageQuery, CrudPageResult } from '../../src/headless/useCrudPage'

/**
 * The backend's `PagedQueryDto` clamps `PageSize` to `MaxPageSize` (100)
 * silently: a loader that asks for 500 gets 100 rows, `totalCount: 150`,
 * `hasNextPage: true`, and no error anywhere. Every option loader in the
 * package asked for 200 or 500, so a tenant with 150 customers could not
 * pick customers 101-150 in any document editor. This helper pages until
 * the server says there is no more, regardless of what page size it honours.
 */

interface Row { id: number }

/** A server with `total` rows that honours at most `cap` rows per page. */
function clampingServer(total: number, cap = 100) {
  const calls: CrudPageQuery[] = []
  const fetch = vi.fn(async (query: CrudPageQuery): Promise<CrudPageResult<Row>> => {
    calls.push(query)
    const size = Math.min(query.pageSize, cap)
    const start = (query.pageIndex - 1) * size
    const items = Array.from({ length: Math.max(0, Math.min(size, total - start)) }, (_, i) => ({ id: start + i + 1 }))
    const totalPages = Math.ceil(total / size)
    return {
      items,
      totalCount: total,
      pageIndex: query.pageIndex,
      pageSize: size,
      totalPages,
      hasPreviousPage: query.pageIndex > 1,
      hasNextPage: query.pageIndex < totalPages,
    }
  })
  return { fetch, calls }
}

describe('fetchAllPages', () => {
  it('collects every page when the server clamps the requested page size', async () => {
    const server = clampingServer(150)
    // The size travels through OPTIONS: a `pageSize` in the base query is
    // overwritten by the helper's own (an earlier draft passed it there and
    // asked for 100, so this never exercised a clamp).
    const rows = await fetchAllPages(server.fetch, {}, { pageSize: 500 })
    expect(server.calls.map((c) => c.pageSize)).toEqual([500, 500])
    expect(server.calls.map((c) => c.pageIndex)).toEqual([1, 2])
    expect(rows.map((r) => r.id)).toEqual(Array.from({ length: 150 }, (_, i) => i + 1))
  })

  it('makes a single call when one page holds everything', async () => {
    const server = clampingServer(30)
    const rows = await fetchAllPages(server.fetch)
    expect(rows).toHaveLength(30)
    expect(server.fetch).toHaveBeenCalledTimes(1)
  })

  it('carries the base query (filters, sort) onto every page', async () => {
    const server = clampingServer(250)
    await fetchAllPages(server.fetch, { filters: { isActive: true }, sortField: 'name', sortOrder: 'asc' })
    expect(server.calls).toHaveLength(3)
    for (const call of server.calls) {
      expect(call.filters).toEqual({ isActive: true })
      expect(call.sortField).toBe('name')
      expect(call.sortOrder).toBe('asc')
      expect(call.searchText).toBe('')
    }
  })

  it('stops on an empty page even if the server keeps claiming a next page', async () => {
    const fetch = vi.fn(async (query: CrudPageQuery): Promise<CrudPageResult<Row>> => ({
      items: query.pageIndex === 1 ? [{ id: 1 }] : [],
      totalCount: 99,
      pageIndex: query.pageIndex,
      pageSize: 100,
      totalPages: 99,
      hasPreviousPage: false,
      hasNextPage: true,
    }))
    const rows = await fetchAllPages(fetch)
    expect(rows).toEqual([{ id: 1 }])
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('stops when a page repeats the previous one (a server that ignores pageIndex)', async () => {
    const fetch = vi.fn(async (): Promise<CrudPageResult<Row>> => ({
      items: [{ id: 1 }, { id: 2 }],
      totalCount: 500,
      pageIndex: 1,
      pageSize: 2,
      totalPages: 250,
      hasPreviousPage: false,
      hasNextPage: true,
    }))
    const rows = await fetchAllPages(fetch, {}, { rowKey: (r) => r.id })
    expect(rows).toEqual([{ id: 1 }, { id: 2 }])
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('honours the row cap and reports the truncation', async () => {
    const server = clampingServer(1000)
    const result = await fetchAllPagesDetailed(server.fetch, {}, { max: 250 })
    expect(result.items).toHaveLength(250)
    expect(result.truncated).toBe(true)
    expect(result.totalCount).toBe(1000)
    expect(server.fetch).toHaveBeenCalledTimes(3)
  })

  it('reports no truncation when everything fit', async () => {
    const server = clampingServer(120)
    const result = await fetchAllPagesDetailed(server.fetch)
    expect(result.items).toHaveLength(120)
    expect(result.truncated).toBe(false)
    expect(result.totalCount).toBe(120)
  })

  it('propagates a rejected page instead of returning a partial list as if complete', async () => {
    const fetch = vi.fn(async (query: CrudPageQuery): Promise<CrudPageResult<Row>> => {
      if (query.pageIndex === 2) throw new Error('Permission denied')
      return { items: [{ id: 1 }], totalCount: 2, pageIndex: 1, pageSize: 1, totalPages: 2, hasPreviousPage: false, hasNextPage: true }
    })
    await expect(fetchAllPages(fetch)).rejects.toThrow('Permission denied')
  })
})
