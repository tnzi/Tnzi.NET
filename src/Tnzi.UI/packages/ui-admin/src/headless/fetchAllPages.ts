import type { CrudPageQuery, CrudPageResult } from './useCrudPage'

/**
 * Page through a `fetch(query)` bridge method until the server says there is
 * nothing more, and return every row.
 *
 * ★ Why this exists: the backend's `PagedQueryDto` clamps `PageSize` to its
 * `MaxPageSize` (100 by default) **silently** - the getter returns the clamped
 * value, no error, no header. A picker that asked for `pageSize: 500` received
 * 100 rows, `totalCount: 150`, `hasNextPage: true`, and rendered a select that
 * simply did not contain customers 101-150. Every option loader in this package
 * did exactly that (2026-09-12 audit). Asking for a bigger page is not a fix;
 * the honest page size is whatever the server honours, so this loops.
 *
 * Termination never trusts the requested page size: it stops when the server
 * reports no next page, when the collected count reaches `totalCount`, when a
 * page comes back empty, when a page repeats the previous one (a server that
 * ignores `pageIndex` would otherwise loop to `max`), or when `max` rows have
 * been collected. A rejected page rejects the whole call - a partial list
 * returned as if complete is the defect this helper replaces.
 *
 * Use {@link fetchAllPagesDetailed} when the caller wants to know that the
 * cap was hit (`truncated`) rather than silently getting the first `max` rows.
 */
export interface FetchAllPagesOptions<T> {
  /**
   * Hard cap on rows collected (default 5000). A picker whose universe exceeds
   * it needs a remote-search control, not a longer loop; the cap keeps a
   * runaway loader from pulling a whole table into a `<select>`.
   */
  max?: number
  /**
   * Page size to request. Defaults to 100, the backend's default clamp, so the
   * loop makes the fewest calls the server will honour. A server that honours
   * more simply returns more per page; a server that honours less is what the
   * loop is for.
   */
  pageSize?: number
  /**
   * Identity of a row, used to detect a server that ignores `pageIndex` and
   * returns the same page forever. Defaults to `id` when present, otherwise
   * JSON of the row.
   */
  rowKey?: (row: T) => unknown
}

export interface FetchAllPagesResult<T> {
  items: T[]
  /** `totalCount` as reported by the last page fetched. */
  totalCount: number
  /** True when `max` was reached before the server ran out of rows. */
  truncated: boolean
}

const DEFAULT_MAX = 5000
const DEFAULT_PAGE_SIZE = 100

function defaultRowKey(row: unknown): unknown {
  if (row && typeof row === 'object' && 'id' in row) return (row as { id: unknown }).id
  return JSON.stringify(row)
}

export async function fetchAllPagesDetailed<T>(
  fetch: (query: CrudPageQuery) => Promise<CrudPageResult<T>>,
  baseQuery: Partial<CrudPageQuery> = {},
  options: FetchAllPagesOptions<T> = {},
): Promise<FetchAllPagesResult<T>> {
  const max = options.max ?? DEFAULT_MAX
  const pageSize = options.pageSize ?? DEFAULT_PAGE_SIZE
  const rowKey = options.rowKey ?? defaultRowKey

  const items: T[] = []
  let totalCount = 0
  let pageIndex = 1
  let previousFirstKey: unknown = undefined
  let hasFirst = false

  for (;;) {
    const page = await fetch({ searchText: '', filters: {}, ...baseQuery, pageIndex, pageSize })
    const rows = page.items ?? []
    totalCount = page.totalCount ?? totalCount

    if (rows.length === 0) break

    const firstKey = rowKey(rows[0] as T)
    if (hasFirst && firstKey === previousFirstKey) break
    previousFirstKey = firstKey
    hasFirst = true

    const room = max - items.length
    if (rows.length >= room) {
      items.push(...rows.slice(0, room))
      const moreOnServer = page.hasNextPage === true || items.length < totalCount || rows.length > room
      return { items, totalCount: Math.max(totalCount, items.length), truncated: moreOnServer }
    }
    items.push(...rows)

    const noNext = page.hasNextPage === false
    const reachedTotal = totalCount > 0 && items.length >= totalCount
    if (noNext || reachedTotal) break
    pageIndex += 1
  }

  return { items, totalCount: Math.max(totalCount, items.length), truncated: false }
}

/** {@link fetchAllPagesDetailed} without the metadata - the common picker case. */
export async function fetchAllPages<T>(
  fetch: (query: CrudPageQuery) => Promise<CrudPageResult<T>>,
  baseQuery: Partial<CrudPageQuery> = {},
  options: FetchAllPagesOptions<T> = {},
): Promise<T[]> {
  return (await fetchAllPagesDetailed(fetch, baseQuery, options)).items
}
