/**
 * Shared mapping helpers used by all module bridges.
 *
 * Introduced in Phase 3 Task 3.6 - centralises the universal query+result
 * adapters so subsequent bridge fills import rather than copy.
 *
 * 0.2.72+ (C4): `CrudPageResult<T>` is now an alias for `@tnzi/core`'s
 * `PagedList<T>`. `mapResultToCrud` upgrades the legacy 4-field shape into the
 * full PagedList by computing `totalPages` / `hasPreviousPage` / `hasNextPage`
 * so downstream consumers see a consistent surface.
 *
 * The envelope helpers `unwrapUnchecked` / `ensureOk` moved to `@tnzi/core`
 * (`http/response`) - the ApiResult contract is core's, not admin-specific - and
 * are re-exported here so the built-in bridges keep their `from './_mappers'`
 * imports unchanged and consumer bridges can pull the whole plumbing set from
 * one place (`@tnzi/ui-admin` or `@tnzi/core`).
 */
import { createPagedList, ensureOk, unwrapOk, unwrapUnchecked } from '@tnzi/core'
import type { PagedList } from '@tnzi/core'
import type { CrudPageQuery, CrudPageResult } from './types'
import { filterByKeyword } from '../utils/keyword-filter'

// `unwrapOk` = ensureOk + unwrapUnchecked. Bridges use it on every WRITE so a
// business refusal (400 + failed envelope) throws instead of resolving to
// `null` and being announced as "saved"; `__tests__/services/bridge-write-unwrap.test.ts`
// scans the bridges for write-verb calls that still go through bare `unwrap`.
// `unwrapResult` is core's deprecated alias for `unwrapUnchecked`; bridges import it
// as `unwrap` for reads. Re-exported under BOTH names so the bridge-write gate can
// match the real name as well as the alias - it must not depend on an import style.
export { ensureOk, unwrapOk, unwrapUnchecked, unwrapUnchecked as unwrapResult }

export function mapQueryToListRequest(query: CrudPageQuery): Record<string, unknown> {
  return {
    pageIndex: query.pageIndex,
    pageSize: query.pageSize,
    keyword: query.searchText || undefined,
    sortBy: query.sortField,
    // `sortBy` + `sortDescending` is what the user list reads, the one admin
    // list with server-side sorting. Most list endpoints take no sort
    // parameters at all (fixed order), and the admin file query reads its
    // direction as `sortOrder` / `descending` instead (see `FileQueryDto`), so
    // a page that adds a sortable column has to map the direction for its own
    // endpoint. Omitted entirely when no direction is set, so the backend keeps
    // its own default.
    sortDescending: query.sortOrder ? query.sortOrder === 'desc' : undefined,
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
 * already aligned with the destination contract - call sites just want
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

/** A row's own text values: what a keyword is matched against when no fields are named. */
function ownTextValues(item: unknown): string[] {
  if (item == null || typeof item !== 'object') return typeof item === 'string' ? [item] : []
  return Object.values(item).filter((value): value is string => typeof value === 'string')
}

/**
 * Client-side pagination helper for APIs that return flat arrays (not paged lists).
 * Used by authorization and other bridges where the backend has no paged endpoint.
 *
 * `query.searchText` (trimmed, case-insensitive) filters the rows first.
 * `fields` names the text it is matched against, normally what the page shows
 * for a row; without it the row's own string values are searched. Property
 * names, numbers, booleans and nested objects are never matched: a keyword
 * such as "name" or "true" would otherwise keep every row.
 */
export function pageArray<T>(
  items: T[],
  query: CrudPageQuery,
  fields: (row: T) => ReadonlyArray<string | null | undefined> = ownTextValues,
): PagedList<T> {
  const { pageIndex = 1, pageSize = 20, searchText } = query
  const filtered = filterByKeyword(items, searchText, fields)
  const start = (pageIndex - 1) * pageSize
  return createPagedList(
    filtered.slice(start, start + pageSize),
    filtered.length,
    pageIndex,
    pageSize,
  )
}

/** The module is absent (its routes are not mapped) or switched off. */
const MODULE_ABSENT_CODES = new Set([404, 501])

/**
 * A read of an optional module's endpoint: its data, or `absent` when the
 * module is not available here (404 / 501). Every other failed envelope (a
 * 400, a 403, a 5xx) throws with the server's reason, so a page can tell
 * "this module is not loaded" from "this read was refused".
 */
export function readOrAbsent<T>(response: unknown, absent: T): T {
  if (response == null) return absent
  const envelope = response as { succeeded?: boolean; success?: boolean; code?: number; data?: T | null }
  // A bare payload (an API that answers without the envelope) is the data itself.
  if (typeof response !== 'object' || !('succeeded' in envelope || 'success' in envelope)) return response as T
  if (envelope.succeeded ?? envelope.success) return envelope.data ?? absent
  if (MODULE_ABSENT_CODES.has(Number(envelope.code))) return absent
  ensureOk(response)
  return absent
}
