import type { RouterAdapter } from './router/index';
import type { StorageAdapter } from './storage';
import { createNoopRouterAdapter } from './router/index';
import { createMemoryStorageAdapter } from './storage';

/**
 * Composite runtime adapter — aggregates Router + Storage.
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

const _fallback: IRuntimeAdapter = createDefaultRuntimeAdapter();
let _active: IRuntimeAdapter | null = null;

export function setActiveRuntimeAdapter(adapter: IRuntimeAdapter): void {
  _active = adapter;
}

export function useRuntimeAdapter(): IRuntimeAdapter {
  return _active ?? _fallback;
}

export function resetRuntimeAdapter(): void {
  _active = null;
}

/** @deprecated Use `resetRuntimeAdapter` instead */
export const resetRuntimeAdapterRuntime = resetRuntimeAdapter;
