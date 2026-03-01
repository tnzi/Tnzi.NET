/**
 * @tnzi/shadcn/adapters
 *
 * Web-specific adapter implementations for shadcn-vue and Pinia.
 */

// Message adapter (vue-sonner)
export { createShadcnMessageAdapter } from './message';

// Dialog adapter (reactive singleton + TDialogProvider)
export { createShadcnDialogAdapter } from './dialog';

// Theme adapter (Tailwind CSS dark mode class strategy)
export { createShadcnThemeAdapter } from './theme';

// Store adapter (Pinia)
export { createPiniaRuntime, installPinia } from './store/pinia-adapter';

// Storage adapters (re-exported from core)
export { createLocalStorageAdapter, createSessionStorageAdapter, createMemoryStorageAdapter, createStorageAdapter } from '@tnzi/core/adapters';

// Type exports
export type { PiniaRuntimeOptions, PiniaStoreOptions } from './store/pinia-adapter';

// Re-export types from core
export type {
  StoreFactory,
  StoreInstance,
  StoreOptions,
  PersistOptions,
  StoreRuntime,
  StoreDepsInjection,
} from '@tnzi/core/adapters';

export type { StorageAdapter, StorageType } from '@tnzi/core/adapters';
