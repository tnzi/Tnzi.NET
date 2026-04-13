/**
 * @tnzi/ui/adapters/create-runtime-adapter
 *
 * Factory for the composite runtime adapter (router + storage).
 */

import type { IRuntimeAdapter } from '@tnzi/core/adapters';
import type { Ref } from 'vue';
import { createLocalStorageAdapter } from '@tnzi/core/adapters/storage';

/**
 * Minimal vue-router Router shape — avoids hard dependency on vue-router package.
 */
export interface VueRouterLike {
  push(to: string | Record<string, unknown>): unknown;
  replace(to: string | Record<string, unknown>): unknown;
  back(): void;
  currentRoute: Ref<{ fullPath: string }>;
}

export function createRuntimeAdapter(options?: {
  router?: VueRouterLike;
  storagePrefix?: string;
}): IRuntimeAdapter {
  const router = options?.router;
  return {
    router: router
      ? {
          push: (path: string) => { router.push(path); },
          replace: (path: string) => { router.replace(path); },
          back: () => router.back(),
          getCurrentPath: () => router.currentRoute.value.fullPath,
        }
      : { push() {}, replace() {}, back() {}, getCurrentPath: () => '/' },
    storage: createLocalStorageAdapter(options?.storagePrefix ?? ''),
  };
}
