/**
 * @tnzi/mobile/adapters/create-runtime-adapter
 *
 * Composite runtime adapter factory for Vant.
 * Wraps vue-router (optional) + localStorage.
 */

import type { IRuntimeAdapter } from '@tnzi/core/adapters';
import type { RouterAdapter } from '@tnzi/core/adapters';
import { createNoopRouterAdapter, createLocalStorageAdapter } from '@tnzi/core/adapters';

export interface VantRuntimeAdapterOptions {
  /**
   * vue-router instance. If omitted, a no-op router adapter is used.
   */
  router?: { push(to: string): unknown; replace(to: string): unknown; back(): void; currentRoute: { value: { fullPath: string } } };
}

function createVueRouterAdapter(router: NonNullable<VantRuntimeAdapterOptions['router']>): RouterAdapter {
  return {
    push: (path: string) => { router.push(path); },
    replace: (path: string) => { router.replace(path); },
    back: () => router.back(),
    getCurrentPath: () => router.currentRoute.value.fullPath,
  };
}

export function createVantRuntimeAdapter(options: VantRuntimeAdapterOptions = {}): IRuntimeAdapter {
  const { router } = options;

  return {
    router: router ? createVueRouterAdapter(router) : createNoopRouterAdapter(),
    storage: createLocalStorageAdapter(),
  };
}
