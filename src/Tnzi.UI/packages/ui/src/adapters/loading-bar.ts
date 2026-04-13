/**
 * @tnzi/ui/adapters/loading-bar
 *
 * Loading bar adapter using Naive UI's loading bar API.
 *
 * Supports two modes:
 * 1. Explicit API injection: pass the useLoadingBar() result directly
 * 2. Global handle: uses window.$loadingBar (set by a setup component under NLoadingBarProvider)
 */

import type { LoadingBarAdapter } from '@tnzi/core/adapters';

interface NaiveLoadingBarApi {
  start: () => void;
  finish: () => void;
  error: () => void;
}

/**
 * Resolve the loading bar API: use explicit API if provided, otherwise fallback to window.$loadingBar.
 */
function resolveApi(loadingBarApi?: NaiveLoadingBarApi): NaiveLoadingBarApi | undefined {
  if (loadingBarApi) return loadingBarApi;
  return (window as unknown as Record<string, unknown>).$loadingBar as NaiveLoadingBarApi | undefined;
}

/**
 * Create a Naive UI loading bar adapter.
 *
 * When called without arguments, the adapter uses window.$loadingBar as the global handle.
 * The application must wrap the root with NLoadingBarProvider and expose the API:
 * ```ts
 * // In a setup component under NLoadingBarProvider
 * window.$loadingBar = useLoadingBar();
 * ```
 */
export function createLoadingBarAdapter(loadingBarApi?: NaiveLoadingBarApi): LoadingBarAdapter {
  return {
    start() {
      resolveApi(loadingBarApi)?.start();
    },
    finish() {
      resolveApi(loadingBarApi)?.finish();
    },
    error() {
      resolveApi(loadingBarApi)?.error();
    },
  };
}
