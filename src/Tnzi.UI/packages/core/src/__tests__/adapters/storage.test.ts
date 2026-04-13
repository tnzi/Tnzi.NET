import { describe, it, expect, beforeEach } from 'vitest';
import {
  createMemoryStorageAdapter,
  createStorageAdapter,
  useStorage,
  setStorageAdapter,
  resetStorageAdapter,
  type StorageAdapter,
} from '../../adapters/storage';

describe('createMemoryStorageAdapter', () => {
  let adapter: StorageAdapter;

  beforeEach(() => {
    adapter = createMemoryStorageAdapter();
  });

  // ------------------------------------------
  // String-based operations
  // ------------------------------------------

  describe('string-based operations', () => {
    it('should return null for missing keys', () => {
      expect(adapter.getItem('missing')).toBeNull();
    });

    it('should store and retrieve a string', () => {
      adapter.setItem('key', 'value');
      expect(adapter.getItem('key')).toBe('value');
    });

    it('should overwrite existing value', () => {
      adapter.setItem('key', 'first');
      adapter.setItem('key', 'second');
      expect(adapter.getItem('key')).toBe('second');
    });

    it('should remove a key', () => {
      adapter.setItem('key', 'value');
      adapter.removeItem('key');
      expect(adapter.getItem('key')).toBeNull();
    });

    it('should clear all items', () => {
      adapter.setItem('a', '1');
      adapter.setItem('b', '2');
      adapter.clear();
      expect(adapter.getItem('a')).toBeNull();
      expect(adapter.getItem('b')).toBeNull();
    });

    it('should serialize non-string values via getItem', () => {
      adapter.set('obj', { x: 1 });
      // getItem should return JSON string for non-string stored values
      const raw = adapter.getItem('obj');
      expect(raw).toBe('{"x":1}');
    });
  });

  // ------------------------------------------
  // Type-safe operations
  // ------------------------------------------

  describe('type-safe operations', () => {
    it('should return null for missing keys via get', () => {
      const result = adapter.get('missing');
      expect(result).toBeNull();
    });

    it('should store and retrieve typed values', () => {
      adapter.set<number>('count', 42);
      expect(adapter.get<number>('count')).toBe(42);
    });

    it('should store and retrieve objects', () => {
      const user = { name: 'Alice', age: 30 };
      adapter.set('user', user);
      expect(adapter.get<typeof user>('user')).toEqual(user);
    });

    it('should store and retrieve arrays', () => {
      adapter.set('items', [1, 2, 3]);
      expect(adapter.get<number[]>('items')).toEqual([1, 2, 3]);
    });

    it('should store and retrieve boolean', () => {
      adapter.set('flag', true);
      expect(adapter.get<boolean>('flag')).toBe(true);
    });

    it('should return null after typed remove', () => {
      adapter.set('key', 'value');
      adapter.remove('key');
      const result = adapter.get('key');
      expect(result).toBeNull();
    });
  });

  // ------------------------------------------
  // keys and has
  // ------------------------------------------

  describe('keys and has', () => {
    it('should return empty keys initially', () => {
      expect(adapter.keys()).toEqual([]);
    });

    it('should return all keys', () => {
      adapter.set('a', 1);
      adapter.set('b', 2);
      const keys = adapter.keys();
      expect(keys).toContain('a');
      expect(keys).toContain('b');
      expect(keys).toHaveLength(2);
    });

    it('should report has correctly', () => {
      expect(adapter.has('key')).toBe(false);
      adapter.set('key', 'val');
      expect(adapter.has('key')).toBe(true);
      adapter.remove('key');
      expect(adapter.has('key')).toBe(false);
    });
  });
});

// ------------------------------------------
// createStorageAdapter factory
// ------------------------------------------

describe('createStorageAdapter', () => {
  it('should create a memory adapter when type is "memory"', () => {
    const adapter = createStorageAdapter({ type: 'memory' });
    adapter.set('key', 'val');
    expect(adapter.get('key')).toBe('val');
  });

  it('should default to "local" type (returns adapter that works without window)', () => {
    // In Node environment, localStorage is unavailable, so the adapter should
    // return null/empty gracefully.
    const adapter = createStorageAdapter();
    // Operations should not throw
    adapter.setItem('key', 'val');
    expect(adapter.getItem('key')).toBeNull(); // no window.localStorage
  });

  it('should create session adapter (returns adapter that works without window)', () => {
    const adapter = createStorageAdapter({ type: 'session' });
    adapter.setItem('key', 'val');
    expect(adapter.getItem('key')).toBeNull(); // no window.sessionStorage
  });
});

// ------------------------------------------
// Singleton (set/use/reset)
// ------------------------------------------

describe('singleton pattern', () => {
  beforeEach(() => {
    resetStorageAdapter();
  });

  it('useStorage should return a default adapter when none is set', () => {
    const adapter = useStorage();
    expect(adapter).toBeDefined();
    // In Node env, localStorage is unavailable so operations are no-ops
    expect(adapter.getItem('key')).toBeNull();
  });

  it('setStorageAdapter should swap the adapter', () => {
    const custom = createMemoryStorageAdapter();
    custom.set('from-custom', true);
    setStorageAdapter(custom);
    expect(useStorage().get('from-custom')).toBe(true);
  });

  it('resetStorageAdapter should clear to null so useStorage returns default', () => {
    const custom = createMemoryStorageAdapter();
    custom.set('from-custom', true);
    setStorageAdapter(custom);
    resetStorageAdapter();
    // After reset, useStorage returns a fresh default (localStorage in Node = no-op)
    expect(useStorage().has('from-custom')).toBe(false);
  });
});
