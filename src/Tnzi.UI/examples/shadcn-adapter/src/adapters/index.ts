/**
 * @tnzi/ui/adapters
 *
 * Web-specific adapter implementations for Tnzi UI and Pinia.
 */

// Message adapter
export { createMessageAdapter } from './message';

// Dialog adapter (reactive list + DialogProvider)
export { createDialogAdapter } from './dialog';

// Notification adapter
export { createNotificationAdapter } from './notification';

// Loading bar adapter
export { createLoadingBarAdapter } from './loading-bar';

// Theme adapter (Tailwind CSS dark mode class strategy)
export { createThemeAdapter } from './theme';

// Composite adapters
export { createUiAdapter } from './create-ui-adapter';
export { createRuntimeAdapter } from './create-runtime-adapter';

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
