/**
 * @tnzi/core/composables/useDataQuery
 *
 * Composable factory for DataQueryController.
 * Returns a fully reactive data-query instance ready for Vue templates.
 *
 * @example
 * ```ts
 * const query = useDataQuery<UserDto>({
 *   fetchFn: (params, signal) => api.getUsers(params, signal),
 *   pagination: { initialPageSize: 20 },
 *   sort: { defaultField: 'createdAt', defaultDirection: 'desc' },
 *   immediate: true,
 * });
 *
 * // In template: query.items, query.isLoading, query.pagination, etc.
 * ```
 */

import { DataQueryController } from '../headless/data-query';
import type { DataQueryOptions } from '../headless/data-query';

/**
 * Create a reactive data query controller.
 *
 * Combines pagination, sorting, selection, and filtering into a single
 * reactive instance. All properties (items, status, filter, etc.) are
 * reactive and can be used directly in Vue templates.
 */
export function useDataQuery<
  TItem,
  TFilter extends Record<string, unknown> = Record<string, unknown>,
>(options: DataQueryOptions<TItem, TFilter>): DataQueryController<TItem, TFilter> {
  return new DataQueryController<TItem, TFilter>(options);
}

export type { DataQueryOptions };
