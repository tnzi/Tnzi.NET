/**
 * Shared mapping helpers used by all module bridges.
 *
 * Introduced in Phase 3 Task 3.6 — centralises the universal query+result
 * adapters so subsequent bridge fills import rather than copy.
 *
 * 0.2.72+ (C4): `CrudPageResult<T>` is now an alias for `@tnzi/core`'s
 * `PagedList<T>`. `mapResultToCrud` upgrades the legacy 4-field shape into the
 * full PagedList by computing `totalPages` / `hasPreviousPage` / `hasNextPage`
 * so downstream consumers see a consistent surface.
 *
 * The envelope helpers `unwrapResult` / `ensureOk` moved to `@tnzi/core`
 * (`http/response`) — the ApiResult contract is core's, not admin-specific — and
 * are re-exported here so the built-in bridges keep their `from './_mappers'`
 * imports unchanged and consumer bridges can pull the whole plumbing set from
 * one place (`@tnzi/ui-admin` or `@tnzi/core`).
 */
import { createPagedList, ensureOk, unwrapResult } from '@tnzi/core'
import type { PagedList } from '@tnzi/core'
import type { CrudPageQuery, CrudPageResult } from './types'

export { ensureOk, unwrapResult }

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

/**
 * Adapter from the loose backend-shaped paged response (4 fields:
 * items / total / pageIndex / pageSize) to the strict
 * `PagedList<T>` shape (adds totalPages / hasPreviousPage / hasNextPage).
 *
 * Accepts either `total` (legacy / backend serialisation pre-mapping) or
 * `totalCount` (already-aligned shape) so call sites can pass through a
 * raw API response or a partially-mapped one.
 */
export function mapResultToCrud<T>(
  result:
    | { items: T[]; total: number; pageIndex: number; pageSize: number }
    | { items: T[]; totalCount: number; pageIndex: number; pageSize: number },
): CrudPageResult<T> {
  const totalCount =
    (result as { totalCount?: number }).totalCount ??
    (result as { total: number }).total
  return createPagedList(result.items, totalCount, result.pageIndex, result.pageSize)
}

/**
 * 0.2.72+ (C4): Wrap a freshly-built 4-field paged record into a full
 * `PagedList<T>`. Lighter than `mapResultToCrud` because the input is
 * already aligned with the destination contract — call sites just want
 * the `totalPages` / `hasPreviousPage` / `hasNextPage` fill-in.
 *
 * Used by bridges that build the result inline from heterogeneous backend
 * shapes and don't have a single `mapResultToCrud`-suitable response
 * object handy.
 */
export function pagedResult<T>(input: {
  items: T[]
  totalCount: number
  pageIndex: number
  pageSize: number
}): PagedList<T> {
  return createPagedList(input.items, input.totalCount, input.pageIndex, input.pageSize)
}

/**
 * Client-side pagination helper for APIs that return flat arrays (not paged lists).
 * Used by authorization and other bridges where the backend has no paged endpoint.
 */
export function pageArray<T>(items: T[], query: CrudPageQuery): PagedList<T> {
  const { pageIndex = 1, pageSize = 20, searchText } = query
  const filtered = searchText
    ? items.filter((item) => JSON.stringify(item).toLowerCase().includes(searchText.toLowerCase()))
    : items
  const start = (pageIndex - 1) * pageSize
  return createPagedList(
    filtered.slice(start, start + pageSize),
    filtered.length,
    pageIndex,
    pageSize,
  )
}
