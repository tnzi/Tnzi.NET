/**
 * @tnzi/ui/stores/factory
 *
 * Store factory for creating stores with dependency injection.
 */

import type { HttpClient } from '@tnzi/core/http/http';
import type {
  StoreRuntime,
  StoreInstance,
  StoreGetters,
  StoreActions,
  StoreDepsInjection,
  PersistOptions,
} from '@tnzi/core/adapters';
import type { StorageAdapter } from '@tnzi/core/adapters';
import {
  createPiniaRuntime,
  type PiniaRuntimeOptions,
} from '../adapters/store/pinia-adapter';
import {
  getActiveStoreRuntime,
  setActiveStoreRuntime,
} from '@tnzi/core/adapters';

// ============================================
// Store Instance Type
// ============================================

/**
 * Store instance with flat property access via Proxy.
 * PiniaStoreWrapper uses a Proxy to expose state, getters, and actions directly.
 * This type extends StoreInstance with Readonly<State> for typed flat access.
 */
type FlatStoreInstance<State> =
  StoreInstance<State, StoreGetters<State>, StoreActions<State>>
  & Readonly<State>
  & Record<string, unknown>;

// ============================================
// Store Runtime Management
// ============================================

let _httpClient: HttpClient | null = null;
let _storageAdapter: StorageAdapter | null = null;

/**
 * Initialize store runtime.
 */
export function initStoreRuntime(
  runtime: StoreRuntime,
  deps?: StoreDepsInjection,
): void {
  setActiveStoreRuntime(runtime);
  _httpClient = (deps?.httpClient as HttpClient) ?? null;

  // Get storage from runtime
  if (runtime.storageAdapter) {
    _storageAdapter = runtime.storageAdapter;
  }
}

/**
 * Get store runtime.
 */
export function getStoreRuntime(): StoreRuntime {
  const runtime = getActiveStoreRuntime();
  if (!runtime) {
    throw new Error('Store runtime not available. Ensure Tnzi UI plugin is installed.');
  }
  return runtime;
}

/**
 * Get HTTP client for stores.
 */
export function getStoreHttpClient(): HttpClient {
  if (!_httpClient) {
    throw new Error('HTTP client not available. Initialize store runtime with httpClient.');
  }
  return _httpClient;
}

/**
 * Get storage adapter for stores.
 */
export function getStoreStorage(): StorageAdapter | null {
  return _storageAdapter;
}

/**
 * Set HTTP client (for dependency injection).
 */
export function setStoreHttpClient(client: HttpClient): void {
  _httpClient = client;
}

/**
 * Set storage adapter (for dependency injection).
 */
export function setStoreStorageAdapter(adapter: StorageAdapter): void {
  _storageAdapter = adapter;
}

/**
 * Reset store runtime module-level singletons.
 * Useful for testing or SSR to avoid shared state between requests.
 */
export function resetStoreRuntime(): void {
  _httpClient = null;
  _storageAdapter = null;
}

// ============================================
// Store Creation Helper
// ============================================

/**
 * Create a store with options.
 * This function provides a convenient way to create stores
 * with automatic dependency injection.
 *
 * Returns a function that creates/retrieves a FlatStoreInstance:
 * the PiniaStoreWrapper Proxy exposes state, getters, and actions
 * directly on the instance alongside the $state/$getters/$actions accessors.
 */
export function createStore<State>(options: {
  id: string;
  state: () => State;
  getters?: Record<string, (state: State) => unknown>;
  actions?: Record<string, (...args: never[]) => unknown>;
  persist?: boolean | PersistOptions;
}): () => FlatStoreInstance<State> {
  // Return a function that creates/retrieves the store
  return () => {
    const runtime = getStoreRuntime();

    // Check if store already exists
    const existingStore = runtime.factory.getStore(options.id);
    if (existingStore) {
      return existingStore as FlatStoreInstance<State>;
    }

    // Prepare deps with HttpClient for store use
    const deps: StoreDepsInjection = {};
    if (_httpClient) {
      deps.httpClient = _httpClient;
    }

    // Call the factory's defineStore
    // Cast getters/actions to adapter types - the underlying Pinia handles actual type safety
    return runtime.factory.defineStore<State, StoreGetters<State>, StoreActions<State>>({
      id: options.id,
      state: options.state,
      getters: options.getters as StoreGetters<State>,
      actions: options.actions as StoreActions<State>,
      persist: options.persist,
    }) as FlatStoreInstance<State>;
  };
}

// ============================================
// Pinia Runtime Setup
// ============================================

/**
 * Setup Pinia runtime with options.
 */
export function setupPiniaRuntime(options?: PiniaRuntimeOptions): StoreRuntime {
  const runtime = createPiniaRuntime(options);
  return runtime as unknown as StoreRuntime;
}
