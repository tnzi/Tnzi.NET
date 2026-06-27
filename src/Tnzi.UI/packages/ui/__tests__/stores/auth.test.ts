import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Mock AuthStateManager so tests exercise store delegation without touching real http/storage
const managerInstance = {
  // --- State (mutable per test) ---
  isAuthenticated: false,
  accessToken: null as string | null,
  refreshToken: null as string | null,
  tokenExpiry: null as number | null,
  user: null as { id: string; name: string } | null,
  permissions: [] as string[],
  roles: [] as string[],
  isRefreshing: false,
  error: null as string | null,
  // --- Getters ---
  isLoggedIn: false,
  userName: '',
  displayName: '',
  avatar: '',
  userRoles: [] as string[],
  userPermissions: [] as string[],
  isTokenExpired: false,
  tokenExpiresIn: 0,
  // --- Methods ---
  hasRole: vi.fn((r: string) => r === 'admin'),
  hasPermission: vi.fn((p: string) => p === 'read'),
  hasAnyRole: vi.fn((rs: string[]) => rs.includes('admin')),
  hasAnyPermission: vi.fn((ps: string[]) => ps.includes('read')),
  login: vi.fn().mockResolvedValue({ accessToken: 'tok', refreshToken: 'rt', user: { id: 'u1' } }),
  logout: vi.fn().mockResolvedValue(undefined),
  refreshAccessToken: vi.fn().mockResolvedValue(undefined),
  fetchUserProfile: vi.fn().mockResolvedValue(undefined),
  updateProfile: vi.fn().mockResolvedValue({ id: 'u1', name: 'Alice' }),
  changePassword: vi.fn().mockResolvedValue(undefined),
  setAuth: vi.fn(),
  setError: vi.fn(),
  clearAuth: vi.fn(),
  restoreAuth: vi.fn().mockResolvedValue(undefined),
}

vi.mock('@tnzi/core/state', () => ({
  // vitest 4: `new`-able mock must use a regular function (arrow impls are not constructors)
  AuthStateManager: vi.fn(function () { return managerInstance }),
}))

vi.mock('@tnzi/core/adapters/storage', () => ({
  createLocalStorageAdapter: () => ({ getItem: vi.fn(), setItem: vi.fn(), removeItem: vi.fn() }),
}))

// Stub factory runtime accessors since AuthStateManager is mocked and won't use them
vi.mock('../../src/stores/factory', () => ({
  getStoreHttpClient: () => ({ get: vi.fn(), post: vi.fn() }),
  getStoreStorage: () => null,
  STORE_HTTP_CLIENT: Symbol('stub-http'),
  STORE_STORAGE: Symbol('stub-storage'),
}))

import { useAuthStore, useAuth, resetAuthRuntime } from '../../src/stores/auth'
import { AuthStateManager as MockedAuthStateManager } from '@tnzi/core/state'

describe('stores/auth', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetAuthRuntime()
    // Reset mock call history but keep implementation
    Object.values(managerInstance).forEach((v) => {
      if (typeof v === 'function' && 'mockClear' in v) (v as ReturnType<typeof vi.fn>).mockClear()
    })
    // Reset mutable manager state
    managerInstance.isAuthenticated = false
    managerInstance.user = null
    managerInstance.permissions = []
    managerInstance.roles = []
    managerInstance.isLoggedIn = false
    managerInstance.userName = ''
    managerInstance.displayName = ''
    managerInstance.error = null
  })

  afterEach(() => {
    resetAuthRuntime()
  })

  describe('useAuthStore — reactive state proxies', () => {
    it('exposes isAuthenticated through the manager', () => {
      managerInstance.isAuthenticated = true
      const store = useAuthStore()
      expect(store.isAuthenticated).toBe(true)
    })

    it('exposes accessToken/refreshToken/tokenExpiry', () => {
      managerInstance.accessToken = 'abc'
      managerInstance.refreshToken = 'def'
      managerInstance.tokenExpiry = 123456
      const store = useAuthStore()
      expect(store.accessToken).toBe('abc')
      expect(store.refreshToken).toBe('def')
      expect(store.tokenExpiry).toBe(123456)
    })

    it('exposes user/permissions/roles/isRefreshing/error', () => {
      managerInstance.user = { id: 'u1', name: 'Alice' }
      managerInstance.permissions = ['read', 'write']
      managerInstance.roles = ['admin']
      managerInstance.isRefreshing = true
      managerInstance.error = 'oops'
      const store = useAuthStore()
      expect(store.user).toEqual({ id: 'u1', name: 'Alice' })
      expect(store.permissions).toEqual(['read', 'write'])
      expect(store.roles).toEqual(['admin'])
      expect(store.isRefreshing).toBe(true)
      expect(store.error).toBe('oops')
    })

    it('exposes getters (isLoggedIn/userName/displayName/avatar/userRoles/userPermissions/isTokenExpired/tokenExpiresIn)', () => {
      managerInstance.isLoggedIn = true
      managerInstance.userName = 'alice'
      managerInstance.displayName = 'Alice Smith'
      managerInstance.avatar = 'avatar.png'
      managerInstance.userRoles = ['admin']
      managerInstance.userPermissions = ['read']
      managerInstance.isTokenExpired = false
      managerInstance.tokenExpiresIn = 3600
      const store = useAuthStore()
      expect(store.isLoggedIn).toBe(true)
      expect(store.userName).toBe('alice')
      expect(store.displayName).toBe('Alice Smith')
      expect(store.avatar).toBe('avatar.png')
      expect(store.userRoles).toEqual(['admin'])
      expect(store.userPermissions).toEqual(['read'])
      expect(store.isTokenExpired).toBe(false)
      expect(store.tokenExpiresIn).toBe(3600)
    })
  })

  describe('useAuthStore — permission checks', () => {
    it('delegates hasRole to manager', () => {
      const store = useAuthStore()
      expect(store.hasRole('admin')).toBe(true)
      expect(store.hasRole('user')).toBe(false)
      expect(managerInstance.hasRole).toHaveBeenCalledWith('admin')
    })

    it('delegates hasPermission to manager', () => {
      const store = useAuthStore()
      expect(store.hasPermission('read')).toBe(true)
      expect(store.hasPermission('write')).toBe(false)
      expect(managerInstance.hasPermission).toHaveBeenCalledWith('read')
    })

    it('delegates hasAnyRole to manager', () => {
      const store = useAuthStore()
      expect(store.hasAnyRole(['admin', 'guest'])).toBe(true)
      expect(store.hasAnyRole(['guest'])).toBe(false)
      expect(managerInstance.hasAnyRole).toHaveBeenCalledWith(['admin', 'guest'])
    })

    it('delegates hasAnyPermission to manager', () => {
      const store = useAuthStore()
      expect(store.hasAnyPermission(['read'])).toBe(true)
      expect(store.hasAnyPermission(['delete'])).toBe(false)
      expect(managerInstance.hasAnyPermission).toHaveBeenCalledWith(['read'])
    })
  })

  describe('useAuthStore — actions', () => {
    it('login delegates to manager and returns result', async () => {
      const store = useAuthStore()
      const result = await store.login({ username: 'alice', password: 'secret' } as any)
      expect(managerInstance.login).toHaveBeenCalledWith({ username: 'alice', password: 'secret' })
      expect(result).toEqual({ accessToken: 'tok', refreshToken: 'rt', user: { id: 'u1' } })
    })

    it('logout delegates', async () => {
      const store = useAuthStore()
      await store.logout()
      expect(managerInstance.logout).toHaveBeenCalled()
    })

    it('refreshAccessToken delegates', async () => {
      const store = useAuthStore()
      await store.refreshAccessToken()
      expect(managerInstance.refreshAccessToken).toHaveBeenCalled()
    })

    it('fetchUserProfile delegates', async () => {
      const store = useAuthStore()
      await store.fetchUserProfile()
      expect(managerInstance.fetchUserProfile).toHaveBeenCalled()
    })

    it('updateProfile delegates with payload', async () => {
      const store = useAuthStore()
      const result = await store.updateProfile({ displayName: 'A' } as any)
      expect(managerInstance.updateProfile).toHaveBeenCalledWith({ displayName: 'A' })
      expect(result).toEqual({ id: 'u1', name: 'Alice' })
    })

    it('changePassword delegates with args', async () => {
      const store = useAuthStore()
      await store.changePassword('old', 'new')
      expect(managerInstance.changePassword).toHaveBeenCalledWith('old', 'new')
    })

    it('setAuth delegates', () => {
      const store = useAuthStore()
      const result = { accessToken: 'a', refreshToken: 'r' } as any
      store.setAuth(result)
      expect(managerInstance.setAuth).toHaveBeenCalledWith(result)
    })

    it('setError delegates (both value and null)', () => {
      const store = useAuthStore()
      store.setError('bad')
      expect(managerInstance.setError).toHaveBeenCalledWith('bad')
      store.setError(null)
      expect(managerInstance.setError).toHaveBeenCalledWith(null)
    })

    it('clearAuth delegates', () => {
      const store = useAuthStore()
      store.clearAuth()
      expect(managerInstance.clearAuth).toHaveBeenCalled()
    })

    it('restoreAuth delegates', async () => {
      const store = useAuthStore()
      await store.restoreAuth()
      expect(managerInstance.restoreAuth).toHaveBeenCalled()
    })
  })

  describe('useAuth composable wrapper', () => {
    it('returns reactive computed refs for state', () => {
      managerInstance.isAuthenticated = true
      managerInstance.user = { id: 'u1', name: 'Alice' }
      managerInstance.userName = 'alice'
      managerInstance.displayName = 'Alice Smith'
      managerInstance.avatar = 'a.png'
      managerInstance.userRoles = ['admin']
      managerInstance.userPermissions = ['read']
      managerInstance.isRefreshing = true
      managerInstance.error = 'err'
      managerInstance.isLoggedIn = true
      managerInstance.isTokenExpired = false
      managerInstance.tokenExpiresIn = 100

      const auth = useAuth()
      expect(auth.isAuthenticated.value).toBe(true)
      expect(auth.user.value).toEqual({ id: 'u1', name: 'Alice' })
      expect(auth.userName.value).toBe('alice')
      expect(auth.displayName.value).toBe('Alice Smith')
      expect(auth.avatar.value).toBe('a.png')
      expect(auth.roles.value).toEqual(['admin'])
      expect(auth.permissions.value).toEqual(['read'])
      expect(auth.isLoading.value).toBe(true)
      expect(auth.error.value).toBe('err')
      expect(auth.isLoggedIn.value).toBe(true)
      expect(auth.isTokenExpired.value).toBe(false)
      expect(auth.tokenExpiresIn.value).toBe(100)
    })

    it('binds actions so they can be destructured and called standalone', async () => {
      const auth = useAuth()
      await auth.login({ username: 'a', password: 'b' } as any)
      await auth.logout()
      await auth.refreshAccessToken()
      await auth.fetchUserProfile()
      await auth.updateProfile({ displayName: 'X' } as any)
      await auth.changePassword('o', 'n')
      auth.setAuth({ accessToken: 'a' } as any)
      await auth.restoreAuth()
      expect(managerInstance.login).toHaveBeenCalled()
      expect(managerInstance.logout).toHaveBeenCalled()
      expect(managerInstance.refreshAccessToken).toHaveBeenCalled()
      expect(managerInstance.fetchUserProfile).toHaveBeenCalled()
      expect(managerInstance.updateProfile).toHaveBeenCalled()
      expect(managerInstance.changePassword).toHaveBeenCalled()
      expect(managerInstance.setAuth).toHaveBeenCalled()
      expect(managerInstance.restoreAuth).toHaveBeenCalled()
    })

    it('binds permission checks', () => {
      const auth = useAuth()
      expect(auth.hasRole('admin')).toBe(true)
      expect(auth.hasPermission('read')).toBe(true)
      expect(auth.hasAnyRole(['admin'])).toBe(true)
      expect(auth.hasAnyPermission(['read'])).toBe(true)
    })
  })

  describe('resetAuthRuntime', () => {
    it('creates a fresh manager instance on next access', () => {
      const ctor = vi.mocked(MockedAuthStateManager)
      useAuthStore().hasRole('admin')
      const callsBefore = ctor.mock.calls.length
      resetAuthRuntime()
      useAuthStore().hasRole('admin')
      const callsAfter = ctor.mock.calls.length
      expect(callsAfter).toBeGreaterThan(callsBefore)
    })
  })
})
