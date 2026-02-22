/**
 * Pagination Types - Aligned with Tnzi.NET backend PagedList<T> and PagedQuery
 */

/**
 * Paged list response from backend
 */
export interface PagedList<T> {
  /** Current page index (1-based) */
  pageIndex: number;
  /** Number of items per page */
  pageSize: number;
  /** Total number of items */
  totalCount: number;
  /** Total number of pages */
  totalPages: number;
  /** Whether there is a previous page */
  hasPreviousPage: boolean;
  /** Whether there is a next page */
  hasNextPage: boolean;
  /** Items on current page */
  items: T[];
}

/**
 * Base query parameters for pagination
 */
export interface PagedQuery {
  /** Page index (1-based, default: 1) */
  pageIndex: number;
  /** Page size (default: 10, max: usually 100) */
  pageSize: number;
}

/**
 * Extended paged query with computed skip/take
 */
export interface PagedQueryDto extends PagedQuery {
  /** Number of items to skip (readonly: (pageIndex - 1) * pageSize) */
  skip: number;
  /** Number of items to take (readonly: pageSize) */
  take: number;
}

/**
 * Create a PagedQueryDto from page index and size
 */
export function createPagedQuery(pageIndex = 1, pageSize = 10): PagedQueryDto {
  return {
    pageIndex,
    pageSize,
    skip: (pageIndex - 1) * pageSize,
    take: pageSize,
  };
}

/**
 * Update skip/take based on pageIndex and pageSize
 */
export function updatePagedQuery(query: Partial<PagedQueryDto>): PagedQueryDto {
  const pageIndex = query.pageIndex ?? 1;
  const pageSize = query.pageSize ?? 10;
  return {
    pageIndex,
    pageSize,
    skip: (pageIndex - 1) * pageSize,
    take: pageSize,
  };
}

/**
 * Sort direction
 */
export type SortDirection = 'asc' | 'desc' | 'ascending' | 'descending';

/**
 * Paged query with sorting
 */
export interface SortedPagedQueryDto extends PagedQueryDto {
  /** Field to sort by */
  sortBy?: string;
  /** Whether to sort descending (default: true) */
  sortDescending?: boolean;
}

/**
 * Paged query with keyword search
 */
export interface SearchPagedQueryDto extends PagedQueryDto {
  /** Search keyword */
  keyword?: string;
}

/**
 * Paged query with date range filter
 */
export interface DateRangePagedQueryDto extends PagedQueryDto {
  /** Start date filter */
  startDate?: Date | string;
  /** End date filter */
  endDate?: Date | string;
}

/**
 * Full-featured paged query with common filters
 */
export interface FullPagedQueryDto extends SortedPagedQueryDto, SearchPagedQueryDto, DateRangePagedQueryDto {
}

/**
 * Empty paged list result
 */
export function emptyPagedList<T>(): PagedList<T> {
  return {
    pageIndex: 1,
    pageSize: 10,
    totalCount: 0,
    totalPages: 0,
    hasPreviousPage: false,
    hasNextPage: false,
    items: [],
  };
}

/**
 * Create a paged list from items and total count
 */
export function createPagedList<T>(
  items: T[],
  totalCount: number,
  pageIndex: number,
  pageSize: number
): PagedList<T> {
  const totalPages = Math.ceil(totalCount / pageSize);
  return {
    pageIndex,
    pageSize,
    totalCount,
    totalPages,
    hasPreviousPage: pageIndex > 1,
    hasNextPage: pageIndex < totalPages,
    items,
  };
}
