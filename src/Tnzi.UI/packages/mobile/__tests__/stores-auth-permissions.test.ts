import { describe, it, expect, beforeEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import type { HttpClient } from '@tnzi/core/http'
import type { StorageAdapter } from '@tnzi/core/adapters/storage'
import { initStoreRuntime } from '../src/stores/factory'
import { useAuthStore } from '../src/stores/auth'

const ACCESS_PROFILE = '/admin/function-authorization/access-profile'

function memoryStorage(entries: Record<string, unknown>): StorageAdapter {
  const store = new Map<string, unknown>(Object.entries(entries))
  return {
    getItem: (key: string) => (store.get(key) as string) ?? null,
    setItem: (key: string, value: string) => store.set(key, value),
    removeItem: (key: string) => store.delete(key),
    clear: () => store.clear(),
    get: <T>(key: string) => (store.get(key) ?? null) as T | null,
    set: <T>(key: string, value: T) => store.set(key, value),
    remove: (key: string) => store.delete(key),
    keys: () => Array.from(store.keys()),
    has: (key: string) => store.has(key),
  } as StorageAdapter
}

/**
 * The store builds its AuthStateManager without a permission source. Before
 * the manager carried a default, `hasPermission()` here answered false for
 * every code, a super admin included, so any UI gated on it never appeared.
 */
describe('useAuthStore permissions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('loads the access profile so hasPermission reflects what the server granted', async () => {
    const get = vi.fn(async (url: string) =>
      url === ACCESS_PROFILE
        ? { succeeded: true, code: 200, data: { permissions: ['system.appearance.update'] } }
        : { succeeded: true, code: 200, data: { id: 'u1', userName: 'admin', roles: [] } })
    const http = { get, setAccessToken: vi.fn(), getAccessToken: vi.fn(() => null) } as unknown as HttpClient
    initStoreRuntime(http, memoryStorage({
      'tnzi:auth:token': 'tok',
      'tnzi:auth:refresh': 'ref',
      'tnzi:auth:expiry': new Date(Date.now() + 3_600_000).toISOString(),
    }))

    const store = useAuthStore()
    await store.restoreAuth()

    expect(get).toHaveBeenCalledWith(ACCESS_PROFILE, { skipAuthRefresh: true })
    expect(store.isAuthenticated).toBe(true)
    expect(store.hasPermission('system.appearance.update')).toBe(true)
    expect(store.hasPermission('something.else')).toBe(false)
  })
})
