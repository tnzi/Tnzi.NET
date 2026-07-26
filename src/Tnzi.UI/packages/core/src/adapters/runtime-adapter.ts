import type { RouterAdapter } from './router/index';
import type { StorageAdapter } from './storage';
import { createNoopRouterAdapter } from './router/index';
import { createMemoryStorageAdapter } from './storage';
import { createAdapterSingleton } from './singleton';

/**
 * Composite runtime adapter - aggregates Router + Storage.
 * Store adapter is kept separate (Pinia integration is complex).
 */
export interface IRuntimeAdapter {
  readonly router: RouterAdapter;
  readonly storage: StorageAdapter;
}

function createDefaultRuntimeAdapter(): IRuntimeAdapter {
  return {
    router: createNoopRouterAdapter(),
    storage: createMemoryStorageAdapter(),
  };
}

const _slot = createAdapterSingleton<IRuntimeAdapter>('runtime', createDefaultRuntimeAdapter);

export function setActiveRuntimeAdapter(adapter: IRuntimeAdapter): void {
  _slot.set(adapter);
}

export function useRuntimeAdapter(): IRuntimeAdapter {
  return _slot.use();
}

export function resetRuntimeAdapter(): void {
  _slot.reset();
}
