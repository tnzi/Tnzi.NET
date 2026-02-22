/**
 * @tnzi/core/composables/usePagination
 *
 * Composable factory for PaginationController.
 *
 * @example
 * ```ts
 * const pagination = usePagination({ initialPageSize: 20 });
 * pagination.goTo(3);
 * // pagination.totalPages, pagination.hasNextPage, etc. are reactive
 * ```
 */

import { PaginationController } from '../headless/pagination';
import type { PaginationOptions } from '../headless/pagination';

/**
 * Create a reactive pagination controller.
 *
 * All properties (pageIndex, totalPages, hasNextPage, etc.) are reactive
 * and can be used directly in Vue templates.
 */
export function usePagination(options: PaginationOptions = {}): PaginationController {
  return new PaginationController(options);
}

export type { PaginationOptions };
