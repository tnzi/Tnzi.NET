import { describe, expect, it, vi } from 'vitest'
import { AuthStateManager } from '../../src/state/auth'
import type { StateDeps } from '../../src/state/types'
import type { HttpClient } from '../../src/http/http'
import type { StorageAdapter } from '../../src/adapters/storage'

const profileApiMocks = vi.hoisted(() => ({
  get: vi.fn(async () => ({ succeeded: true, data: { id: 'u1', userName: 'admin' } })),
}))
vi.mock('../../src/services/identity/index', () => ({
  useAuthApi: () => ({}),
  useProfileApi: () => profileApiMocks,
}))

const ACCESS_PROFILE = '/admin/function-authorization/access-profile'

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
  }
}

function httpClient(permissions: unknown) {
  const get = vi.fn(async (url: string) =>
    url === ACCESS_PROFILE ? { succeeded: true, code: 200, data: { permissions } } : { succeeded: false, code: 404 })
  return { client: { setAccessToken: vi.fn(), getAccessToken: vi.fn(() => null), get } as unknown as HttpClient, get }
}

/**
 * Only `createTnziClient` used to supply a permission source. The Pinia auth
 * stores of `@tnzi/ui` and `@tnzi/mobile` build the manager directly, so their
 * `hasPermission()` answered false for every code, a super admin included, and
 * a menu gated on it never appeared, with nothing logged.
 */
describe('AuthStateManager default permission source', () => {
  it('loads the access profile when no permissionsFetchFn is given', async () => {
    const { client, get } = httpClient(['system.appearance.update'])
    const deps: StateDeps = { httpClient: client, storage: memoryStorage() }
    const auth = new AuthStateManager(deps)

    await auth.applyTokenSession({ accessToken: 'tok', refreshToken: 'ref', expiresIn: 3600 })

    expect(get).toHaveBeenCalledWith(ACCESS_PROFILE, { skipAuthRefresh: true })
    expect(auth.hasPermission('system.appearance.update')).toBe(true)
  })

  it('treats an unexpected envelope as holding nothing', async () => {
    const { client } = httpClient('not-a-list')
    const auth = new AuthStateManager({ httpClient: client, storage: memoryStorage() })

    await auth.applyTokenSession({ accessToken: 'tok', refreshToken: 'ref', expiresIn: 3600 })

    expect(auth.permissions).toEqual([])
  })

  it('makes no call when permissionsFetchFn is null', async () => {
    const { client, get } = httpClient(['system.appearance.update'])
    const auth = new AuthStateManager({ httpClient: client, storage: memoryStorage(), permissionsFetchFn: null })

    await auth.applyTokenSession({ accessToken: 'tok', refreshToken: 'ref', expiresIn: 3600 })

    expect(get.mock.calls.map(([url]) => url)).not.toContain(ACCESS_PROFILE)
    expect(auth.hasPermission('system.appearance.update')).toBe(false)
  })

  it('uses a custom permissionsFetchFn instead of the default', async () => {
    const { client, get } = httpClient(['from.profile'])
    const permissionsFetchFn = vi.fn(async () => ['custom.code'])
    const auth = new AuthStateManager({ httpClient: client, storage: memoryStorage(), permissionsFetchFn })

    await auth.applyTokenSession({ accessToken: 'tok', refreshToken: 'ref', expiresIn: 3600 })

    expect(permissionsFetchFn).toHaveBeenCalled()
    expect(get.mock.calls.map(([url]) => url)).not.toContain(ACCESS_PROFILE)
    expect(auth.hasPermission('custom.code')).toBe(true)
  })
})
