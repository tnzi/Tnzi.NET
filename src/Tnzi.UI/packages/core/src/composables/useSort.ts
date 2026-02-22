/**
 * @tnzi/core/composables/useSort
 *
 * Composable factory for SortController.
 *
 * @example
 * ```ts
 * const sort = useSort({ defaultField: 'name', defaultDirection: 'asc' });
 * sort.toggle('name');     // switches to desc
 * sort.toggle('createdAt'); // switches to createdAt asc
 * // sort.sortBy, sort.sortDirection, etc. are reactive
 * ```
 */

import { SortController } from '../headless/sort';
import type { SortDirection, SortField, SortOptions } from '../headless/sort';

/**
 * Create a reactive sort controller.
 *
 * Supports single and multi-field sorting. All properties are reactive
 * and can be used directly in Vue templates.
 */
export function useSort(options: SortOptions = {}): SortController {
  return new SortController(options);
}

export type { SortDirection, SortField, SortOptions };
