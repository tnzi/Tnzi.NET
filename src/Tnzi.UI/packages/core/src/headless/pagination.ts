/**
 * @tnzi/core/headless/pagination
 *
 * Pagination controller - reactive headless logic.
 */

import { reactive } from 'vue';
import type { PagedQueryDto } from '../types/pagination';
import { createPagedQuery } from '../types/pagination';

// ============================================
// Types
// ============================================

export interface PaginationOptions {
  /** Initial page number (1-based) */
  initialPage?: number;
  /** Initial page size */
  initialPageSize?: number;
  /** Available page size options */
  pageSizes?: number[];
}

// ============================================
// PaginationController
// ============================================

/**
 * Reactive pagination controller.
 *
 * ```ts
 * const pagination = new PaginationController({ initialPageSize: 20 });
 * pagination.goTo(3);
 * pagination.totalCount  // reactive
 * pagination.totalPages  // computed
 * ```
 */
export class PaginationController {
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  readonly pageSizes: readonly number[];

  constructor(options: PaginationOptions = {}) {
    this.pageIndex = options.initialPage ?? 1;
    this.pageSize = options.initialPageSize ?? 20;
    this.totalCount = 0;
    this.pageSizes = Object.freeze(options.pageSizes ?? [10, 20, 50, 100]);
    return reactive(this) as this;
  }

  // Getters
  get totalPages(): number {
    if (this.totalCount === 0) return 0;
    return Math.ceil(this.totalCount / this.pageSize);
  }

  get hasNextPage(): boolean {
    return this.pageIndex < this.totalPages;
  }

  get hasPrevPage(): boolean {
    return this.pageIndex > 1;
  }

  get isFirstPage(): boolean {
    return this.pageIndex === 1;
  }

  get isLastPage(): boolean {
    return this.totalPages === 0 || this.pageIndex >= this.totalPages;
  }

  /** Start index of current page (for displaying "X - Y of Z") */
  get startIndex(): number {
    return (this.pageIndex - 1) * this.pageSize;
  }

  /** End index of current page */
  get endIndex(): number {
    return Math.min(this.startIndex + this.pageSize, this.totalCount);
  }

  // Actions
  goTo(page: number): void {
    if (this.totalPages === 0) {
      this.pageIndex = 1;
      return;
    }
    this.pageIndex = Math.max(1, Math.min(page, this.totalPages));
  }

  next(): void {
    if (this.hasNextPage) this.goTo(this.pageIndex + 1);
  }

  prev(): void {
    if (this.hasPrevPage) this.goTo(this.pageIndex - 1);
  }

  first(): void {
    this.goTo(1);
  }

  last(): void {
    this.goTo(this.totalPages);
  }

  setPageSize(size: number): void {
    this.pageSize = size;
    this.pageIndex = 1; // Reset to first page when changing page size
  }

  /** Update totalCount from API response */
  updateFromResponse(totalCount: number): void {
    this.totalCount = totalCount;
    if (this.totalPages === 0) {
      this.pageIndex = 1;
    } else if (this.pageIndex > this.totalPages) {
      this.pageIndex = this.totalPages;
    }
  }

  /** Generate query parameters for request */
  toQuery(): PagedQueryDto {
    return createPagedQuery(this.pageIndex, this.pageSize);
  }

  /** Reset to initial state */
  reset(options: PaginationOptions = {}): void {
    this.pageIndex = options.initialPage ?? 1;
    this.pageSize = options.initialPageSize ?? this.pageSize;
    this.totalCount = 0;
  }
}

// ============================================
// Pagination Utility Functions
// ============================================

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

export function clampPageIndex(pageIndex: number, total: number, pageSize: number): number {
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

// ============================================
// Pagination State (from types/stores.ts)
// ============================================

/**
 * Pagination state
 */
export interface PaginationState {
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasMore: boolean;
}

/**
 * Create initial pagination state
 */
export function initialPaginationState(): PaginationState {
  return {
    pageIndex: 1,
    pageSize: 10,
    totalCount: 0,
    totalPages: 0,
    hasMore: false,
  };
}
