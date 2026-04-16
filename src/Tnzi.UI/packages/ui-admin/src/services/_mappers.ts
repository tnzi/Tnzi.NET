/**
 * Shared mapping helpers used by all module bridges.
 * Introduced in Phase 3 Task 3.6 — centralises the two universal adapters
 * so subsequent bridge fills (3.11 / 3.17 / 3.21 / …) import rather than copy.
 */
import type { CrudPageQuery, CrudPageResult } from './types'

export function mapQueryToListRequest(query: CrudPageQuery): Record<string, unknown> {
  return {
    pageIndex: query.pageIndex,
    pageSize: query.pageSize,
    keyword: query.searchText || undefined,
    sortBy: query.sortField,
    sortOrder: query.sortOrder ?? undefined,
    ...query.filters,
  }
}

export function mapResultToCrud<T>(result: {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}): CrudPageResult<T> {
  return {
    items: result.items,
    totalCount: result.total,
    pageIndex: result.pageIndex,
    pageSize: result.pageSize,
  }
}

/**
 * Client-side pagination helper for APIs that return flat arrays (not paged lists).
 * Used by authorization and other bridges where the backend has no paged endpoint.
 */
export function pageArray<T>(items: T[], query: CrudPageQuery): CrudPageResult<T> {
  const { pageIndex = 1, pageSize = 20, searchText } = query
  const filtered = searchText
    ? items.filter((item) => JSON.stringify(item).toLowerCase().includes(searchText.toLowerCase()))
    : items
  const start = (pageIndex - 1) * pageSize
  return {
    items: filtered.slice(start, start + pageSize),
    totalCount: filtered.length,
    pageIndex,
    pageSize,
  }
}
