import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import type { PagedList } from '@tnzi/core'
import {
  useColumnSettings,
  type ColumnDef,
  type UseColumnSettingsReturn,
} from './useColumnSettings'
import { useBatchActions, type UseBatchActionsReturn } from './useBatchActions'
import type { UseFormModalReturn } from './useFormModal'
import { useDetail, type DetailMode } from './useDetail'
import { normalizeCrudPermission, canAction, type CrudActionPermissions } from './permissionGates'

/**
 * Search/sort/page query shape used by all `useCrudPage`-backed bridges.
 *
 * Field-level alignment with `@tnzi/core`'s `FullPagedQueryDto` shape:
 *  - `pageIndex` / `pageSize` - identical contract (1-based index, items per page)
 *  - `searchText` - equivalent to FullPagedQueryDto.keyword
 *  - `sortField` + `sortOrder` - equivalent to FullPagedQueryDto.sortBy + sortDescending
 *  - `filters` - ui-admin-specific (FullPagedQueryDto has no
 *                               typed filter bag; pages typically extend it
 *                               via subclassing on the .NET side)
 *
 * The names diverge from FullPagedQueryDto for backward compatibility - 30+
 * bridges + 60+ pages built against the legacy shape. The `_mappers.ts`
 * `mapQueryToListRequest` helper bridges the names when calling the backend.
 */
export interface CrudPageQuery {
  pageIndex: number
  pageSize: number
  searchText: string
  sortField?: string
  sortOrder?: 'asc' | 'desc' | null
  filters: Record<string, unknown>
}

/**
 * 0.2.72+ (C4): `CrudPageResult<T>` is now an alias for `@tnzi/core`'s
 * `PagedList<T>` - a strict superset that also carries `totalPages`,
 * `hasPreviousPage`, `hasNextPage` (handy for pagination UI). The legacy
 * minimal shape (items / totalCount / pageIndex / pageSize) stays valid
 * because PagedList includes those four fields.
 *
 * Bridges that previously returned `{ items, totalCount, pageIndex, pageSize }`
 * keep working - the framework's `mapResultToCrud` helper now fills in
 * the missing `totalPages` / `hasPreviousPage` / `hasNextPage` fields
 * automatically so downstream consumers can rely on the full PagedList
 * surface.
 */
export type CrudPageResult<T> = PagedList<T>

// `CrudActionPermissions` moved to `./permissionGates` (shared with
// `useChildCollection`); imported above and still re-exported via the headless
// barrel, so `{ CrudActionPermissions }` consumers are unaffected.

/**
 * Operation tag passed to `onError` so a single handler can disambiguate
 * which surface failed (e.g. tailor the message or only log writes).
 */
export type CrudPageOp = 'fetch' | 'create' | 'update' | 'delete' | 'export' | 'import'

export interface UseCrudPageOptions<T, TId = string | number> {
  pageId: string
  columns: ColumnDef[]
  rowKey: (row: T) => TId
  initialPageSize?: number
  fetchData: (query: CrudPageQuery) => Promise<CrudPageResult<T>>
  createData?: (data: Partial<T>) => Promise<T>
  updateData?: (id: TId, data: Partial<T>) => Promise<T>
  deleteData?: (ids: TId[]) => Promise<void>
  exportData?: (query: CrudPageQuery) => Promise<Blob>
  importData?: (file: File) => Promise<void>
  onRefresh?: () => void
  /**
   * Called whenever fetchData / createData / updateData / deleteData /
   * exportData / importData rejects. The signature lets a page tailor
   * messaging per operation. Default behavior - when this is omitted -
   * is to surface the error via `window.$message.error(err.message)`.
   * `TAdminAppRoot` registers that handle automatically (via its internal
   * `TAdminWindowHandles` component inside the provider stack); apps that
   * do NOT mount `TAdminAppRoot` must expose `window.$message` themselves
   * or the default toast silently no-ops.
   *
   * Return `false` to suppress the default toast; any other return value
   * (including `void`) lets the default toast run after your handler.
   *
   * The error is *always* re-thrown after this callback so callers (e.g.
   * a submit button) can still react. To swallow the throw entirely,
   * catch at the call site.
   */
  onError?: (err: Error, op: CrudPageOp) => boolean | void
  /**
   * Retry attempts for `fetchData` on transient failures. Default `3` -
   * three retries with exponential backoff, then surface the error. Set
   * to `0` to disable retries.
   *
   * Only `fetchData` is retried. Write operations (create/update/delete/
   * import) are never retried because duplicate writes are worse than
   * a surfaced error; export is also single-attempt to avoid double
   * billing for expensive report endpoints.
   */
  retryFetch?: number
  /**
   * Base delay (ms) for the exponential retry backoff. Each attempt waits
   * `retryDelayMs * 2 ** attempt` - defaults give 300 → 600 → 1200ms.
   */
  retryDelayMs?: number
  /** Presentation mode for the add/edit/view detail. Default 'modal'. */
  detailMode?: DetailMode
  /**
   * Two-way sync the add/edit/view overlay (which mode + which record) to a
   * URL query key so it is **deep-linkable, refresh-survivable and
   * Back-closeable** - `?detail=view:<id>` / `=edit:<id>` / `=new`. On by
   * default (key `'detail'`); a string renames the key (only to avoid a clash);
   * `false` opts the page out. Coexists with section deep-links and business
   * query params in the same URL (`?detail=view:42&section=overview`). No-ops
   * without a router.
   */
  detailUrl?: boolean | string
  /**
   * Resolve a record by its (string) id so a deep-linked / refreshed
   * `?detail=view:<id>` can hydrate even when the row isn't on the current page.
   * Without it, deep-link restore falls back to the loaded `items` and quietly
   * drops the URL key if the id isn't found once the list has finished loading.
   */
  loadDetailById?: (id: string) => Promise<T | null>
  /**
   * Called whenever the detail opens in **view** mode - both an in-session
   * `openView(row)` AND a deep-link / refresh that restores `?detail=view:<id>`.
   * Use it to lazy-load the record's related data for a read-only `#detail`
   * drawer (e.g. the agents using a persona, a thread's messages) without
   * re-implementing open-state plumbing. Fires once per opened record.
   */
  onView?: (row: T) => void
  /**
   * Load the first page automatically on construction. Default `true` - the
   * hook fires `refresh()` once so pages no longer need the boilerplate
   * `crud.refresh().catch(() => undefined)` line in setup. Set `false` for
   * pages that must configure filters or await a parent resource before the
   * initial fetch, then call `refresh()` themselves.
   */
  autoLoad?: boolean
  /**
   * Operation-level permission gating for the page's write affordances -
   * the UI half of the backend's per-endpoint enforcement. A string is the
   * code prefix and derives the three write codes (`'user'` →
   * `user.create` / `user.update` / `user.delete`); an object names them
   * explicitly (omit an action to leave it ungated).
   *
   * `canCreate` / `canUpdate` / `canDelete` then require BOTH the data
   * callback AND the permission, so the Create button / edit / delete row
   * actions / batch delete disappear for users who hold only the page's
   * `.view` code. Evaluation is reactive against the admin auth store and
   * mirrors the sidebar's fail-open semantics (no store / no user loaded /
   * super admin → allowed); the backend `[ApiAuthorize]` stays the real wall.
   *
   * UPSERT surfaces: when a write callback maps to a backend endpoint whose
   * action code differs from the derived one (e.g. a "create" form that hits
   * an upsert endpoint enforced as `.update`), use the OBJECT form and map
   * that action to the real backend code - a derived code the catalogue
   * never declares is grantable to no one, hiding the button for every
   * non-super user (see Quotas / ExchangeRates / Subscriptions).
   */
  permission?: string | CrudActionPermissions
}

export interface UseCrudPageReturn<T, TId = string | number> {
  query: Ref<CrudPageQuery>
  items: Ref<T[]>
  total: Ref<number>
  loading: Ref<boolean>
  error: Ref<Error | null>
  hasData: ComputedRef<boolean>
  columnSettings: UseColumnSettingsReturn
  batchActions: UseBatchActionsReturn<TId>
  formModal: UseFormModalReturn<T>
  /**
   * The row-key extractor passed to {@link useCrudPage}. Re-exposed so
   * consumer components (notably `TCrudPage`) can default to it instead of
   * forcing callers to pass `rowKey` twice.
   */
  rowKey: (row: T) => TId
  // Reactive getters - `callback supplied && action permission held` (see the
  // `permission` option). Without a `permission` config they degrade to the
  // legacy static "callback supplied" booleans.
  /** Whether the create affordance should show (callback && permission). */
  canCreate: boolean
  /** Whether edit affordances should show (callback && permission). */
  canUpdate: boolean
  /** Whether delete affordances should show (callback && permission). */
  canDelete: boolean
  refresh: () => Promise<void>
  setPage: (pageIndex: number) => void
  setPageSize: (pageSize: number) => void
  setSearch: (text: string) => void
  setSort: (field: string | undefined, order: 'asc' | 'desc' | null) => void
  setFilters: (filters: Record<string, unknown>) => void
  resetQuery: () => void
  /**
   * Open the create form. `seed` pre-fills fields - used when another page
   * hands off with context ("Receive payment" from a customer page already
   * knows the party); without it the operator retypes what the link already
   * said.
   */
  openCreate: (seed?: Partial<T> | MouseEvent) => void
  openEdit: (row: T) => void
  openView: (row: T) => void
  submit: () => Promise<T | null>
  handleDelete: (ids?: TId[]) => Promise<void>
  exportAll: () => Promise<Blob | null>
  importFile: (file: File) => Promise<void>
  /**
   * Clear `error` without re-running `refresh()`. Use from `#error` slot
   * close buttons when the page wants to hide a stale alert without
   * triggering another HTTP call.
   */
  dismissError: () => void
  detailMode: Ref<DetailMode>
}

/**
 * Look up the global `window.$message` handle. `TAdminAppRoot` registers
 * it automatically from inside its provider stack; apps not using
 * `TAdminAppRoot` must register their own. The interface is duck-typed so
 * the helper stays decoupled from naive-ui's type surface and works in
 * test environments where the handle isn't registered.
 */
interface ToastApi {
  error: (content: string) => void
}
function getGlobalMessage(): ToastApi | null {
  if (typeof window === 'undefined') return null
  const m = (window as unknown as { $message?: unknown }).$message
  if (m && typeof (m as { error?: unknown }).error === 'function') {
    return m as ToastApi
  }
  return null
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms)
  })
}

// `normalizeCrudPermission` + `canAction` moved to `./permissionGates` (shared
// with `useChildCollection`); imported at the top of this file.

export function useCrudPage<T, TId = string | number>(
  options: UseCrudPageOptions<T, TId>,
): UseCrudPageReturn<T, TId> {
  const initialPageSize = options.initialPageSize ?? 20
  const retryAttempts = Math.max(0, options.retryFetch ?? 3)
  const retryBaseMs = Math.max(0, options.retryDelayMs ?? 300)

  const makeInitialQuery = (): CrudPageQuery => ({
    pageIndex: 1,
    pageSize: initialPageSize,
    searchText: '',
    sortField: undefined,
    sortOrder: null,
    filters: {},
  })

  const query = ref<CrudPageQuery>(makeInitialQuery()) as Ref<CrudPageQuery>
  const items = ref<T[]>([]) as Ref<T[]>
  const total = ref(0)
  const loading = ref(false)
  const error = ref<Error | null>(null)

  const hasData = computed(() => items.value.length > 0)

  const columnSettings = useColumnSettings({
    pageId: options.pageId,
    columns: options.columns,
  })
  const batchActions = useBatchActions<TId>()

  // The add/edit/view overlay is a `useDetail` instance - the single detail
  // engine. useCrudPage keeps its own create/update/refresh business logic
  // (`submit` below) and exposes `detail.form` AS its public `formModal`, so the
  // 60+ consuming pages' API is unchanged while the overlay open-state ⇄ URL
  // deep-linking lives in ONE place (useDetail). `source` makes deep-link
  // restore resolve from the loaded `items` first, then `loadDetailById`, and
  // an initial `?detail=edit:<id>` waits for the first page of items instead
  // of self-wiping. No-ops without a router (degrades to a plain form modal).
  const detail = useDetail<T>({
    mode: options.detailMode ?? 'modal',
    url: options.detailUrl ?? true,
    getId: (row) => options.rowKey(row) as string | number,
    source: { items, loading, loadById: options.loadDetailById },
  })
  const formModal = detail.form
  const detailMode = detail.mode

  // Fire `onView` whenever the detail opens (or restores via deep-link) in view
  // mode, keyed on the viewed record - so a page can lazy-load related data for
  // its read-only `#detail` drawer without touching open-state plumbing. Keyed
  // on identity so re-opening the SAME record after a close fires again.
  if (options.onView) {
    watch(
      () =>
        formModal.visible.value && formModal.mode.value === 'view'
          ? formModal.formData.value
          : null,
      (row) => {
        if (row) options.onView!(row as T)
      },
    )
  }

  /**
   * Run `op` and route any thrown error through `options.onError` plus
   * the global toast fallback. Always re-throws so the caller still sees
   * the failure - onError is a side channel for messaging, not a swallow.
   */
  async function runWithErrorHandling<R>(op: CrudPageOp, fn: () => Promise<R>): Promise<R> {
    try {
      return await fn()
    } catch (rawErr) {
      const wrapped = rawErr instanceof Error ? rawErr : new Error(String(rawErr))
      const suppressed = options.onError?.(wrapped, op) === false
      if (!suppressed) {
        getGlobalMessage()?.error(wrapped.message)
      }
      throw wrapped
    }
  }

  /**
   * Request sequence token.
   *
   * Type a keyword, hit Search, then immediately page: two loads are in flight.
   * If the first is slower it resolves last and paints page 1's rows while the
   * pager still reads "2". Every entry point here is fire-and-forget
   * (`void refresh()` from setPage / setPageSize / the shell's toolbar), so
   * nothing else serialises them. Only the newest request may write
   * `items` / `total` / `error` / `loading`.
   *
   * The same defect was fixed in `useGlDrilldown` first; this is the base every
   * list page rides on, so it belongs here rather than in one page's hook.
   */
  let seq = 0

  /**
   * Reload the current page.
   *
   * **Never rejects.** Failures land in `error` (and the toast) instead, because
   * every call site is `void refresh()` - a rejected promise there becomes an
   * unhandled rejection, which in Vite dev throws a full-screen overlay over an
   * error the page has already reported properly.
   */
  async function refresh(): Promise<void> {
    const token = ++seq
    loading.value = true
    error.value = null
    let lastErr: Error | null = null

    for (let attempt = 0; attempt <= retryAttempts; attempt++) {
      try {
        const result = await options.fetchData({ ...query.value })
        if (token !== seq) return
        items.value = result.items
        total.value = result.totalCount
        options.onRefresh?.()
        loading.value = false
        return
      } catch (rawErr) {
        if (token !== seq) return
        lastErr = rawErr instanceof Error ? rawErr : new Error(String(rawErr))
        if (attempt < retryAttempts) {
          // Exponential backoff. Skip the wait entirely when base = 0
          // (typical in tests) so unit assertions can race the loop
          // without faking timers.
          if (retryBaseMs > 0) {
            await sleep(retryBaseMs * 2 ** attempt)
          }
          continue
        }
      }
    }

    if (token !== seq) return

    // All attempts exhausted. Record it and tell the user; do not rethrow.
    const finalErr = lastErr ?? new Error('Unknown fetch error')
    error.value = finalErr
    const suppressed = options.onError?.(finalErr, 'fetch') === false
    if (!suppressed) {
      getGlobalMessage()?.error(finalErr.message)
    }
    loading.value = false
  }

  function dismissError(): void {
    error.value = null
  }

  function setPage(pageIndex: number): void {
    query.value = { ...query.value, pageIndex }
    void refresh()
  }

  function setPageSize(pageSize: number): void {
    query.value = { ...query.value, pageSize, pageIndex: 1 }
    void refresh()
  }

  function setSearch(text: string): void {
    query.value = { ...query.value, searchText: text, pageIndex: 1 }
  }

  function setSort(field: string | undefined, order: 'asc' | 'desc' | null): void {
    // Sorting is server-side, so a new sort has to refetch. Without this the
    // function only mutated `query` and nothing ever happened, which is why the
    // whole sort path read as wired but dead.
    // The page index is deliberately kept: "page 3 of the new ordering" is a
    // well-defined place to be, and jumping the reader back to the top on every
    // header click is more disruptive than useful.
    query.value = { ...query.value, sortField: field, sortOrder: order }
    void refresh()
  }

  function setFilters(filters: Record<string, unknown>): void {
    query.value = { ...query.value, filters, pageIndex: 1 }
  }

  function resetQuery(): void {
    query.value = makeInitialQuery()
  }

  function openCreate(seed?: Partial<T> | MouseEvent): void {
    // Open create with a fresh empty object - `useFormModal.open(mode, null)`
    // would set formData to null, which fast-paths submit() into "close
    // without calling createData" (the `!data` guard). Page form templates
    // typically render `:model="(formData ?? {}) as Record<string, unknown>"`
    // which would otherwise hand the user a throwaway local object whose
    // edits never propagate back to formData.
    // `MouseEvent` is in the signature because dozens of pages bind
    // `@click="crud.openCreate"` directly - the browser hands us the event.
    // Spreading it would inject `isTrusted` into formData and ship it to the
    // backend, so events are discarded here rather than at every call site.
    const safe = seed && typeof seed === 'object' && !(typeof Event !== 'undefined' && seed instanceof Event) ? (seed as Partial<T>) : undefined
    formModal.open('create', { ...(safe ?? {}) } as T)
  }

  function openEdit(row: T): void {
    formModal.open('edit', row)
  }

  function openView(row: T): void {
    formModal.open('view', row)
  }

  async function submit(): Promise<T | null> {
    const mode = formModal.mode.value
    const data = formModal.formData.value
    if (mode === 'view' || !mode || !data) {
      formModal.close()
      return null
    }
    if (mode === 'create') {
      if (!options.createData) {
        formModal.close()
        return null
      }
      const created = await runWithErrorHandling('create', () =>
        options.createData!(data as Partial<T>),
      )
      formModal.close()
      await refresh()
      return created
    }
    // edit
    if (!options.updateData) {
      formModal.close()
      return null
    }
    const id = options.rowKey(data as T)
    const updated = await runWithErrorHandling('update', () =>
      options.updateData!(id, data as Partial<T>),
    )
    formModal.close()
    await refresh()
    return updated
  }

  async function handleDelete(ids?: TId[]): Promise<void> {
    if (!options.deleteData) return
    const target = ids ?? batchActions.selectedIds.value
    if (target.length === 0) return
    await runWithErrorHandling('delete', () => options.deleteData!(target))
    batchActions.clear()
    await refresh()
  }

  async function exportAll(): Promise<Blob | null> {
    if (!options.exportData) return null
    return runWithErrorHandling('export', () => options.exportData!({ ...query.value }))
  }

  async function importFile(file: File): Promise<void> {
    if (!options.importData) return
    await runWithErrorHandling('import', () => options.importData!(file))
    await refresh()
  }

  // Auto-load the first page unless the caller opts out. Replaces the per-page
  // `crud.refresh().catch(() => undefined)` line: refresh() already captures
  // its failure into `error` + the global toast, so the swallowed reject here
  // only prevents an unhandled-rejection warning on the initial load.
  if (options.autoLoad !== false) {
    void refresh().catch(() => undefined)
  }

  // Write-affordance visibility = data callback && action permission. Plain
  // getters over computeds so `crud.canCreate` stays a boolean for consumers
  // (templates read it inside render effects → fully reactive) while the
  // check re-evaluates when permissions load or change.
  const actionPerms = normalizeCrudPermission(options.permission)
  const canCreateRef = computed(() => !!options.createData && canAction(actionPerms.create))
  const canUpdateRef = computed(() => !!options.updateData && canAction(actionPerms.update))
  const canDeleteRef = computed(() => !!options.deleteData && canAction(actionPerms.delete))

  return {
    query,
    items,
    total,
    loading,
    error,
    hasData,
    columnSettings,
    batchActions,
    formModal,
    rowKey: options.rowKey,
    get canCreate() {
      return canCreateRef.value
    },
    get canUpdate() {
      return canUpdateRef.value
    },
    get canDelete() {
      return canDeleteRef.value
    },
    refresh,
    setPage,
    setPageSize,
    setSearch,
    setSort,
    setFilters,
    resetQuery,
    openCreate,
    openEdit,
    openView,
    submit,
    handleDelete,
    exportAll,
    importFile,
    dismissError,
    detailMode,
  }
}
