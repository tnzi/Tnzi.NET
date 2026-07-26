/**
 * @tnzi/core/adapters/storage
 *
 * Unified storage adapter for data persistence.
 * Supports both string-based and type-safe operations.
 */

// ============================================
// Type Definitions
// ============================================

/**
 * Storage type for different storage backends.
 */
export type StorageType = 'local' | 'session' | 'memory';

/**
 * Serializer options for complex object storage.
 *
 * Applies to the TYPE-SAFE pair only (`get` / `set`); the string pair
 * (`getItem` / `setItem`) always stores the raw string it was handed. Supplying
 * a serializer swaps out the default `JSON.stringify` / `JSON.parse` in every
 * backend (including memory), so a value round-trips identically no matter
 * which backend the adapter ended up on.
 */
export interface SerializerOptions {
  serialize: (value: unknown) => string;
  deserialize: (value: string) => unknown;
}

/**
 * Unified storage adapter interface.
 * Provides both string-based and type-safe operations.
 */
export interface StorageAdapter {
  // String-based operations (for backward compatibility)
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
  clear(): void;

  // Type-safe operations
  get<T>(key: string): T | null;
  set<T>(key: string, value: T): void;
  remove(key: string): void;
  keys(): string[];
  has(key: string): boolean;
}

/**
 * Storage options for configuration.
 */
export interface StorageOptions {
  /** Storage type */
  type?: StorageType;
  /** Key prefix for namespacing */
  prefix?: string;
  /** Serializer for complex objects */
  serializer?: SerializerOptions;
}

// ============================================
// Internal Helpers
// ============================================

import { useLogger } from './logger';
import { createAdapterSingleton } from './singleton';

const JSON_SERIALIZER: SerializerOptions = {
  serialize: (value) => JSON.stringify(value),
  deserialize: (value) => JSON.parse(value),
};

/**
 * Log a warning when a storage write fails (e.g. quota exceeded).
 */
function warnStorageError(operation: string, key: string, error: unknown): void {
  const isQuotaError =
    error instanceof DOMException &&
    (error.code === 22 || error.code === 1014 || error.name === 'QuotaExceededError');

  if (isQuotaError) {
    useLogger().warn(`[Storage] Quota exceeded on ${operation}("${key}"). Consider clearing unused data.`);
  } else {
    useLogger().warn(`[Storage] ${operation}("${key}") failed:`, error);
  }
}

// ============================================
// Storage Adapter Implementations
// ============================================

/**
 * Adapter over a live Web Storage object (localStorage / sessionStorage).
 */
function createNativeStorageAdapter(
  storage: Storage,
  prefix: string,
  serializer: SerializerOptions
): StorageAdapter {
  return {
    getItem(key: string): string | null {
      return storage.getItem(prefix + key);
    },
    setItem(key: string, value: string): void {
      try { storage.setItem(prefix + key, value); }
      catch (e) { warnStorageError('setItem', key, e); }
    },
    removeItem(key: string): void {
      storage.removeItem(prefix + key);
    },
    clear(): void {
      if (prefix) {
        const keys = Object.keys(storage).filter(k => k.startsWith(prefix));
        keys.forEach(k => storage.removeItem(k));
      } else {
        storage.clear();
      }
    },
    get<T>(key: string): T | null {
      try {
        const item = storage.getItem(prefix + key);
        return item ? (serializer.deserialize(item) as T) : null;
      } catch { return null; }
    },
    set<T>(key: string, value: T): void {
      try { storage.setItem(prefix + key, serializer.serialize(value)); }
      catch (e) { warnStorageError('set', key, e); }
    },
    remove(key: string): void {
      storage.removeItem(prefix + key);
    },
    keys(): string[] {
      const result: string[] = [];
      for (let i = 0; i < storage.length; i++) {
        const key = storage.key(i);
        if (key && (!prefix || key.startsWith(prefix))) {
          result.push(prefix ? key.slice(prefix.length) : key);
        }
      }
      return result;
    },
    has(key: string): boolean {
      return storage.getItem(prefix + key) !== null;
    },
  };
}

/**
 * Inert adapter used when there is no DOM at all (SSR, node unit tests).
 *
 * Deliberately NOT a memory store: a server-side module singleton outlives the
 * request that wrote to it, so one user's tokens would leak into the next
 * request. Writes are dropped, reads report empty.
 */
function createNoopStorageAdapter(): StorageAdapter {
  return {
    getItem: () => null,
    setItem: () => {},
    removeItem: () => {},
    clear: () => {},
    get: () => null,
    set: () => {},
    remove: () => {},
    keys: () => [],
    has: () => false,
  };
}

/**
 * Resolve the backing store for a Web Storage adapter.
 *
 * Reading `window.localStorage` can THROW rather than return null: Safari
 * private mode and sandboxed iframes raise a SecurityError from the property
 * getter itself. That is why the probe is wrapped, and why it runs lazily -
 * doing it at module scope made a single throwing getter break the import of
 * this whole module (and therefore of every entry point that pulls it in).
 */
function resolveWebStorage(
  pick: () => Storage,
  prefix: string,
  serializer: SerializerOptions
): StorageAdapter {
  if (typeof window === 'undefined') {
    return createNoopStorageAdapter();
  }
  try {
    return createNativeStorageAdapter(pick(), prefix, serializer);
  } catch (error) {
    useLogger().warn('[Storage] Web Storage is unavailable, falling back to memory:', error);
    return createMemoryStorageAdapter(prefix, serializer);
  }
}

/**
 * Shared factory for Web Storage API adapters (localStorage/sessionStorage).
 * The backend is resolved on first use, never at import time.
 */
function createWebStorageAdapter(
  pick: () => Storage,
  prefix: string,
  serializer: SerializerOptions
): StorageAdapter {
  let delegate: StorageAdapter | null = null;
  const target = (): StorageAdapter => (delegate ??= resolveWebStorage(pick, prefix, serializer));

  return {
    getItem: (key) => target().getItem(key),
    setItem: (key, value) => target().setItem(key, value),
    removeItem: (key) => target().removeItem(key),
    clear: () => target().clear(),
    get: <T>(key: string) => target().get<T>(key),
    set: <T>(key: string, value: T) => target().set<T>(key, value),
    remove: (key) => target().remove(key),
    keys: () => target().keys(),
    has: (key) => target().has(key),
  };
}

/**
 * Create localStorage adapter with type-safe operations.
 */
export function createLocalStorageAdapter(
  prefix: string = '',
  serializer: SerializerOptions = JSON_SERIALIZER
): StorageAdapter {
  return createWebStorageAdapter(() => window.localStorage, prefix, serializer);
}

/**
 * Create sessionStorage adapter with type-safe operations.
 */
export function createSessionStorageAdapter(
  prefix: string = '',
  serializer: SerializerOptions = JSON_SERIALIZER
): StorageAdapter {
  return createWebStorageAdapter(() => window.sessionStorage, prefix, serializer);
}

/**
 * Create memory storage adapter.
 * Useful for SSR or temporary storage.
 *
 * `prefix` namespaces the keys exactly like the Web Storage adapters do, so an
 * adapter can be swapped between backends without changing what a caller sees.
 * A custom `serializer` forces `get`/`set` to round-trip through it; with the
 * default the value is stored as-is (no serialization cost in memory).
 */
export function createMemoryStorageAdapter(
  prefix: string = '',
  serializer?: SerializerOptions
): StorageAdapter {
  const store = new Map<string, unknown>();
  const k = (key: string) => prefix + key;

  return {
    // String-based operations
    getItem(key: string): string | null {
      const value = store.get(k(key));
      if (value === undefined) return null;
      return typeof value === 'string' ? value : JSON.stringify(value);
    },
    setItem(key: string, value: string): void {
      store.set(k(key), value);
    },
    removeItem(key: string): void {
      store.delete(k(key));
    },
    clear(): void {
      if (!prefix) {
        store.clear();
        return;
      }
      for (const key of Array.from(store.keys())) {
        if (key.startsWith(prefix)) store.delete(key);
      }
    },

    // Type-safe operations
    get<T>(key: string): T | null {
      const value = store.get(k(key));
      if (value === undefined) return null;
      if (!serializer) return value as T;
      try { return serializer.deserialize(value as string) as T; }
      catch { return null; }
    },
    set<T>(key: string, value: T): void {
      store.set(k(key), serializer ? serializer.serialize(value) : value);
    },
    remove(key: string): void {
      store.delete(k(key));
    },
    keys(): string[] {
      const all = Array.from(store.keys());
      if (!prefix) return all;
      return all.filter(key => key.startsWith(prefix)).map(key => key.slice(prefix.length));
    },
    has(key: string): boolean {
      return store.has(k(key));
    },
  };
}

// ============================================
// Singleton
// ============================================

const _slot = createAdapterSingleton<StorageAdapter>('storage', () => createLocalStorageAdapter());

export function setStorageAdapter(adapter: StorageAdapter): void {
  _slot.set(adapter);
}

export function useStorage(): StorageAdapter {
  return _slot.use();
}

export function resetStorageAdapter(): void {
  _slot.reset();
}

// ============================================
// Convenience Factory
// ============================================

/**
 * Create storage adapter based on storage type.
 * `prefix` and `serializer` are honoured by every backend.
 */
export function createStorageAdapter(options: StorageOptions = {}): StorageAdapter {
  const { type = 'local', prefix = '', serializer } = options;

  switch (type) {
    case 'session':
      return createSessionStorageAdapter(prefix, serializer ?? JSON_SERIALIZER);
    case 'memory':
      return createMemoryStorageAdapter(prefix, serializer);
    case 'local':
    default:
      return createLocalStorageAdapter(prefix, serializer ?? JSON_SERIALIZER);
  }
}
