import { computed, watch, type ComputedRef, type Ref } from 'vue'
import type { PagedList } from '@tnzi/core'
import { DataQueryController } from '@tnzi/core/headless'
import {
  useColumnSettings,
  type ColumnDef,
  type ColumnDefs,
  type UseColumnSettingsReturn,
} from './useColumnSettings'
import { useBatchActions, type UseBatchActionsReturn } from './useBatchActions'
import type { FormFooterSwitch, UseFormModalReturn } from './useFormModal'
import { useDetail, type DetailMode } from './useDetail'
import { getGlobalMessage } from '../utils/global-toast'
import { normalizeCrudPermission, canAction, type CrudActionPermissions } from './permission-gates'
import { translatePageKey } from '../i18n'

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
export type CrudPageOp = 'fetch' | 'load' | 'create' | 'update' | 'delete' | 'export' | 'import'

export interface UseCrudPageOptions<T, TId = string | number> {
  pageId: string
  /** Column definitions. Declare them against the page's row type
   *  (`ColumnDef<FooDto>[]`) to have every `render` body checked against it,
   *  or pass the loose `ColumnDef[]` shape - see {@link ColumnDefs}.
   *  `NoInfer` keeps this a checked position, not an inference site: `T` comes
   *  from the explicit type argument / `fetchData` / `rowKey`, and a column
   *  list typed against a narrower row alias must not redefine it. */
  columns: ColumnDefs<NoInfer<T>>
  rowKey: (row: T) => TId
  initialPageSize?: number
  fetchData: (query: CrudPageQuery) => Promise<CrudPageResult<T>>
  createData?: (data: Partial<T>) => Promise<T>
  updateData?: (id: TId, data: Partial<T>) => Promise<T>
  deleteData?: (ids: TId[]) => Promise<void>
  exportData?: (query: CrudPageQuery) => Promise<Blob>
  importData?: (file: File) => Promise<void>
  /**
   * The file types the import picker offers, as an `<input accept>` value
   * (`'.csv'`). Name what `importData` reads: without it the picker offers
   * `.csv,.xlsx,.json`, and a format the endpoint does not parse is read as
   * text and refused row by row.
   */
  importAccept?: string
  onRefresh?: () => void
  /**
   * Called whenever fetchData / loadEditRecord / createData / updateData /
   * deleteData / exportData / importData rejects. The signature lets a page tailor
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
   * Toast a confirmation after a successful create / update / delete.
   * Default `true`.
   *
   * A list-form save gives no other feedback: the modal closes and the row
   * appears somewhere in a refreshed list, which on page 3 of a sorted list
   * is indistinguishable from "nothing happened". Detail surfaces that toast
   * their own save should pass `false` here rather than let both fire.
   *
   * Per-op form for the mixed case, e.g. `{ delete: false }` when a page
   * already renders its own removal confirmation.
   *
   * Uses the same `window.$message` handle as the error path, so it silently
   * no-ops in apps that do not mount `TAdminAppRoot`.
   */
  successToast?: boolean | Partial<Record<'create' | 'update' | 'delete', boolean>>
  /**
   * Values a new record starts with: whatever the create request would end
   * up with when the field is left out, so the form shows it.
   *
   * A switch the form does not seed renders OFF, but a request that omits the
   * field gets the server's default, and for "enabled" / "active" flags that
   * is ON: the operator saw "disabled" and created a live record. Every way
   * into create gets them (the Create button, a seeded `openCreate`, a restored
   * `?detail=new`); a seed passed to `openCreate` wins over a default.
   */
  createDefaults?: Partial<T>
  /**
   * A record flag such as "Active" shown as a switch at the left of the form modal's footer, opposite
   * Cancel / Confirm, instead of as a form field; see {@link FormFooterSwitch}. Edit only by default.
   */
  formFooterSwitch?: FormFooterSwitch
  /**
   * Retry attempts for `fetchData` on transient failures. Default `3` -
   * three retries with exponential backoff, then surface the error. Set
   * to `0` to disable retries.
   *
   * Transient means no HTTP status (network error, timeout, a thrown non-HTTP
   * error), 408, 429 or 5xx. Any other 4xx (a 403 for a role without the
   * list code, a 404 on a host without the endpoint, a 400 for a bad filter)
   * is final and surfaces after the first request: asking again only repeats
   * the answer.
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
   * Load the record an **edit** form opens on, for a list whose rows are not
   * the whole record (a summary without the body, the tag ids, a nickname).
   *
   * Without it the form opens on the row, so every field the row does not
   * carry shows empty and is saved empty: editing a post clears its body,
   * editing a user clears their nickname. With it, `openEdit` (and so
   * `editAction`) opens the form once this record has arrived, and a restored
   * `?detail=edit:<id>`, which resolves from the loaded rows, swaps it in and
   * holds the save button until it has.
   *
   * Return the shape the form edits; merge the row in when the detail DTO lacks
   * list-only fields. A rejection goes through `onError` with op `'load'`
   * and closes a form that is open on the incomplete row, so it cannot be
   * saved.
   */
  loadEditRecord?: (row: T) => Promise<T>
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
  /** The `formFooterSwitch` option, read by the list shell's form modal. */
  formFooterSwitch?: FormFooterSwitch
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
  /**
   * Save the open form (create or update). While a save is in flight a second
   * call is refused and resolves to `null` without writing; see
   * {@link UseCrudPageReturn.submitting}.
   */
  submit: () => Promise<T | null>
  /**
   * A `submit` is waiting on `createData` / `updateData`. The built-in Confirm
   * buttons show it as their loading state; bind it in a custom form footer.
   * (Same ref as `formModal.submitting`.)
   */
  submitting: Readonly<Ref<boolean>>
  handleDelete: (ids?: TId[]) => Promise<void>
  exportAll: () => Promise<Blob | null>
  importFile: (file: File) => Promise<void>
  /** The `importAccept` option: what the import picker offers. */
  importAccept?: string
  /**
   * Clear `error` without re-running `refresh()`. Use from `#error` slot
   * close buttons when the page wants to hide a stale alert without
   * triggering another HTTP call.
   */
  dismissError: () => void
  detailMode: Ref<DetailMode>
}

/** i18n keys for the write-confirmation toasts, keyed by operation. */
const SUCCESS_TOAST_KEYS = {
  create: 'admin.shared.toasts.created',
  update: 'admin.shared.toasts.updated',
  delete: 'admin.shared.toasts.deleted',
} as const

type WriteOp = keyof typeof SUCCESS_TOAST_KEYS

/**
 * The filter half of the controller's state. Kept as two named fields rather
 * than a flat bag so `buildQuery` can emit the legacy `searchText` / `filters`
 * split that {@link CrudPageQuery} publishes.
 */
interface CrudFilterState extends Record<string, unknown> {
  searchText: string
  filters: Record<string, unknown>
}

/**
 * `@tnzi/core`'s `SortDirection` also admits the long spellings
 * (`'ascending'` / `'descending'`) because some backends emit them.
 * {@link CrudPageQuery} publishes only the short pair, so narrow here rather
 * than widening a contract 60+ pages read.
 */
function toShortDirection(direction: string): 'asc' | 'desc' {
  return direction === 'desc' || direction === 'descending' ? 'desc' : 'asc'
}

// `normalizeCrudPermission` + `canAction` moved to `./permissionGates` (shared
// with `useChildCollection`); imported at the top of this file.

export function useCrudPage<T, TId = string | number>(
  options: UseCrudPageOptions<T, TId>,
): UseCrudPageReturn<T, TId> {
  const initialPageSize = options.initialPageSize ?? 20
  const retryAttempts = Math.max(0, options.retryFetch ?? 3)
  const retryBaseMs = Math.max(0, options.retryDelayMs ?? 300)

  /**
   * The list state lives in `@tnzi/core`'s `DataQueryController`, not in local
   * refs. This hook used to re-implement the same loop (page/sort/filter state,
   * fetch orchestration, staleness token, retry with backoff) alongside it,
   * which is how the two drifted apart.
   *
   * The three behaviours that differ from the controller's defaults are stated
   * as options rather than worked around:
   *  - `resetPageOnSort: false` - a new sort keeps you on the current page.
   *  - `clearSelectionOnPageChange: false` - batch selection survives paging.
   *  - `buildQuery` - emits the legacy {@link CrudPageQuery} field names that
   *    30+ bridges and 60+ pages are written against.
   *
   * `fetchData` returns a bare `PagedList` and signals failure by rejecting;
   * the controller accepts that shape directly.
   */
  const controller = new DataQueryController<T, CrudFilterState>({
    pagination: { initialPageSize },
    defaultFilter: { searchText: '', filters: {} },
    fetchFn: (q) => options.fetchData(q as unknown as CrudPageQuery),
    resetPageOnSort: false,
    clearSelectionOnPageChange: false,
    clampPageToTotal: false,
    retry: { attempts: retryAttempts, delayMs: retryBaseMs },
    buildQuery: ({ pagination, sort, filter }) => ({
      pageIndex: pagination.pageIndex,
      pageSize: pagination.pageSize,
      searchText: filter.searchText,
      sortField: sort.sortBy ?? undefined,
      sortOrder: sort.hasSorting ? toShortDirection(sort.sortDirection) : null,
      filters: filter.filters,
    } satisfies CrudPageQuery),
    onError: (err) => {
      const suppressed = options.onError?.(err, 'fetch') === false
      if (!suppressed) getGlobalMessage()?.error(err.message)
    },
  })

  /**
   * Writable views onto the controller, so the published `Ref<T>` surface is
   * unchanged for the 60+ consuming pages while there is only one copy of the
   * state underneath. (`WritableComputedRef` extends `Ref`, so the exported
   * types below did not have to change.)
   */
  const query = computed<CrudPageQuery>({
    get: (): CrudPageQuery => ({
      pageIndex: controller.pagination.pageIndex,
      pageSize: controller.pagination.pageSize,
      searchText: controller.filter.searchText,
      sortField: controller.sort.sortBy ?? undefined,
      sortOrder: controller.sort.hasSorting ? toShortDirection(controller.sort.sortDirection) : null,
      filters: controller.filter.filters,
    }),
    set: (next: CrudPageQuery) => {
      controller.pagination.pageIndex = next.pageIndex
      controller.pagination.pageSize = next.pageSize
      controller.filter = { searchText: next.searchText, filters: next.filters }
      if (next.sortField && next.sortOrder) {
        controller.sort.setSort(next.sortField, next.sortOrder)
      } else {
        controller.sort.clear()
      }
    },
  })

  const items = computed<T[]>({
    get: () => controller.items,
    set: (next) => {
      controller.items = next
    },
  })
  const total = computed<number>({
    get: () => controller.pagination.totalCount,
    set: (next) => {
      controller.pagination.totalCount = next
    },
  })
  const loading = computed<boolean>({
    get: () => controller.isLoading,
    set: (next) => {
      controller.status = next ? 'loading' : 'idle'
    },
  })
  const error = computed<Error | null>({
    get: () => controller.errorObject,
    set: (next) => {
      controller.errorObject = next
      controller.error = next?.message ?? null
      if (!next && controller.status === 'error') controller.status = 'idle'
    },
  })

  const hasData = computed(() => items.value.length > 0)

  const columnSettings = useColumnSettings({
    pageId: options.pageId,
    // Normalise `ColumnDefs<T>` to the loose shape once, here. The settings
    // engine is pure key/visibility/order bookkeeping - it never touches a row
    // - and the table renderer calls `render` with the actual row object, so
    // this erases nothing at runtime. Row-level type safety lives where it is
    // useful: at the declaration site (`ColumnDef<FooDto>[]` in the config).
    columns: options.columns as ColumnDef[],
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

  // `createDefaults` must reach every way into create: `openCreate` below and
  // the `?detail=new` restore inside useDetail, which opens on an empty record
  // of its own (already, by the time this line runs, on a cold deep link:
  // hence `immediate`). Both go through `formModal.open`, which sets the mode
  // before the record; synchronous, so the form never renders without them.
  const createDefaults = options.createDefaults
  if (createDefaults) {
    watch(
      () => formModal.formData.value,
      (record) => {
        if (!record || formModal.mode.value !== 'create') return
        const target = record as Record<string, unknown>
        for (const [key, value] of Object.entries(createDefaults)) {
          if (target[key] === undefined) target[key] = value
        }
      },
      { flush: 'sync', immediate: true },
    )
  }

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

  /** Whether a write confirmation should fire for `op` (default on). */
  function shouldToast(op: WriteOp): boolean {
    const cfg = options.successToast
    if (cfg === undefined) return true
    if (typeof cfg === 'boolean') return cfg
    return cfg[op] !== false
  }

  function notifyWritten(op: WriteOp): void {
    if (!shouldToast(op)) return
    const toast = getGlobalMessage()
    // `success` is optional on the duck-typed handle - an app may register a
    // minimal one carrying only `error`.
    // Empty namespace: these are absolute `admin.*` keys, which
    // `translatePageKey` resolves at the locale root without prefixing.
    toast?.success?.(translatePageKey('', SUCCESS_TOAST_KEYS[op]))
  }

  /**
   * Reload the current page.
   *
   * **Never rejects.** Failures land in `error` (and the toast) instead, because
   * every call site is `void refresh()` - a rejected promise there becomes an
   * unhandled rejection, which in Vite dev throws a full-screen overlay over an
   * error the page has already reported properly. The controller upholds that
   * contract, along with the retry/backoff and the staleness guard that keeps a
   * slow earlier request from painting over a newer one.
   */
  async function refresh(): Promise<void> {
    // Gate on the controller's "was THIS call applied" answer, not on
    // `isSuccess`: a superseded call also resolves, and by then `isSuccess`
    // may reflect the newer request - announcing a reload this call never did.
    const applied = await controller.fetch()
    if (applied) options.onRefresh?.()
  }

  function dismissError(): void {
    error.value = null
  }

  function setPage(pageIndex: number): void {
    void controller.changePage(pageIndex)
  }

  function setPageSize(pageSize: number): void {
    // `changePageSize` returns to page 1 itself.
    void controller.changePageSize(pageSize)
  }

  function setSearch(text: string): void {
    // Deliberately does NOT refetch - the shell's Search button (or Enter)
    // drives that, so typing does not fire a request per keystroke.
    controller.filter = { ...controller.filter, searchText: text }
    controller.pagination.goTo(1)
  }

  function setSort(field: string | undefined, order: 'asc' | 'desc' | null): void {
    // Sorting is server-side, so a new sort has to refetch. The page index is
    // deliberately kept (`resetPageOnSort: false`): "page 3 of the new
    // ordering" is a well-defined place to be, and jumping the reader back to
    // the top on every header click is more disruptive than useful.
    void controller.setSort(order ? field : null, order ?? 'asc')
  }

  function setFilters(filters: Record<string, unknown>): void {
    // Same no-refetch contract as `setSearch`.
    controller.filter = { ...controller.filter, filters }
    controller.pagination.goTo(1)
  }

  function resetQuery(): void {
    // Deliberately NOT `pagination.reset()` - that also zeroes `totalCount`,
    // and this only resets the QUERY. The rows stay on screen until the next
    // fetch, so a zeroed total would leave the pager claiming "0 items" over a
    // list that is still showing three.
    controller.pagination.pageIndex = 1
    controller.pagination.pageSize = initialPageSize
    controller.sort.clear()
    controller.filter = { searchText: '', filters: {} }
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

  // `loadEditRecord` bookkeeping: the form object the last load opened (so the
  // watcher below does not load it again), and a sequence number so a slower,
  // earlier load cannot replace the form a later one opened.
  let loadedEditRecord: T | null = null
  let editLoadSeq = 0

  async function openEditOnLoadedRecord(row: T): Promise<void> {
    const loader = options.loadEditRecord
    if (!loader) return
    const seq = ++editLoadSeq
    const formBefore = formModal.formData.value
    // Open on the row already (a restored deep link): keep it from being saved
    // with the fields the row lacks while the full record is on its way.
    const holdsOpenForm = formBefore !== null && formModal.mode.value === 'edit'
    if (holdsOpenForm) formModal.submitting.value = true
    try {
      const record = await runWithErrorHandling('load', () => loader(row))
      // A later edit, a close, or another form opened while this was loading.
      if (seq !== editLoadSeq || formModal.formData.value !== formBefore) return
      formModal.open('edit', record)
      loadedEditRecord = formModal.formData.value
    } catch {
      // Already reported through onError / the toast. A form left open on the
      // row would save its empty fields over the real ones.
      if (seq === editLoadSeq && holdsOpenForm && formModal.formData.value === formBefore) formModal.close()
    } finally {
      if (holdsOpenForm) formModal.submitting.value = false
    }
  }

  if (options.loadEditRecord) {
    watch(
      () => (formModal.visible.value && formModal.mode.value === 'edit' ? formModal.formData.value : null),
      (record) => {
        if (!record) {
          // Closed, or another mode: whatever is still loading is for a form that is gone.
          editLoadSeq++
          loadedEditRecord = null
          return
        }
        if (record !== loadedEditRecord) void openEditOnLoadedRecord(record as T)
      },
    )
  }

  function openEdit(row: T): void {
    if (options.loadEditRecord) {
      void openEditOnLoadedRecord(row)
      return
    }
    formModal.open('edit', row)
  }

  function openView(row: T): void {
    formModal.open('view', row)
  }

  /**
   * Run one write with the form's in-flight flag raised. The flag is checked
   * and set synchronously in `submit` (before its first await), so a second
   * click, however fast, finds it raised. It comes down when the request
   * answers either way: a failed save leaves the form open for a retry.
   */
  async function writeOnce<R>(fn: () => Promise<R>): Promise<R> {
    formModal.submitting.value = true
    try {
      return await fn()
    } finally {
      formModal.submitting.value = false
    }
  }

  /**
   * Close the form the save was for. The operator may have closed it while the
   * request was in flight and opened another record; closing whatever is open
   * now would throw away a form they are halfway through.
   */
  function closeIfStillOpenOn(data: T): void {
    if (formModal.formData.value === data) formModal.close()
  }

  async function submit(): Promise<T | null> {
    if (formModal.submitting.value) return null
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
      const created = await writeOnce(() =>
        runWithErrorHandling('create', () => options.createData!(data as Partial<T>)),
      )
      notifyWritten('create')
      closeIfStillOpenOn(data)
      await refresh()
      return created
    }
    // edit
    if (!options.updateData) {
      formModal.close()
      return null
    }
    const id = options.rowKey(data as T)
    const updated = await writeOnce(() =>
      runWithErrorHandling('update', () => options.updateData!(id, data as Partial<T>)),
    )
    notifyWritten('update')
    closeIfStillOpenOn(data)
    await refresh()
    return updated
  }

  async function handleDelete(ids?: TId[]): Promise<void> {
    if (!options.deleteData) return
    const target = ids ?? batchActions.selectedIds.value
    if (target.length === 0) return
    const shownBefore = new Set(controller.items.map(options.rowKey))
    try {
      await runWithErrorHandling('delete', () => options.deleteData!(target))
    } catch (error) {
      // Most bridges delete a batch one id at a time and stop at the first
      // refusal, so the rows before it are already gone. Reload so the list
      // stops showing them, and unselect them: a retry that still names them
      // fails at once on a 404 and never reaches the rest.
      await refresh()
      if (controller.status === 'success') {
        const shownAfter = new Set(controller.items.map(options.rowKey))
        for (const id of target) {
          if (shownBefore.has(id) && !shownAfter.has(id)) batchActions.unselect(id)
        }
      }
      throw error
    }
    notifyWritten('delete')
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
    formFooterSwitch: options.formFooterSwitch,
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
    submitting: formModal.submitting,
    handleDelete,
    exportAll,
    importFile,
    importAccept: options.importAccept,
    dismissError,
    detailMode,
  }
}
