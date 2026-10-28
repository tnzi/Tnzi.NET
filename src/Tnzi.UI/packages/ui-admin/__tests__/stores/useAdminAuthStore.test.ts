import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { createApp, nextTick } from 'vue'
import piniaPluginPersistedstate from 'pinia-plugin-persistedstate'
import { useAdminAuthStore } from '../../src/stores/useAdminAuthStore'

describe('useAdminAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts logged out with no token', () => {
    const store = useAdminAuthStore()
    expect(store.isLogin).toBe(false)
    expect(store.token).toBe('')
    expect(store.userInfo).toBeNull()
  })

  it('setToken marks the user as logged in', () => {
    const store = useAdminAuthStore()
    store.setToken('abc123')
    expect(store.token).toBe('abc123')
    expect(store.isLogin).toBe(true)
  })

  it('setUserInfo populates userInfo', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'alice', roles: ['admin'], permissions: ['user:read'] })
    expect(store.userInfo?.username).toBe('alice')
    expect(store.userRoles).toContain('admin')
    expect(store.userPermissions).toContain('user:read')
  })

  it('hasPermission returns true when permission present', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: ['user:read', 'user:write'] })
    expect(store.hasPermission('user:read')).toBe(true)
    expect(store.hasPermission('user:delete')).toBe(false)
  })

  it('hasAnyPermission returns true if any match', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: ['user:read'] })
    expect(store.hasAnyPermission(['user:read', 'user:write'])).toBe(true)
    expect(store.hasAnyPermission(['user:write', 'user:delete'])).toBe(false)
  })

  it('hasAllPermissions returns true only if all match', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: ['user:read', 'user:write'] })
    expect(store.hasAllPermissions(['user:read', 'user:write'])).toBe(true)
    expect(store.hasAllPermissions(['user:read', 'user:delete'])).toBe(false)
  })

  it('logout clears token and userInfo', () => {
    const store = useAdminAuthStore()
    store.setToken('t')
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: [] })
    store.logout()
    expect(store.token).toBe('')
    expect(store.userInfo).toBeNull()
    expect(store.isLogin).toBe(false)
  })

  it('logout resets the super-user flag (no cross-session leak)', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: [], permissions: [] })
    store.setSuperUser(true)
    store.logout()
    expect(store.isSuperUser).toBe(false)
  })

  it('hasRole returns true when role present', () => {
    const store = useAdminAuthStore()
    store.setUserInfo({ id: '1', username: 'a', roles: ['admin', 'editor'], permissions: [] })
    expect(store.hasRole('admin')).toBe(true)
    expect(store.hasRole('guest')).toBe(false)
  })
})

// The access token is a credential. The core client decides where it lives (in
// cookie mode: memory only, nothing in web storage); this store is a UI gate and
// must not put a copy where any script or local process can read it.
describe('useAdminAuthStore persistence', () => {
  const KEY = 'tnzi-admin-auth'

  /** A pinia wired the way `install()` wires it, so `persist` actually runs. */
  function persistedPinia(): void {
    const pinia = createPinia()
    pinia.use(piniaPluginPersistedstate)
    createApp({ render: () => null }).use(pinia)
    setActivePinia(pinia)
  }

  beforeEach(() => {
    localStorage.clear()
    persistedPinia()
  })

  it('★ never writes the access token to web storage', async () => {
    const store = useAdminAuthStore()
    store.setToken('secret-access-token', 'secret-refresh-token')
    store.setUserInfo({ id: 'u1', username: 'alice', roles: [], permissions: [] })
    await nextTick() // persistence runs as a pre-flush subscription

    const raw = localStorage.getItem(KEY) ?? ''
    expect(raw).toContain('alice') // the write did happen
    expect(raw).not.toContain('secret-access-token')
    expect(raw).not.toContain('secret-refresh-token')
    expect(store.token).toBe('secret-access-token') // still available in memory
  })

  it('a reload keeps the signed-in marker without the credential', async () => {
    const before = useAdminAuthStore()
    before.setToken('secret-access-token')
    before.setUserInfo({ id: 'u1', username: 'alice', roles: [], permissions: [] })
    await nextTick()

    persistedPinia() // a fresh page: hydrate from storage
    const after = useAdminAuthStore()
    expect(after.isLogin).toBe(true)
    expect(after.token).toBe('')
    expect(after.userInfo?.id).toBe('u1')
  })

  it('a sign-out survives a reload', async () => {
    const before = useAdminAuthStore()
    before.setToken('secret-access-token')
    await nextTick()
    before.logout()
    await nextTick()

    persistedPinia()
    expect(useAdminAuthStore().isLogin).toBe(false)
  })

  it('★ scrubs a token persisted by an earlier version and keeps the session marker', () => {
    localStorage.setItem(KEY, JSON.stringify({
      token: 'legacy-access-token',
      userInfo: { id: 'u1', username: 'alice', roles: [], permissions: [] },
      isSuperUser: false,
    }))
    persistedPinia()

    const store = useAdminAuthStore()
    expect(store.isLogin).toBe(true)
    expect(store.token).toBe('')
    expect(localStorage.getItem(KEY) ?? '').not.toContain('legacy-access-token')
  })

  it('an earlier version\'s signed-out payload stays signed out', () => {
    localStorage.setItem(KEY, JSON.stringify({ token: '', userInfo: null, isSuperUser: false }))
    persistedPinia()
    expect(useAdminAuthStore().isLogin).toBe(false)
  })
})
