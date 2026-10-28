/**
 * @tnzi/core/constants/pagination
 *
 * Pagination-related constants.
 */

/** First page (pages are 1-based). */
export const DEFAULT_PAGE = 1;

/**
 * Page size the paged-query helpers fall back to (`createPagedQuery`,
 * `updatePagedQuery`, `normalizePageSize`). A `PaginationController` opens a
 * list at 20 rows unless `initialPageSize` says otherwise.
 */
export const DEFAULT_PAGE_SIZE = 10;

/** Page sizes a `PaginationController` offers when no `pageSizes` are given. */
export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100];
