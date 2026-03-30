/**
 * @tnzi/shadcn/adapters/store/pinia
 *
 * Pinia store adapter implementation for web applications.
 * Provides state management using Pinia with persistence support.
 */

import { createPinia, defineStore as piniaDefineStore, type Pinia, type PiniaPluginContext, type StateTree, type StoreGeneric } from 'pinia';
import type {
  StoreFactory,
  StoreInstance,
  StoreOptions,
  PersistOptions,
  StoreDepsInjection,
  StoreGetters,
  StoreActions,
  WithDeps,
} from '@tnzi/core/adapters';
import type {
  StorageAdapter,
  StorageType,
} from '@tnzi/core/adapters';
import { createLocalStorageAdapter, createSessionStorageAdapter, createMemoryStorageAdapter } from '@tnzi/core/adapters';

/**
 * Pinia internal interface exposing the store map.
 * Pinia's `_s` is a private `Map<string, StoreGeneric>` but not part of the public API.
 */
interface PiniaInternals extends Pinia {
  _s: Map<string, StoreGeneric>;
}

/**
 * Pinia DefineStoreOptions with optional persist field for plugin context.
 */
interface PiniaDefineStoreOptionsWithPersist {
  persist?: boolean | PersistOptions;
}

// ============================================
// Store Instance Wrapper
// ============================================

/**
 * Wrapper for Pinia store instances implementing StoreInstance interface.
 *
 * This wrapper uses a Proxy to merge getters and actions directly onto the instance,
 * matching Pinia's native behavior while implementing the StoreInstance interface.
 */
class PiniaStoreWrapper<
  State,
  Getters extends StoreGetters<State>,
  Actions extends StoreActions<State>,
> implements StoreInstance<State, Getters, Actions>
{
  private readonly store: StoreGeneric;

  constructor(
    store: StoreGeneric,
  ) {
    this.store = store;

    // Merge getters and actions onto this instance via Proxy
    return new Proxy(this, {
      get(target, prop, receiver) {
        // First check if property exists on target (our wrapper methods)
        if (prop in target) {
          return Reflect.get(target, prop, receiver);
        }
        // Then check the underlying store (getters and actions)
        if (prop in store) {
          const value = store[prop as keyof typeof store];
          // Bind functions to the store to preserve 'this' context
          if (typeof value === 'function') {
            return (value as Function).bind(store);
          }
          return value;
        }
        return undefined;
      },
    }) as PiniaStoreWrapper<State, Getters, Actions>;
  }

  get $id(): string {
    return this.store.$id;
  }

  get $state(): State {
    return this.store.$state as State;
  }

  get $getters(): Readonly<Getters> {
    // Pinia merges getters into the store instance
    return this.store as unknown as Readonly<Getters>;
  }

  get $actions(): Actions {
    // Pinia merges actions into the store instance
    return this.store as unknown as Actions;
  }

  $reset(): void {
    this.store.$reset();
  }

  $patch(partial: Partial<State>): void {
    this.store.$patch(partial as StateTree);
  }

  $dispose(): void {
    this.store.$dispose();
  }
}

// ============================================
// Pinia Store Factory
// ============================================

/**
 * Pinia store factory implementing StoreFactory interface.
 */
class PiniaStoreFactory implements StoreFactory {
  private readonly pinia: Pinia;
  private readonly deps: StoreDepsInjection;

  constructor(pinia: Pinia, deps: StoreDepsInjection = {}) {
    this.pinia = pinia;
    this.deps = deps;
  }

  defineStore<
    State,
    Getters extends StoreGetters<State>,
    Actions extends StoreActions<State>,
  >(
    options: StoreOptions<State, Getters, Actions>,
  ): StoreInstance<State, Getters, Actions> {
    const useStore = piniaDefineStore(options.id, {
      state: options.state as () => StateTree,
      getters: options.getters as Record<string, (state: StateTree) => unknown>,
      actions: options.actions as Record<string, (...args: unknown[]) => unknown>,
    });
    const store = useStore(this.pinia as Pinia);

    // Inject deps if configured
    if (this.deps && (store as WithDeps<StoreGeneric>).$deps === undefined) {
      (store as WithDeps<StoreGeneric>).$deps = this.deps;
    }

    return new PiniaStoreWrapper<State, Getters, Actions>(store);
  }

  getStore(id: string): StoreInstance<unknown, StoreGetters<unknown>, StoreActions<unknown>> | undefined {
    const storeMap = (this.pinia as PiniaInternals)._s;
    if (!storeMap) return undefined;
    const store = storeMap.get(id);
    return store ? new PiniaStoreWrapper(store) : undefined;
  }

  removeStore(id: string): void {
    const store = this.getStore(id);
    if (store) {
      store.$dispose();
      (this.pinia as PiniaInternals)._s.delete(id);
    }
  }

  getStoreIds(): string[] {
    const storeMap = (this.pinia as PiniaInternals)._s;
    return storeMap ? Array.from(storeMap.keys()) : [];
  }
}

// ============================================
// Pinia Runtime
// ============================================

/**
 * Pinia runtime implementing StoreRuntime interface.
 */
class PiniaRuntimeImplementation {
  private _pinia: Pinia | null = null;
  private _factory: PiniaStoreFactory | null = null;
  private readonly storageAdapter: StorageAdapter | null = null;
  private readonly deps: StoreDepsInjection;

  constructor(
    pinia: Pinia | null = null,
    storageAdapter: StorageAdapter | null = null,
    deps: StoreDepsInjection = {},
  ) {
    this._pinia = pinia;
    this.storageAdapter = storageAdapter;
    this.deps = deps;

    if (pinia) {
      this._factory = new PiniaStoreFactory(pinia, deps);
    }
  }

  install(app: { use: (plugin: unknown) => void }): void {
    if (this._pinia) {
      return; // Already installed
    }

    // Create Pinia instance with persistence plugin
    const pinia = createPinia();
    pinia.use(this.createPersistPlugin());
    app.use(pinia);

    this._pinia = pinia;
    this._factory = new PiniaStoreFactory(pinia, this.deps);
  }

  get pinia(): Pinia | null {
    return this._pinia;
  }

  get factory(): PiniaStoreFactory {
    if (!this._factory) {
      throw new Error('Pinia runtime not initialized. Call install() first.');
    }
    return this._factory;
  }

  storage(): StorageAdapter | null {
    return this.storageAdapter;
  }

  useStore(id: string): StoreInstance<unknown, StoreGetters<unknown>, StoreActions<unknown>> | undefined {
    return this.factory.getStore(id);
  }

  private createPersistPlugin() {
    const storageAdapter = this.storageAdapter;
    const self = this;

    return (context: PiniaPluginContext) => {
      const { store, options } = context;
      const persistOpt = (options as PiniaDefineStoreOptionsWithPersist).persist;

      if (persistOpt === false || !persistOpt) {
        return;
      }

      const key = typeof persistOpt === 'object' && persistOpt.key
        ? persistOpt.key
        : `pinia-${store.$id}`;

      const paths = typeof persistOpt === 'object' ? persistOpt.paths : undefined;
      const excludePaths = typeof persistOpt === 'object' ? persistOpt.excludePaths : undefined;

      // Hydrate store from storage
      try {
        const persisted = storageAdapter?.get(key);
        if (persisted) {
          store.$patch(persisted);
        }
      } catch (error) {
        console.warn(`[PersistPlugin] Failed to hydrate store ${store.$id}:`, error);
      }

      // Subscribe to store changes for persistence
      store.$subscribe(
        () => {
          try {
            const state = store.$state as Record<string, unknown>;

            // Apply path filtering if specified
            let dataToPersist: Record<string, unknown> = state;

            if (paths && paths.length > 0) {
              dataToPersist = self.pickByPaths(state, paths);
            } else if (excludePaths && excludePaths.length > 0) {
              dataToPersist = self.omitByPaths(state, excludePaths);
            }

            storageAdapter?.set(key, dataToPersist);
          } catch (error) {
            console.warn(`[PersistPlugin] Failed to persist store ${store.$id}:`, error);
          }
        },
        { deep: true },
      );
    };
  }

  private pickByPaths(obj: Record<string, unknown>, paths: string[]): Record<string, unknown> {
    const result: Record<string, unknown> = {};
    for (const path of paths) {
      const value = this.getNestedValue(obj, path);
      if (value !== undefined) {
        this.setNestedValue(result, path, value);
      }
    }
    return result;
  }

  private omitByPaths(obj: Record<string, unknown>, paths: string[]): Record<string, unknown> {
    const result = { ...obj };
    for (const path of paths) {
      this.unsetNestedValue(result, path);
    }
    return result;
  }

  private getNestedValue(obj: Record<string, unknown>, path: string): unknown {
    return path.split('.').reduce<unknown>((o, key) => {
      return (o != null && typeof o === 'object') ? (o as Record<string, unknown>)[key] : undefined;
    }, obj);
  }

  private setNestedValue(obj: Record<string, unknown>, path: string, value: unknown): void {
    const keys = path.split('.');
    const lastKey = keys.pop()!;
    const target = keys.reduce<Record<string, unknown>>((o, key) => {
      if (!(key in o) || typeof o[key] !== 'object' || o[key] == null) {
        o[key] = {};
      }
      return o[key] as Record<string, unknown>;
    }, obj);
    target[lastKey] = value;
  }

  private unsetNestedValue(obj: Record<string, unknown>, path: string): void {
    const keys = path.split('.');
    const lastKey = keys.pop()!;
    const target = keys.reduce<Record<string, unknown> | undefined>((o, key) => {
      return (o != null && typeof o === 'object') ? (o as Record<string, unknown>)[key] as Record<string, unknown> | undefined : undefined;
    }, obj);
    if (target != null && typeof target === 'object') {
      delete target[lastKey];
    }
  }
}

// ============================================
// Options Types
// ============================================

/**
 * Pinia store options extension.
 */
export interface PiniaStoreOptions extends Omit<StoreOptions<unknown, StoreGetters<unknown>, StoreActions<unknown>>, 'persist'> {
  /** Dependencies to inject into store */
  deps?: StoreDepsInjection;
}

/**
 * Pinia runtime options.
 */
export interface PiniaRuntimeOptions {
  /** Storage type for persistence */
  storageType?: StorageType;
  /** Storage prefix */
  storagePrefix?: string;
  /** Dependencies to inject globally */
  deps?: StoreDepsInjection;
}

// ============================================
// Runtime Factory
// ============================================

/**
 * Create Pinia runtime with storage and deps.
 */
export function createPiniaRuntime(options: PiniaRuntimeOptions = {}): PiniaRuntimeImplementation {
  const { storageType = 'local', storagePrefix = '', deps } = options;

  // Create storage adapter
  let storageAdapter: StorageAdapter;
  switch (storageType) {
    case 'session':
      storageAdapter = createSessionStorageAdapter(storagePrefix);
      break;
    case 'memory':
      storageAdapter = createMemoryStorageAdapter();
      break;
    case 'local':
    default:
      storageAdapter = createLocalStorageAdapter(storagePrefix);
  }

  return new PiniaRuntimeImplementation(null, storageAdapter, deps);
}

// ============================================
// Convenience Functions
// ============================================

/**
 * Install Pinia with default configuration.
 */
export function installPinia(app: { use: (plugin: unknown) => void }, options?: PiniaRuntimeOptions): Pinia {
  const runtime = createPiniaRuntime(options);
  runtime.install(app);
  if (!runtime.pinia) {
    throw new Error('Failed to create Pinia instance');
  }
  return runtime.pinia;
}

// ============================================
// Storage Adapter Factory
// ============================================

/**
 * Create storage adapter based on storage type.
 */
export function createWebPersistenceAdapter(
  storage: StorageType = 'local',
  prefix: string = '',
): StorageAdapter {
  switch (storage) {
    case 'session':
      return createSessionStorageAdapter(prefix);
    case 'memory':
      return createMemoryStorageAdapter();
    case 'local':
    default:
      return createLocalStorageAdapter(prefix);
  }
}

// Re-export storage adapter types
export type { StorageType } from '@tnzi/core/adapters';
export type { StorageAdapter } from '@tnzi/core/adapters';
export type { StoreDepsInjection } from '@tnzi/core/adapters';
