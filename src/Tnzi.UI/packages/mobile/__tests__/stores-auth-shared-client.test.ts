import { describe, it, expect, beforeEach } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { createTnziClient } from '@tnzi/core/state'
import type { StorageAdapter } from '@tnzi/core/adapters/storage'
import { initStoreRuntime } from '../src/stores/factory'
import { useAuthStore } from '../src/stores/auth'

function memoryStorage(): StorageAdapter {
  const store = new Map<string, unknown>()
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

const SESSION = {
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  expiresIn: 3600,
  tokenType: 'Bearer',
  user: { id: 'u1', userName: 'admin', roles: [], permissions: [] },
}

/**
 * A `createTnziClient()` HttpClient refreshes an expired token through the
 * client's own auth state. The store used to build a second AuthStateManager
 * over the same HttpClient: a session it signed in lived only in that second
 * instance, so on the first 401 the client asked its own (empty) instance for
 * a refresh, failed, and every request kept failing while the store still
 * reported the user as signed in.
 */
describe('useAuthStore over a createTnziClient() runtime', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  // One test: the store keeps its manager for the life of the module (mobile has
  // no runtime reset), so the client must be the first one it sees.
  it('is the client auth state: the refresh chain sees the session, and a session ended there ends here', () => {
    const client = createTnziClient({ storage: memoryStorage() })
    initStoreRuntime(client.http)

    const store = useAuthStore()
    store.setAuth(SESSION as never)

    expect(client.auth.isAuthenticated).toBe(true)
    expect(client.auth.refreshToken).toBe('refresh-1')
    expect(client.http.getAccessToken()).toBe('access-1')

    // What the client's onUnauthorized does when a refresh is rejected.
    client.auth.clearAuth()
    expect(store.isAuthenticated).toBe(false)
  })
})
