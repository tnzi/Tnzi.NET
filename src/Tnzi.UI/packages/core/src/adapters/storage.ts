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
 * Shared factory for Web Storage API adapters (localStorage/sessionStorage).
 */
function createWebStorageAdapter(
  getStorage: () => Storage | null,
  prefix: string
): StorageAdapter {
  const storage = typeof window !== 'undefined' ? getStorage() : null;

  return {
    getItem(key: string): string | null {
      if (!storage) return null;
      return storage.getItem(prefix + key);
    },
    setItem(key: string, value: string): void {
      if (!storage) return;
      try { storage.setItem(prefix + key, value); }
      catch (e) { warnStorageError('setItem', key, e); }
    },
    removeItem(key: string): void {
      if (!storage) return;
      storage.removeItem(prefix + key);
    },
    clear(): void {
      if (!storage) return;
      if (prefix) {
        const keys = Object.keys(storage).filter(k => k.startsWith(prefix));
        keys.forEach(k => storage.removeItem(k));
      } else {
        storage.clear();
      }
    },
    get<T>(key: string): T | null {
      if (!storage) return null;
      try {
        const item = storage.getItem(prefix + key);
        return item ? JSON.parse(item) : null;
      } catch { return null; }
    },
    set<T>(key: string, value: T): void {
      if (!storage) return;
      try { storage.setItem(prefix + key, JSON.stringify(value)); }
      catch (e) { warnStorageError('set', key, e); }
    },
    remove(key: string): void {
      if (!storage) return;
      storage.removeItem(prefix + key);
    },
    keys(): string[] {
      if (!storage) return [];
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
      if (!storage) return false;
      return storage.getItem(prefix + key) !== null;
    },
  };
}

/**
 * Create localStorage adapter with type-safe operations.
 */
export function createLocalStorageAdapter(prefix: string = ''): StorageAdapter {
  return createWebStorageAdapter(() => window.localStorage, prefix);
}

/**
 * Create sessionStorage adapter with type-safe operations.
 */
export function createSessionStorageAdapter(prefix: string = ''): StorageAdapter {
  return createWebStorageAdapter(() => window.sessionStorage, prefix);
}

/**
 * Create memory storage adapter.
 * Useful for SSR or temporary storage.
 */
export function createMemoryStorageAdapter(): StorageAdapter {
  const store = new Map<string, unknown>();

  return {
    // String-based operations
    getItem(key: string): string | null {
      const value = store.get(key);
      if (value === undefined) return null;
      return typeof value === 'string' ? value : JSON.stringify(value);
    },
    setItem(key: string, value: string): void {
      store.set(key, value);
    },
    removeItem(key: string): void {
      store.delete(key);
    },
    clear(): void {
      store.clear();
    },

    // Type-safe operations
    get<T>(key: string): T | null {
      return (store.get(key) ?? null) as T | null;
    },
    set<T>(key: string, value: T): void {
      store.set(key, value);
    },
    remove(key: string): void {
      store.delete(key);
    },
    keys(): string[] {
      return Array.from(store.keys());
    },
    has(key: string): boolean {
      return store.has(key);
    },
  };
}

// ============================================
// Singleton
// ============================================

const _fallback: StorageAdapter = createLocalStorageAdapter();
let _active: StorageAdapter | null = null;

export function setStorageAdapter(adapter: StorageAdapter): void {
  _active = adapter;
}

export function useStorage(): StorageAdapter {
  return _active ?? _fallback;
}

export function resetStorageAdapter(): void {
  _active = null;
}

// ============================================
// Convenience Factory
// ============================================

/**
 * Create storage adapter based on storage type.
 */
export function createStorageAdapter(options: StorageOptions = {}): StorageAdapter {
  const { type = 'local', prefix = '' } = options;

  switch (type) {
    case 'session':
      return createSessionStorageAdapter(prefix);
    case 'memory':
      return createMemoryStorageAdapter();
    case 'local':
    default:
      return createLocalStorageAdapter(prefix);
  }
}
