import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  setActiveRuntimeAdapter,
  useRuntimeAdapter,
  resetRuntimeAdapter,
  type IRuntimeAdapter,
} from '../../adapters/runtime-adapter';

describe('RuntimeAdapterRuntime', () => {
  beforeEach(() => {
    resetRuntimeAdapter();
  });

  it('should provide default adapter', () => {
    const adapter = useRuntimeAdapter();
    expect(adapter).toBeDefined();
    expect(adapter.router).toBeDefined();
    expect(adapter.storage).toBeDefined();
  });

  it('should default router to no-op', () => {
    const adapter = useRuntimeAdapter();
    expect(adapter.router.getCurrentPath()).toBe('/');
    expect(() => adapter.router.push('/test')).not.toThrow();
  });

  it('should default storage to memory adapter', () => {
    const adapter = useRuntimeAdapter();
    adapter.storage.set('key', 'value');
    expect(adapter.storage.get('key')).toBe('value');
    adapter.storage.remove('key');
    expect(adapter.storage.get('key')).toBeNull();
  });

  it('should allow setting custom adapter', () => {
    const custom: IRuntimeAdapter = {
      router: {
        push: vi.fn(),
        replace: vi.fn(),
        back: vi.fn(),
        getCurrentPath: vi.fn(() => '/custom'),
      },
      storage: {
        getItem: vi.fn(() => null),
        setItem: vi.fn(),
        removeItem: vi.fn(),
        clear: vi.fn(),
        get: vi.fn(() => null),
        set: vi.fn(),
        remove: vi.fn(),
        keys: vi.fn(() => []),
        has: vi.fn(() => false),
      },
    };
    setActiveRuntimeAdapter(custom);
    expect(useRuntimeAdapter()).toBe(custom);
    expect(useRuntimeAdapter().router.getCurrentPath()).toBe('/custom');
  });

  it('should reset to default', () => {
    const custom: IRuntimeAdapter = {
      router: { push: vi.fn(), replace: vi.fn(), back: vi.fn(), getCurrentPath: vi.fn(() => '/x') },
      storage: { getItem: vi.fn(() => null), setItem: vi.fn(), removeItem: vi.fn(), clear: vi.fn(), get: vi.fn(() => null), set: vi.fn(), remove: vi.fn(), keys: vi.fn(() => []), has: vi.fn(() => false) },
    };
    setActiveRuntimeAdapter(custom);
    resetRuntimeAdapter();
    expect(useRuntimeAdapter()).not.toBe(custom);
    expect(useRuntimeAdapter().router.getCurrentPath()).toBe('/');
  });
});
