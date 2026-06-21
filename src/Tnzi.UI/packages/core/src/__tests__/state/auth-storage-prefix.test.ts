import { describe, it, expect, vi } from 'vitest';
import { AuthStateManager } from '../../state/auth';
import type { StateDeps } from '../../state/types';
import type { StorageAdapter } from '../../adapters/storage';
import type { HttpClient } from '../../http/http';

vi.mock('../../services/identity/index', () => ({
  useAuthApi: () => ({ loginWithRefreshToken: vi.fn(), refreshToken: vi.fn(), logout: vi.fn() }),
  useProfileApi: () => ({ get: vi.fn(), update: vi.fn(), changePassword: vi.fn() }),
}));

function mockStorage(): StorageAdapter {
  const s = new Map<string, unknown>();
  return {
    getItem: (k: string) => (s.get(k) as string) ?? null,
    setItem: (k: string, v: string) => s.set(k, v),
    removeItem: (k: string) => s.delete(k),
    clear: () => s.clear(),
    get: <T>(k: string) => (s.get(k) ?? null) as T | null,
    set: <T>(k: string, v: T) => s.set(k, v),
    remove: (k: string) => s.delete(k),
    keys: () => Array.from(s.keys()),
    has: (k: string) => s.has(k),
  };
}

const httpClient = { setAccessToken: vi.fn(), getAccessToken: vi.fn() } as unknown as HttpClient;

function persist(a: AuthStateManager): void {
  (a as unknown as { persistTokens(): void }).persistTokens();
}

describe('AuthStateManager storagePrefix (F7)', () => {
  it('defaults to the tnzi:auth prefix', () => {
    const storage = mockStorage();
    const a = new AuthStateManager({ httpClient, storage } as StateDeps);
    a.accessToken = 'tok';
    persist(a);
    expect(storage.get('tnzi:auth:token')).toBe('tok');
  });

  it('honors a custom storagePrefix', () => {
    const storage = mockStorage();
    const a = new AuthStateManager({ httpClient, storage, storagePrefix: 'myapp:auth' } as StateDeps);
    a.accessToken = 'tok';
    a.refreshToken = 'ref';
    persist(a);
    expect(storage.get('myapp:auth:token')).toBe('tok');
    expect(storage.get('myapp:auth:refresh')).toBe('ref');
    expect(storage.get('tnzi:auth:token')).toBeNull();
  });
});
