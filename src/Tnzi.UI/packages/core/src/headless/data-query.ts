/**
 * @tnzi/core/headless/data-query
 *
 * Data query orchestrator - combines pagination + sort + selection for complete data query logic.
 */

import { reactive } from '@vue/reactivity';
import { PaginationController } from './pagination';
import { SelectionController } from './selection';
import { SortController } from './sort';
import type { PaginationOptions } from './pagination';
import type { SelectionOptions } from './selection';
import type { SortOptions } from './sort';
import type { ApiResult, PagedList } from '../types/index';

// ============================================
// Types
// ============================================

export type DataQueryStatus = 'idle' | 'loading' | 'success' | 'error';

export interface DataQueryOptions<TItem, TFilter extends Record<string, unknown> = Record<string, unknown>> {
  /** Pagination options */
  pagination?: PaginationOptions;
  /** Sort options */
  sort?: SortOptions;
  /** Selection options */
  selection?: SelectionOptions;
  /** Initial filter values */
  defaultFilter?: TFilter;
  /** Data fetch function (accepts query params and optional AbortSignal) */
  fetchFn: (query: Record<string, unknown>, signal?: AbortSignal) => Promise<ApiResult<PagedList<TItem>>>;
  /** Whether to auto-load on initialization */
  immediate?: boolean;
  /** Extract unique key from item (default: item.id) */
  rowKey?: (item: TItem) => string;
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
export class DataQueryController<TItem, TFilter extends Record<string, unknown> = Record<string, unknown>> {
  /** Current data items */
  items: TItem[];
  /** Loading status */
  status: DataQueryStatus;
  /** Error message */
  error: string | null;
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
  private _abortController: AbortController | null = null;

  constructor(options: DataQueryOptions<TItem, TFilter>) {
    this.items = [];
    this.status = 'idle';
    this.error = null;
    this.filter = (options.defaultFilter ?? {} as TFilter);
    this._fetchFn = options.fetchFn;
    this._rowKey = options.rowKey;

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

  /** Execute data query */
  async fetch(): Promise<void> {
    // Cancel previous in-flight request
    this._abortController?.abort();
    const abortController = new AbortController();
    this._abortController = abortController;

    this.status = 'loading';
    this.error = null;

    try {
      const query = {
        ...this.pagination.toQuery(),
        ...this.sort.toQuery(),
        ...this.filter,
      };

      const result = await this._fetchFn(query, abortController.signal);

      // Check if this request was superseded
      if (abortController.signal.aborted) return;

      if (result.succeeded) {
        this.items = result.data?.items ?? [];
        this.pagination.updateFromResponse(result.data?.totalCount ?? 0);
        const keyExtractor = this._rowKey ?? ((item: TItem) => String((item as Record<string, unknown>)['id'] ?? ''));
        this.selection.setAllKeys(this.items.map(keyExtractor));
        this.status = 'success';
      } else {
        this.error = result.message ?? 'Fetch failed';
        this.status = 'error';
      }
    } catch (e) {
      // Silently ignore aborted requests
      if (abortController.signal.aborted) return;
      this.error = e instanceof Error ? e.message : 'Fetch failed';
      this.status = 'error';
    }
  }

  /** Change page and re-fetch */
  async changePage(page: number): Promise<void> {
    this.pagination.goTo(page);
    this.selection.clear();
    await this.fetch();
  }

  /** Change page size and re-fetch */
  async changePageSize(size: number): Promise<void> {
    this.pagination.setPageSize(size);
    this.selection.clear();
    await this.fetch();
  }

  /** Change sort and re-fetch */
  async changeSort(field: string): Promise<void> {
    this.sort.toggle(field);
    this.pagination.goTo(1);
    this.selection.clear();
    await this.fetch();
  }

  /** Apply filter and re-fetch */
  async applyFilter(filter: Partial<TFilter>): Promise<void> {
    this.filter = { ...this.filter, ...filter };
    this.pagination.goTo(1);
    this.selection.clear();
    await this.fetch();
  }

  /** Reset filter and re-fetch */
  async resetFilter(defaultFilter?: TFilter): Promise<void> {
    this.filter = defaultFilter ?? {} as TFilter;
    this.pagination.goTo(1);
    this.selection.clear();
    await this.fetch();
  }

  /** Refresh current page data */
  async refresh(): Promise<void> {
    this.selection.clear();
    await this.fetch();
  }

  /** Full reset (pagination, sort, filter, selection) */
  async reset(defaultFilter?: TFilter): Promise<void> {
    // Cancel any in-flight request
    this._abortController?.abort();
    this._abortController = null;

    this.pagination.reset();
    this.sort.clear();
    this.filter = defaultFilter ?? {} as TFilter;
    this.selection.clear();
    this.items = [];
    this.status = 'idle';
    this.error = null;
  }
}
