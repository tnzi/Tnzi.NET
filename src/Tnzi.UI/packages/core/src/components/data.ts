/**
 * @tnzi/core/components/data
 *
 * Cross-platform data list contracts (Web & Mobile).
 */

// ============================================
// Common Data Contracts
// ============================================

/**
 * Generic list query model.
 */
export interface IDataQuery {
  /** Page index (starts from 1) */
  pageIndex?: number;
  /** Page size */
  pageSize?: number;
  /** Cursor for cursor-based pagination */
  cursor?: string;
  /** Keyword search */
  keyword?: string;
  /** Filter bag */
  filters?: Record<string, unknown>;
  /** Sort field */
  sortBy?: string;
  /** Sort direction */
  sortDirection?: 'asc' | 'desc';
}

/**
 * Generic load state model.
 */
export interface IDataLoadState {
  /** Loading list data */
  loading?: boolean;
  /** Refreshing list data */
  refreshing?: boolean;
  /** No more data */
  noMore?: boolean;
  /** Empty result */
  empty?: boolean;
  /** Optional error message */
  error?: string;
}

/**
 * Platform-agnostic data list props.
 */
export interface IDataListProps<T = unknown> {
  /** List items */
  items: T[];
  /** Current query */
  query?: IDataQuery;
  /** Current loading state */
  loadState?: IDataLoadState;
  /** Item key field */
  itemKey?: string | ((item: T, index: number) => string);
  /** Empty text */
  emptyText?: string;
  /** Custom style class */
  class?: string | string[];
  /** Custom inline style */
  style?: string | Record<string, string | number>;
}

/**
 * Platform-agnostic data list emits.
 */
export interface IDataListEmits<T = unknown> {
  /** Query change */
  'update:query': [query: IDataQuery];
  /** Refresh data */
  refresh: [];
  /** Load more data */
  loadMore: [];
  /** Item click */
  itemClick: [item: T, index: number];
}

// ============================================
// Web Specific Contracts
// ============================================

/**
 * Web pager configuration.
 */
export interface IWebPagerConfig {
  /** Current page (starts from 1) */
  pageIndex: number;
  /** Page size */
  pageSize: number;
  /** Total records */
  total: number;
  /** Page size options */
  pageSizes?: number[];
  /** Show quick jumper */
  showQuickJumper?: boolean;
  /** Show total */
  showTotal?: boolean;
}

/**
 * Web data list props.
 */
export interface IWebDataListProps<T = unknown> extends IDataListProps<T> {
  /** Render mode on desktop */
  mode?: 'table' | 'list';
  /** Pager config, false means no pager */
  pager?: IWebPagerConfig | false;
}

/**
 * Web data list emits.
 */
export interface IWebDataListEmits<T = unknown> extends IDataListEmits<T> {
  /** Pager change */
  pageChange: [pageIndex: number, pageSize: number];
}

// ============================================
// Mobile Specific Contracts
// ============================================

/**
 * Mobile list load trigger mode.
 */
export type MobileLoadTrigger = 'scroll' | 'pull' | 'manual' | 'hybrid';

/**
 * Mobile data list props.
 */
export interface IMobileDataListProps<T = unknown> extends IDataListProps<T> {
  /** Load trigger mode */
  trigger?: MobileLoadTrigger;
  /** Enable pull-to-refresh */
  pullToRefresh?: boolean;
}

/**
 * Mobile data list emits.
 */
export interface IMobileDataListEmits<T = unknown> extends IDataListEmits<T> {
  /** Pull refresh event */
  pullRefresh: [];
}
