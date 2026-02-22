/**
 * Headless data-state utilities for list/table components.
 */

export type HeadlessSortDirection = 'asc' | 'desc';

export interface SortState {
  sortBy?: string;
  sortDirection?: HeadlessSortDirection;
}

export function normalizePageSize(pageSize: number | undefined, fallback = 10): number {
  if (!pageSize || pageSize <= 0) {
    return fallback;
  }
  return Math.floor(pageSize);
}

export function calculateTotalPages(total: number, pageSize: number): number {
  const normalizedSize = normalizePageSize(pageSize);
  return Math.max(1, Math.ceil(Math.max(0, total) / normalizedSize));
}

export function clampPageIndex(
  pageIndex: number,
  total: number,
  pageSize: number
): number {
  const totalPages = calculateTotalPages(total, pageSize);
  return Math.min(totalPages, Math.max(1, Math.floor(pageIndex)));
}

export function updatePageQuery<T extends object>(
  query: T | undefined,
  pageIndex: number,
  pageSize?: number
): T & { pageIndex: number; pageSize?: number } {
  return {
    ...(query ?? ({} as T)),
    pageIndex,
    pageSize,
  };
}

export function toggleSort(
  current: SortState,
  nextSortBy: string,
  initialDirection: HeadlessSortDirection = 'asc'
): Required<SortState> {
  if (current.sortBy === nextSortBy) {
    return {
      sortBy: nextSortBy,
      sortDirection: current.sortDirection === 'asc' ? 'desc' : 'asc',
    };
  }

  return {
    sortBy: nextSortBy,
    sortDirection: initialDirection,
  };
}
