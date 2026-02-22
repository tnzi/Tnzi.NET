/**
 * @tnzi/shadcn/adapters/store
 *
 * Store adapters for Pinia state management.
 */

// Pinia adapter (re-exported)
export { createPiniaRuntime, installPinia, type PiniaRuntimeOptions } from './pinia-adapter';

// Re-export types from core
export type {
  StoreFactory,
  StoreInstance,
  StoreOptions,
  PersistOptions,
  StoreRuntime,
  StoreDepsInjection,
} from '@tnzi/core/adapters';

// Re-export storage types
export type { StorageAdapter, StorageType } from '@tnzi/core/adapters';
