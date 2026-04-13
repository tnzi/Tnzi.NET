/**
 * @tnzi/core/adapters/store
 *
 * Store adapter interfaces for state management abstraction.
 * Provides framework-agnostic state management interface definitions.
 */

// ============================================
// Type Definitions
// ============================================

// Unsubscribe type re-exported from event-bus (single canonical definition)
export type { Unsubscribe } from '../event-bus';

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
 * are provided by consuming packages (e.g., Pinia in @tnzi/ui).
 */
export function createStoreAdapter(): StoreFactory {
  // Base implementation - concrete implementation provided by consuming packages.
  // defineStore throws until a real implementation (e.g. Pinia) is installed.
  return {
    defineStore() {
      throw new Error(
        'StoreFactory.defineStore() is not implemented. ' +
        'Install a store runtime (e.g. createPiniaRuntime from @tnzi/ui) before defining stores.'
      );
    },
    getStore: () => undefined,
    removeStore: () => {},
    getStoreIds: () => [],
  };
}

// ============================================
// Singleton
// ============================================

const _fallback: StoreFactory = createStoreAdapter();
let _active: StoreFactory | null = null;

export function setStoreAdapter(factory: StoreFactory): void {
  _active = factory;
}

export function useStore(): StoreFactory {
  return _active ?? _fallback;
}

export function resetStoreAdapter(): void {
  _active = null;
}

/** @deprecated Use `setStoreAdapter` instead */
export const setStoreFactory = setStoreAdapter;
/** @deprecated Use `useStore` instead */
export const useStoreFactory = useStore;
/** @deprecated Use `resetStoreAdapter` instead */
export const resetStoreRuntime = resetStoreAdapter;
