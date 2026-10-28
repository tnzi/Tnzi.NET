/**
 * @tnzi/core/headless/data-query
 *
 * Data query orchestrator - combines pagination + sort + selection for complete data query logic.
 */

import { reactive, toRaw } from 'vue';
import { PaginationController } from './pagination';
import { SelectionController } from './selection';
import { SortController } from './sort';
import { HttpError } from '../errors/api-error';
import type { PaginationOptions } from './pagination';
import type { SelectionOptions } from './selection';
import type { SortOptions, SortDirection } from './sort';
import type { ApiResult, PagedList } from '../types/index';

/**
 * 4xx answers that are worth asking again: the server timed the request out
 * (408) or asked the caller to slow down (429). Every other 4xx is the server's
 * final word on this request (no permission, not found, invalid query), and
 * repeating it only delays the error the operator needs to see.
 */
const RETRYABLE_CLIENT_STATUSES: ReadonlySet<number> = new Set([408, 429]);

/**
 * Whether a fetch failure is the server's final answer rather than a transient
 * fault. Read structurally (`statusCode`, else a numeric `code`) instead of
 * with `instanceof`: every `@tnzi/core` entry inlines its own copy of the error
 * classes, so the class a bridge threw need not be the one imported here.
 * A failure without a status (network error, timeout) stays retryable.
 */
function isPermanentFailure(error: unknown): boolean {
  if (typeof error !== 'object' || error === null) return false;
  const { statusCode, code } = error as { statusCode?: unknown; code?: unknown };
  const status = typeof statusCode === 'number' ? statusCode : typeof code === 'number' ? code : undefined;
  return status !== undefined && status >= 400 && status < 500 && !RETRYABLE_CLIENT_STATUSES.has(status);
}

/**
 * Copy plain objects and arrays so the stored default filter and the live
 * filter never share a nested object. Other values (Date, class instances) are
 * kept as they are: a filter holds data, and those are not mutated in place.
 */
function copyPlain<T>(value: T): T {
  const raw = toRaw(value);
  if (Array.isArray(raw)) return raw.map((item) => copyPlain(item)) as T;
  if (raw !== null && typeof raw === 'object' && Object.getPrototypeOf(raw) === Object.prototype) {
    return Object.fromEntries(Object.entries(raw).map(([key, item]) => [key, copyPlain(item)])) as T;
  }
  return raw;
}

// ============================================
// Types
// ============================================

export type DataQueryStatus = 'idle' | 'loading' | 'success' | 'error';

/**
 * What `fetchFn` may resolve to.
 *
 * `ApiResult<PagedList>` is the framework envelope. A bare `PagedList` is also
 * accepted for callers whose data layer already unwrapped it and signals
 * failure by rejecting (the shape `@tnzi/ui-admin` bridges return).
 */
export type DataQueryFetchResult<TItem> = ApiResult<PagedList<TItem>> | PagedList<TItem>;

/**
 * Retry policy for {@link DataQueryController.fetch}.
 *
 * Only transient failures are retried: a rejection or failed envelope without
 * an HTTP status (network error, timeout, a thrown non-HTTP error), 408, 429
 * and 5xx. Any other 4xx (400 / 401 / 403 / 404 / 409 / 422 ...) is the
 * server's answer, and asking again only repeats it seconds later, so it
 * surfaces after the first attempt.
 */
export interface DataQueryRetryOptions {
  /** Number of RETRIES after the first attempt. Default 0 (no retry). */
  attempts?: number;
  /** Base delay in ms; attempt N waits `delayMs * 2 ** N`. Default 300. */
  delayMs?: number;
}

/** Inputs handed to a custom {@link DataQueryOptions.buildQuery}. */
export interface DataQueryBuildContext<TFilter extends object> {
  pagination: PaginationController;
  sort: SortController;
  filter: TFilter;
}

export interface DataQueryOptions<TItem, TFilter extends object = Record<string, unknown>> {
  /** Pagination options */
  pagination?: PaginationOptions;
  /** Sort options */
  sort?: SortOptions;
  /** Selection options */
  selection?: SelectionOptions;
  /**
   * Initial filter values. Also what {@link DataQueryController.resetFilter}
   * and {@link DataQueryController.reset} return to when called without one.
   */
  defaultFilter?: TFilter;
  /**
   * Data fetch function (accepts query params and optional AbortSignal).
   *
   * May resolve either the framework `ApiResult` envelope or a bare
   * `PagedList` - see {@link DataQueryFetchResult}. A rejection is treated as
   * a failed fetch and, when {@link DataQueryOptions.retry} is set, retried.
   */
  fetchFn: (query: Record<string, unknown>, signal?: AbortSignal) => Promise<DataQueryFetchResult<TItem>>;
  /** Whether to auto-load on initialization */
  immediate?: boolean;
  /** Extract unique key from item (default: item.id) */
  rowKey?: (item: TItem) => string;

  /**
   * Whether changing the sort returns to page 1. Default `true`.
   *
   * Set `false` to stay put: "page 3 of the new ordering" is a well-defined
   * place to be, and yanking the reader back to the top on every header click
   * is more disruptive than useful for a long list.
   */
  resetPageOnSort?: boolean;

  /**
   * Whether paging (or changing page size) clears the selection. Default `true`.
   *
   * Set `false` for cross-page batch selection, where the operator ticks rows
   * on page 1, pages forward, and expects the earlier ticks to survive.
   */
  clearSelectionOnPageChange?: boolean;

  /**
   * Whether {@link DataQueryController.changePage} clamps the target page to
   * the known page count. Default `true`.
   *
   * Set `false` when pages may be set before the total is known - a restored
   * bookmark or deep link asking for page 3 would otherwise be silently
   * rewritten to page 1, because `totalPages` is 0 until the first response
   * lands and "unknown" is indistinguishable from "only one page".
   */
  clampPageToTotal?: boolean;

  /**
   * Retry policy for transient fetch failures. Default: no retry.
   *
   * Only transient failures are retried: a rejection or failed envelope with
   * no HTTP status (network error, timeout), a 5xx, a 408 or a 429. Any other
   * 4xx (403 without the module's view permission, 404, a rejected query) is
   * the server's final answer and is surfaced at once.
   *
   * Only fetches are retried. There is deliberately no write-side equivalent:
   * a duplicated write is worse than a surfaced error.
   */
  retry?: DataQueryRetryOptions;

  /**
   * Override how the request payload is built from the current state.
   *
   * Default is `{ ...pagination.toQuery(), ...sort.toQuery(), ...filter }`,
   * which sends the sort as `sortBy` + `sortDescending`. Supply this when the
   * backend contract names these fields differently. An endpoint that binds
   * the generic `PagedQuery` (as `CrudAdminControllerBase` does) reads only
   * `orderBy` ("Name DESC") and ignores the default sort keys without an
   * error, so it needs a `buildQuery` that emits `orderBy`.
   */
  buildQuery?: (context: DataQueryBuildContext<TFilter>) => Record<string, unknown>;

  /**
   * Called when a fetch ultimately fails (after any retries).
   *
   * `fetch()` never rejects, so this is the hook for surfacing the failure.
   */
  onError?: (error: Error) => void;
}

// ============================================
// DataQueryController
// ============================================

/**
 * Data query orchestrator that combines pagination, sorting, selection and filtering.
 *
 * ```ts
 * const query = new DataQueryController<UserDto>({
 *   fetchFn: (params, signal) => userApi.getList(params, signal),
 *   pagination: { initialPageSize: 20 },
 *   sort: { defaultField: 'createdAt', defaultDirection: 'desc' },
 *   immediate: true,
 * });
 *
 * // Reactive properties for Vue templates
 * query.items       // reactive data list
 * query.isLoading   // loading state
 * query.pagination  // pagination controller
 *
 * // Actions
 * await query.changePage(2);
 * await query.changeSort('name');
 * await query.applyFilter({ keyword: 'test' });
 * ```
 */
export class DataQueryController<TItem, TFilter extends object = Record<string, unknown>> {
  /** Current data items */
  items: TItem[];
  /** Loading status */
  status: DataQueryStatus;
  /** Error message */
  error: string | null;
  /**
   * The failure itself, when the last fetch failed.
   *
   * `error` keeps only the message; consumers that need the cause (to branch
   * on an HTTP status, or re-surface the original) read this instead.
   */
  errorObject: Error | null;
  /** Current filter values */
  filter: TFilter;

  /** Pagination controller */
  readonly pagination: PaginationController;
  /** Sort controller */
  readonly sort: SortController;
  /** Selection controller */
  readonly selection: SelectionController;

  private readonly _fetchFn: DataQueryOptions<TItem, TFilter>['fetchFn'];
  private readonly _rowKey: ((item: TItem) => string) | undefined;
  private readonly _resetPageOnSort: boolean;
  private readonly _clearSelectionOnPageChange: boolean;
  private readonly _clampPageToTotal: boolean;
  private readonly _retryAttempts: number;
  private readonly _retryDelayMs: number;
  private readonly _buildQuery: DataQueryOptions<TItem, TFilter>['buildQuery'];
  private readonly _onError: DataQueryOptions<TItem, TFilter>['onError'];
  private readonly _defaultFilter: TFilter;
  private _abortController: AbortController | null = null;

  constructor(options: DataQueryOptions<TItem, TFilter>) {
    this.items = [];
    this.status = 'idle';
    this.error = null;
    this.errorObject = null;
    this._defaultFilter = copyPlain(options.defaultFilter ?? {} as TFilter);
    this.filter = copyPlain(this._defaultFilter);
    this._fetchFn = options.fetchFn;
    this._rowKey = options.rowKey;
    this._resetPageOnSort = options.resetPageOnSort ?? true;
    this._clearSelectionOnPageChange = options.clearSelectionOnPageChange ?? true;
    this._clampPageToTotal = options.clampPageToTotal ?? true;
    this._retryAttempts = Math.max(0, options.retry?.attempts ?? 0);
    this._retryDelayMs = Math.max(0, options.retry?.delayMs ?? 300);
    this._buildQuery = options.buildQuery;
    this._onError = options.onError;

    this.pagination = new PaginationController(options.pagination);
    this.sort = new SortController(options.sort);
    this.selection = new SelectionController(options.selection);

    const instance = reactive(this) as this;

    if (options.immediate) {
      // Auto-load in next microtask
      Promise.resolve().then(() => instance.fetch());
    }

    return instance;
  }

  // Getters
  get isLoading(): boolean {
    return this.status === 'loading';
  }

  get isIdle(): boolean {
    return this.status === 'idle';
  }

  get isSuccess(): boolean {
    return this.status === 'success';
  }

  get isError(): boolean {
    return this.status === 'error';
  }

  get isEmpty(): boolean {
    return this.status === 'success' && this.items.length === 0;
  }

  get hasData(): boolean {
    return this.items.length > 0;
  }

  // Actions

  /** Build the request payload from the current pagination / sort / filter state. */
  private _toQuery(): Record<string, unknown> {
    if (this._buildQuery) {
      return this._buildQuery({ pagination: this.pagination, sort: this.sort, filter: this.filter });
    }
    return {
      ...this.pagination.toQuery(),
      ...this.sort.toQuery(),
      ...this.filter,
    };
  }

  /** Commit a successful page of data. */
  private _applyPage(page: PagedList<TItem> | undefined): void {
    this.items = page?.items ?? [];
    this.pagination.updateFromResponse(page?.totalCount ?? 0);
    const keyExtractor = this._rowKey ?? ((item: TItem) => String((item as Record<string, unknown>)['id'] ?? ''));
    this.selection.setAllKeys(this.items.map(keyExtractor));
    this.status = 'success';
    this.error = null;
    this.errorObject = null;
  }

  /** Record a terminal failure and notify. Never throws. */
  private _applyError(error: Error): void {
    this.error = error.message || 'Fetch failed';
    this.errorObject = error;
    this.status = 'error';
    this._onError?.(error);
  }

  /**
   * Execute the data query.
   *
   * **Never rejects.** Failures land in `status`/`error`/`errorObject` and the
   * `onError` callback, because call sites are typically fire-and-forget
   * (`void query.fetch()`) where a rejection would surface as an unhandled
   * promise rejection rather than as the error the UI already reported.
   *
   * @returns `true` when THIS call's result was applied to the state.
   *   `false` when it was superseded by a newer fetch (its result was
   *   discarded) or it ultimately failed. Callers that announce "the list
   *   reloaded" must gate on this - a superseded call resolves too, and
   *   reading `isSuccess` afterwards would observe the *newer* request's
   *   success and announce a reload that this call never performed.
   */
  async fetch(): Promise<boolean> {
    // Cancel previous in-flight request. Also acts as the staleness token:
    // a superseded attempt sees its own signal aborted and writes nothing.
    this._abortController?.abort();
    const abortController = new AbortController();
    this._abortController = abortController;

    this.status = 'loading';
    this.error = null;
    this.errorObject = null;

    const signal = abortController.signal;
    const requestedPage = this.pagination.pageIndex;
    let corrected = false;
    let lastError: Error | null = null;

    for (let attempt = 0; attempt <= this._retryAttempts; attempt++) {
      try {
        const result = await this._fetchFn(this._toQuery(), signal);

        if (signal.aborted) return false;

        // Envelope or bare page: only the envelope carries `succeeded`.
        let page: PagedList<TItem> | undefined;
        if (result && typeof result === 'object' && 'succeeded' in result) {
          const envelope = result as ApiResult<PagedList<TItem>>;
          // An HttpError, like the one a bridge throws for the same answer,
          // so the status survives for the retry decision and for `errorObject`.
          if (!envelope.succeeded) throw new HttpError({ ...envelope, message: envelope.message ?? 'Fetch failed' });
          page = envelope.data;
        } else {
          page = result as PagedList<TItem>;
        }

        // The page asked for is past the end: the list shrank under it (the
        // last row of the last page was deleted, an edit moved rows out of the
        // filter) or a restored page number was never valid. The server does
        // not clamp, so the answer is an empty page with the real total. Pull
        // the index back inside the real page count and ask once more, so the
        // rows shown match the page the pager shows; applying the empty answer
        // would leave an empty table under a pager whose current page cannot
        // be clicked to recover. At most once: a server whose totals keep
        // disagreeing with its pages must not turn this into a loop. The
        // corrective request gets the full retry budget of its own.
        if (!corrected && this._isPastEnd(page, requestedPage)) {
          corrected = true;
          this.pagination.updateFromResponse(page?.totalCount ?? 0);
          attempt = -1;
          continue;
        }

        this._applyPage(page);
        return true;
      } catch (e) {
        if (signal.aborted) return false;
        lastError = e instanceof Error ? e : new Error(String(e));
        if (isPermanentFailure(e)) break;
      }

      if (attempt < this._retryAttempts) {
        if (this._retryDelayMs > 0) {
          await new Promise<void>((resolve) => setTimeout(resolve, this._retryDelayMs * 2 ** attempt));
        }
        // A newer fetch may have started while we waited out the backoff.
        // Checked even with a zero delay: the retry loop yields on every
        // `await` above, so a supersede can land between attempts.
        if (signal.aborted) return false;
      }
    }

    if (signal.aborted) return false;
    this._applyError(lastError ?? new Error('Fetch failed'));
    return false;
  }

  /** Whether `page` answered a request for a page beyond the last one that has rows. */
  private _isPastEnd(page: PagedList<TItem> | undefined, requestedPage: number): boolean {
    const totalCount = page?.totalCount ?? 0;
    if ((page?.items?.length ?? 0) > 0 || totalCount <= 0 || this.pagination.pageSize <= 0) return false;
    return requestedPage > Math.ceil(totalCount / this.pagination.pageSize);
  }

  /** Change page and re-fetch */
  async changePage(page: number): Promise<void> {
    if (this._clampPageToTotal) {
      this.pagination.goTo(page);
    } else {
      this.pagination.pageIndex = Math.max(1, page);
    }
    if (this._clearSelectionOnPageChange) this.selection.clear();
    await this.fetch();
  }

  /** Change page size and re-fetch */
  async changePageSize(size: number): Promise<void> {
    this.pagination.setPageSize(size);
    if (this._clearSelectionOnPageChange) this.selection.clear();
    await this.fetch();
  }

  /** Change sort and re-fetch */
  async changeSort(field: string): Promise<void> {
    this.sort.toggle(field);
    if (this._resetPageOnSort) this.pagination.goTo(1);
    if (this._clearSelectionOnPageChange) this.selection.clear();
    await this.fetch();
  }

  /**
   * Set an explicit sort (field + direction) and re-fetch.
   *
   * Unlike {@link changeSort}, which toggles between asc and desc on repeat
   * calls and never clears, this states the target outright - the shape a
   * table header emits. Passing `null`/`undefined` for `field` clears the sort.
   */
  async setSort(field: string | null | undefined, direction: SortDirection = 'asc'): Promise<void> {
    if (field) {
      this.sort.setSort(field, direction);
    } else {
      this.sort.clear();
    }
    if (this._resetPageOnSort) this.pagination.goTo(1);
    if (this._clearSelectionOnPageChange) this.selection.clear();
    await this.fetch();
  }

  /** Apply filter and re-fetch */
  async applyFilter(filter: Partial<TFilter>): Promise<void> {
    this.filter = { ...this.filter, ...filter };
    this.pagination.goTo(1);
    this.selection.clear();
    await this.fetch();
  }

  /**
   * Reset the filter and re-fetch. Without an argument the filter returns to
   * the configured `defaultFilter` (a fresh copy), not to an empty object.
   */
  async resetFilter(defaultFilter?: TFilter): Promise<void> {
    this.filter = defaultFilter ?? copyPlain(this._defaultFilter);
    this.pagination.goTo(1);
    this.selection.clear();
    await this.fetch();
  }

  /** Refresh current page data */
  async refresh(): Promise<void> {
    this.selection.clear();
    await this.fetch();
  }

  /**
   * Full reset (pagination, sort, filter, selection). Without an argument the
   * filter returns to the configured `defaultFilter` (a fresh copy).
   */
  async reset(defaultFilter?: TFilter): Promise<void> {
    // Cancel any in-flight request
    this._abortController?.abort();
    this._abortController = null;

    this.pagination.reset();
    this.sort.clear();
    this.filter = defaultFilter ?? copyPlain(this._defaultFilter);
    this.selection.clear();
    this.items = [];
    this.status = 'idle';
    this.error = null;
    this.errorObject = null;
  }
}
