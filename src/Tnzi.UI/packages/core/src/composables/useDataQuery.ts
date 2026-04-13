/**
 * @tnzi/core/composables/useDataQuery
 *
 * Composable factory for DataQueryController with lifecycle management.
 *
 * @example
 * ```ts
 * const query = useDataQuery<UserDto>({
 *   fetchFn: (params, signal) => api.getUsers(params, signal),
 *   pagination: { initialPageSize: 20 },
 *   sort: { defaultField: 'createdAt', defaultDirection: 'desc' },
 *   immediate: true,
 *   autoRefreshInterval: 30000, // refresh every 30s
 * });
 *
 * // In template: query.items, query.isLoading, query.pagination, etc.
 * // Cleanup: query.dispose() in onUnmounted
 * ```
 */

import { DataQueryController } from '../headless/data-query';
import type { DataQueryOptions } from '../headless/data-query';

/**
 * Extended options for useDataQuery composable.
 */
export interface UseDataQueryOptions<
  TItem,
  TFilter extends object = Record<string, unknown>,
> extends DataQueryOptions<TItem, TFilter> {
  /**
   * Auto-refresh interval in milliseconds.
   * Set to 0 or omit to disable auto-refresh.
   */
  autoRefreshInterval?: number;
}

/**
 * Return type of useDataQuery — DataQueryController plus lifecycle methods.
 */
export type UseDataQueryReturn<
  TItem,
  TFilter extends object = Record<string, unknown>,
> = DataQueryController<TItem, TFilter> & {
  /**
   * Dispose resources: stops auto-refresh timer and resets the controller.
   * Call this in Vue's onUnmounted or equivalent lifecycle hook.
   */
  dispose: () => void;
};

/**
 * Create a reactive data query controller with optional auto-refresh.
 *
 * Combines pagination, sorting, selection, and filtering into a single
 * reactive instance. All properties (items, status, filter, etc.) are
 * reactive and can be used directly in Vue templates.
 *
 * Unlike the raw DataQueryController, this composable provides:
 * - `autoRefreshInterval` for periodic data refresh
 * - `dispose()` method for cleanup (timer + abort inflight)
 */
export function useDataQuery<
  TItem,
  TFilter extends object = Record<string, unknown>,
>(options: UseDataQueryOptions<TItem, TFilter>): UseDataQueryReturn<TItem, TFilter> {
  const controller = new DataQueryController<TItem, TFilter>(options);
  let intervalId: ReturnType<typeof setInterval> | null = null;

  if (options.autoRefreshInterval && options.autoRefreshInterval > 0) {
    intervalId = setInterval(() => {
      // Skip if a fetch is already in progress to avoid stacking requests
      if (!controller.isLoading) {
        controller.fetch();
      }
    }, options.autoRefreshInterval);
  }

  const dispose = () => {
    if (intervalId !== null) {
      clearInterval(intervalId);
      intervalId = null;
    }
    controller.reset();
  };

  return Object.assign(controller, { dispose });
}

export type { DataQueryOptions };
