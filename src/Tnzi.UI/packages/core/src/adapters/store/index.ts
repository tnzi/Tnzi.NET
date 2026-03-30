/**
 * @tnzi/core/adapters/store
 *
 * Store adapter interfaces for state management abstraction.
 * Provides framework-agnostic state management interface definitions.
 */

import type { StorageAdapter } from '../storage';

// ============================================
// Type Definitions
// ============================================

/**
 * Unsubscribe function type for store subscriptions.
 */
export type Unsubscribe = () => void;

/**
 * Generic computed/ref type for getters.
 */
export interface ComputedRef<T> {
  readonly value: T;
}

/**
 * Store state definition function.
 */
export type StoreState<T> = () => T;

/**
 * Store getters definition.
 */
export interface StoreGetters<_State> {
  [key: string]: unknown;
}

/**
 * Store actions definition.
 */
export interface StoreActions<_State> {
  [key: string]: (...args: unknown[]) => unknown;
}

/**
 * Persistence options for store state.
 */
export interface PersistOptions {
  /** Storage key (defaults to store id) */
  key?: string;
  /** Storage type */
  storage?: 'local' | 'session';
  /** Paths to persist (supports dot notation for nested) */
  paths?: string[];
  /** Paths to exclude from persistence */
  excludePaths?: string[];
}

/**
 * Store options for defining a new store.
 */
export interface StoreOptions<
  State,
  Getters extends StoreGetters<State>,
  Actions extends StoreActions<State>
> {
  /** Store identifier (must be unique) */
  id: string;
  /** State factory function */
  state: StoreState<State>;
  /** Computed/getters definitions */
  getters?: Getters;
  /** Action methods */
  actions?: Actions;
  /** Enable persistence for this store */
  persist?: boolean | PersistOptions;
}

/**
 * Store instance interface providing access to state, getters, and actions.
 */
export interface StoreInstance<
  State,
  Getters extends StoreGetters<State>,
  Actions extends StoreActions<State>
> {
  /** Store identifier */
  readonly $id: string;
  /** Access to state */
  readonly $state: State;
  /** Access to getters */
  readonly $getters: Readonly<Getters>;
  /** Access to actions */
  readonly $actions: Actions;
  /** Reset state to initial value */
  $reset(): void;
  /** Patch state with partial values */
  $patch(partial: Partial<State>): void;
  /** Dispose store and cleanup */
  $dispose(): void;
}

// ============================================
// Store Factory Interface
// ============================================

/**
 * Store factory for creating and managing stores.
 */
export interface StoreFactory {
  /**
   * Define and create a new store.
   */
  defineStore<
    State,
    Getters extends StoreGetters<State>,
    Actions extends StoreActions<State>
  >(
    options: StoreOptions<State, Getters, Actions>
  ): StoreInstance<State, Getters, Actions>;

  /**
   * Get existing store by id.
   */
  getStore(id: string): StoreInstance<unknown, StoreGetters<unknown>, StoreActions<unknown>> | undefined;

  /**
   * Remove/dispose a store.
   */
  removeStore(id: string): void;

  /**
   * Get all store IDs.
   */
  getStoreIds(): string[];
}

// ============================================
// Store Runtime Interface
// ============================================

/**
 * Store runtime for managing store factory and persistence.
 */
export interface StoreRuntime {
  /** Store factory */
  factory: StoreFactory;
  /** Storage adapter for persistence */
  storageAdapter?: StorageAdapter;
  /** Install to framework (e.g., Vue app) */
  install(app: unknown): void;
  /**
   * Get store by id.
   */
  useStore(id: string): StoreInstance<unknown, StoreGetters<unknown>, StoreActions<unknown>> | undefined;
}

/**
 * Store runtime options.
 */
export interface StoreRuntimeOptions {
  /** Storage adapter for persistence */
  storageAdapter?: StorageAdapter;
}

// ============================================
// Dependency Injection
// ============================================

/**
 * Store dependency injection.
 * Allows injecting HTTP client and other dependencies into stores.
 */
export interface StoreDepsInjection {
  /** HTTP client for API calls */
  httpClient?: unknown;
  /** Additional dependencies */
  [key: string]: unknown;
}

/**
 * Type for stores with dependencies.
 */
export type WithDeps<T> = T & {
  $deps?: StoreDepsInjection;
};

// ============================================
// Default Implementations
// ============================================

/**
 * Create a store adapter instance.
 * Note: This is a base implementation - concrete implementations
 * are provided by consuming packages (e.g., Pinia in @tnzi/shadcn).
 */
export function createStoreAdapter(): StoreFactory {
  // Base implementation - concrete implementation provided by consuming packages.
  // defineStore throws until a real implementation (e.g. Pinia) is installed.
  return {
    defineStore() {
      throw new Error(
        'StoreFactory.defineStore() is not implemented. ' +
        'Install a store runtime (e.g. createPiniaRuntime from @tnzi/shadcn) before defining stores.'
      );
    },
    getStore: () => undefined,
    removeStore: () => {},
    getStoreIds: () => [],
  };
}

/**
 * Create store runtime.
 */
export function createStoreRuntime(options: StoreRuntimeOptions = {}): StoreRuntime {
  const { storageAdapter } = options;
  const factory = createStoreAdapter();

  return {
    factory,
    storageAdapter,
    install(_app: unknown): void {
      // Base implementation - concrete implementation provided by consuming packages
    },
    useStore(id: string) {
      return factory.getStore(id);
    },
  };
}

// ============================================
// Default Runtime Instance
// ============================================

const defaultStoreRuntime = createStoreRuntime();
let activeStoreRuntime: StoreRuntime = defaultStoreRuntime;

/**
 * Set active store runtime.
 */
export function setActiveStoreRuntime(runtime: StoreRuntime): void {
  activeStoreRuntime = runtime;
}

/**
 * Get active store runtime.
 */
export function getActiveStoreRuntime(): StoreRuntime {
  return activeStoreRuntime;
}

/**
 * Get store factory (convenience function).
 */
export function getStoreFactory(): StoreFactory {
  return activeStoreRuntime.factory;
}

/**
 * Get store by id (convenience function).
 */
export function useStore(id: string): StoreInstance<unknown, StoreGetters<unknown>, StoreActions<unknown>> | undefined {
  return activeStoreRuntime.useStore(id);
}

/**
 * Get all store IDs (convenience function).
 */
export function getStoreIds(): string[] {
  return activeStoreRuntime.factory.getStoreIds();
}

/**
 * Define a store (convenience function).
 */
export function defineStore<
  State,
  Getters extends StoreGetters<State>,
  Actions extends StoreActions<State>
>(
  options: StoreOptions<State, Getters, Actions>
): StoreInstance<State, Getters, Actions> {
  return activeStoreRuntime.factory.defineStore<State, Getters, Actions>(options);
}

/**
 * Remove a store (convenience function).
 */
export function removeStore(id: string): void {
  activeStoreRuntime.factory.removeStore(id);
}

/**
 * Reset store runtime to default. For tests and SSR isolation.
 */
export function resetStoreRuntime(): void {
  activeStoreRuntime = defaultStoreRuntime;
}
