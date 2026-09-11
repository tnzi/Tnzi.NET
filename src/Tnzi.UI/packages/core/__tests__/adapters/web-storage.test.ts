import { describe, it, expect, vi, afterEach } from 'vitest';
import { createLocalStorageAdapter, createSessionStorageAdapter } from '../../src/adapters/storage';
import { setLoggerAdapter, resetLoggerAdapter } from '../../src/adapters/logger';

type WindowSlot = typeof globalThis & { window?: unknown };
const globals = globalThis as WindowSlot;
const originalWindow = globals.window;

afterEach(() => {
  if (originalWindow === undefined) delete globals.window;
  else globals.window = originalWindow;
  resetLoggerAdapter();
});

/**
 * A Storage double that also exposes stored keys as own enumerable properties,
 * because the adapter's prefixed `clear()` enumerates `Object.keys(storage)`
 * the way real Web Storage allows.
 */
function fakeStorage(): Storage {
  const store: Record<string, string> = {};
  const api: Record<string, unknown> = {
    get length() {
      return Object.keys(store).length;
    },
    clear() {
      for (const key of Object.keys(store)) {
        delete store[key];
        delete api[key];
      }
    },
    getItem(key: string) {
      return key in store ? store[key] : null;
    },
    key(index: number) {
      return Object.keys(store)[index] ?? null;
    },
    removeItem(key: string) {
      delete store[key];
      delete api[key];
    },
    setItem(key: string, value: string) {
      store[key] = String(value);
      api[key] = String(value);
    },
  };
  return api as unknown as Storage;
}

function spyLogger() {
  const warn = vi.fn();
  setLoggerAdapter({ debug: vi.fn(), info: vi.fn(), warn, error: vi.fn() });
  return warn;
}

describe('web storage adapters', () => {
  it('become inert without a DOM: writes are dropped and reads report empty', () => {
    // Deliberately not a memory store: a server-side singleton would carry one
    // request's tokens into the next.
    delete globals.window;
    const adapter = createLocalStorageAdapter('t:');

    adapter.setItem('a', '1');
    adapter.set('b', { x: 1 });

    expect(adapter.getItem('a')).toBeNull();
    expect(adapter.get('b')).toBeNull();
    expect(adapter.has('a')).toBe(false);
    expect(adapter.keys()).toEqual([]);
    expect(() => {
      adapter.removeItem('a');
      adapter.remove('b');
      adapter.clear();
    }).not.toThrow();
  });

  it('read and write the browser store under the configured prefix', () => {
    const local = fakeStorage();
    const session = fakeStorage();
    globals.window = { localStorage: local, sessionStorage: session };

    const adapter = createLocalStorageAdapter('app:');
    adapter.set('user', { id: 7 });
    adapter.setItem('raw', 'text');

    expect(local.getItem('app:user')).toBe(JSON.stringify({ id: 7 }));
    expect(adapter.get<{ id: number }>('user')).toEqual({ id: 7 });
    expect(adapter.getItem('raw')).toBe('text');
    expect(adapter.has('user')).toBe(true);
    expect(adapter.has('missing')).toBe(false);
    expect(adapter.keys().sort()).toEqual(['raw', 'user']);

    adapter.remove('user');
    adapter.removeItem('raw');
    expect(adapter.keys()).toEqual([]);

    const other = createSessionStorageAdapter();
    other.setItem('k', 'v');
    expect(session.getItem('k')).toBe('v');
    expect(local.getItem('k')).toBeNull();
  });

  it('a prefixed clear only removes its own keys', () => {
    const local = fakeStorage();
    globals.window = { localStorage: local, sessionStorage: fakeStorage() };
    local.setItem('foreign', 'keep me');

    const adapter = createLocalStorageAdapter('app:');
    adapter.setItem('a', '1');
    adapter.setItem('b', '2');
    adapter.clear();

    expect(adapter.keys()).toEqual([]);
    expect(local.getItem('foreign')).toBe('keep me');

    createLocalStorageAdapter().clear();
    expect(local.getItem('foreign')).toBeNull();
  });

  it('falls back to memory and warns once when the storage getter itself throws', () => {
    // Safari private mode and sandboxed iframes raise from the property getter.
    const warn = spyLogger();
    globals.window = {
      get localStorage(): Storage {
        throw new Error('SecurityError');
      },
    };

    const adapter = createLocalStorageAdapter();
    adapter.set('a', 1);
    adapter.set('b', 2);

    expect(adapter.get('a')).toBe(1);
    expect(adapter.keys().sort()).toEqual(['a', 'b']);
    expect(warn).toHaveBeenCalledTimes(1);
  });

  it('resolves the backing store on first use, not when the adapter is created', () => {
    const warn = spyLogger();
    globals.window = {
      get localStorage(): Storage {
        throw new Error('SecurityError');
      },
    };

    const adapter = createLocalStorageAdapter();
    expect(warn).not.toHaveBeenCalled();

    adapter.keys();
    expect(warn).toHaveBeenCalledTimes(1);
  });

  it('returns null instead of throwing when a stored value is not valid JSON', () => {
    const local = fakeStorage();
    globals.window = { localStorage: local, sessionStorage: fakeStorage() };
    local.setItem('broken', '{not json');

    expect(createLocalStorageAdapter().get('broken')).toBeNull();
  });
});
