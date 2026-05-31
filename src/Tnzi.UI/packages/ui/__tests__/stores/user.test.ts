import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Mock UserStateManager so tests exercise store delegation without touching real http/storage.
// The store is a thin proxy: state is read through computed(() => manager.xxx),
// actions forward to manager methods.
const managerInstance = {
  // --- State (mutable per test) ---
  currentUser: null as { id: string; userName?: string; nickname?: string; avatar?: string; email?: string; roles?: string[] } | null,
  preferences: { theme: 'light', language: 'en-US', density: 'default', fontSize: 14 } as any,
  recentItems: [] as any[],
  favorites: [] as any[],
  isLoading: false,
  error: null as string | null,
  // --- Getters ---
  isLoaded: false,
  isAuthenticated: false,
  displayName: '',
  userName: '',
  avatar: null as string | null,
  email: null as string | null,
  roles: [] as string[],
  theme: 'light',
  language: 'en-US',
  recentItemsCount: 0,
  favoritesCount: 0,
  // --- Methods ---
  fetchCurrentUser: vi.fn().mockResolvedValue(undefined),
  updateProfile: vi.fn().mockResolvedValue({ id: 'u1', userName: 'alice', nickname: 'Ally' }),
  updatePreferences: vi.fn(),
  resetPreferences: vi.fn(),
  setTheme: vi.fn(),
  setLanguage: vi.fn(),
  addRecentItem: vi.fn(),
  removeRecentItem: vi.fn(),
  clearRecentItems: vi.fn(),
  addFavorite: vi.fn(),
  removeFavorite: vi.fn(),
  isFavorite: vi.fn((id: string) => id === 'fav'),
  loadPersistedData: vi.fn(),
  persistData: vi.fn(),
}

vi.mock('@tnzi/core/state', () => ({
  UserStateManager: vi.fn(() => managerInstance),
}))

vi.mock('@tnzi/core/adapters/storage', () => ({
  createLocalStorageAdapter: () => ({ getItem: vi.fn(), setItem: vi.fn(), removeItem: vi.fn() }),
}))

// Stub the theme adapter so manager construction stays inert
vi.mock('../../src/adapters/theme', () => ({
  createThemeAdapter: () => ({ applyTheme: vi.fn(), getResolvedTheme: () => 'light' }),
}))

// Stub factory runtime accessors since UserStateManager is mocked and won't use them
vi.mock('../../src/stores/factory', () => ({
  getStoreHttpClient: () => ({ get: vi.fn(), post: vi.fn() }),
  getStoreStorage: () => null,
}))

import { useUserStore, useUser, resetUserRuntime } from '../../src/stores/user'
import { UserStateManager as MockedUserStateManager } from '@tnzi/core/state'

describe('stores/user', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetUserRuntime()
    // Reset mock call history but keep implementation
    Object.values(managerInstance).forEach((v) => {
      if (typeof v === 'function' && 'mockClear' in v) (v as ReturnType<typeof vi.fn>).mockClear()
    })
    // Reset mutable manager state
    managerInstance.currentUser = null
    managerInstance.recentItems = []
    managerInstance.favorites = []
    managerInstance.isLoading = false
    managerInstance.error = null
    managerInstance.isLoaded = false
    managerInstance.isAuthenticated = false
    managerInstance.displayName = ''
    managerInstance.userName = ''
    managerInstance.avatar = null
    managerInstance.email = null
    managerInstance.roles = []
    managerInstance.theme = 'light'
    managerInstance.language = 'en-US'
    managerInstance.recentItemsCount = 0
    managerInstance.favoritesCount = 0
  })

  afterEach(() => {
    resetUserRuntime()
  })

  describe('useUserStore — reactive state proxies', () => {
    it('exposes currentUser/isLoading/error through the manager', () => {
      managerInstance.currentUser = { id: 'u1', userName: 'alice' }
      managerInstance.isLoading = true
      managerInstance.error = 'boom'
      const s = useUserStore()
      expect(s.currentUser).toEqual({ id: 'u1', userName: 'alice' })
      expect(s.isLoading).toBe(true)
      expect(s.error).toBe('boom')
    })

    it('exposes preferences/recentItems/favorites', () => {
      managerInstance.preferences = { theme: 'dark', language: 'zh-CN' } as any
      managerInstance.recentItems = [{ id: 'r1' }] as any
      managerInstance.favorites = [{ id: 'f1' }] as any
      const s = useUserStore()
      expect(s.preferences).toEqual({ theme: 'dark', language: 'zh-CN' })
      expect(s.recentItems).toEqual([{ id: 'r1' }])
      expect(s.favorites).toEqual([{ id: 'f1' }])
    })
  })

  describe('useUserStore — getters', () => {
    it('proxies isLoaded/isAuthenticated/userName/avatar/email/roles/theme/language/counts', () => {
      managerInstance.isLoaded = true
      managerInstance.isAuthenticated = true
      managerInstance.userName = 'alice'
      managerInstance.avatar = 'a.png'
      managerInstance.email = 'a@b.com'
      managerInstance.roles = ['admin']
      managerInstance.theme = 'dark'
      managerInstance.language = 'zh-CN'
      managerInstance.recentItemsCount = 3
      managerInstance.favoritesCount = 2
      const s = useUserStore()
      expect(s.isLoaded).toBe(true)
      expect(s.isAuthenticated).toBe(true)
      expect(s.userName).toBe('alice')
      expect(s.avatar).toBe('a.png')
      expect(s.email).toBe('a@b.com')
      expect(s.roles).toEqual(['admin'])
      expect(s.theme).toBe('dark')
      expect(s.language).toBe('zh-CN')
      expect(s.recentItemsCount).toBe(3)
      expect(s.favoritesCount).toBe(2)
    })

    it('displayName proxies the manager value', () => {
      managerInstance.displayName = 'Alice Smith'
      const s = useUserStore()
      expect(s.displayName).toBe('Alice Smith')
    })

    it('displayName falls back to "Guest" when manager returns empty string', () => {
      managerInstance.displayName = ''
      const s = useUserStore()
      expect(s.displayName).toBe('Guest')
    })
  })

  describe('useUserStore — profile actions', () => {
    it('fetchCurrentUser delegates to manager', async () => {
      const s = useUserStore()
      await s.fetchCurrentUser()
      expect(managerInstance.fetchCurrentUser).toHaveBeenCalled()
    })

    it('updateProfile delegates with payload and returns result', async () => {
      const s = useUserStore()
      const result = await s.updateProfile({ nickname: 'Ally' } as any)
      expect(managerInstance.updateProfile).toHaveBeenCalledWith({ nickname: 'Ally' })
      expect(result).toEqual({ id: 'u1', userName: 'alice', nickname: 'Ally' })
    })
  })

  describe('useUserStore — preferences actions', () => {
    it('updatePreferences delegates', async () => {
      const s = useUserStore()
      await s.updatePreferences({ theme: 'dark' as any })
      expect(managerInstance.updatePreferences).toHaveBeenCalledWith({ theme: 'dark' })
    })

    it('resetPreferences delegates', () => {
      const s = useUserStore()
      s.resetPreferences()
      expect(managerInstance.resetPreferences).toHaveBeenCalled()
    })

    it('setTheme delegates', () => {
      const s = useUserStore()
      s.setTheme('dark' as any)
      expect(managerInstance.setTheme).toHaveBeenCalledWith('dark')
    })

    it('setLanguage delegates', () => {
      const s = useUserStore()
      s.setLanguage('zh-CN')
      expect(managerInstance.setLanguage).toHaveBeenCalledWith('zh-CN')
    })
  })

  describe('useUserStore — recent items + favorites actions', () => {
    it('addRecentItem/removeRecentItem/clearRecentItems delegate', () => {
      const s = useUserStore()
      const item = { id: '1', title: 'A', type: 'page', url: '/a' } as any
      s.addRecentItem(item)
      expect(managerInstance.addRecentItem).toHaveBeenCalledWith(item)
      s.removeRecentItem('1')
      expect(managerInstance.removeRecentItem).toHaveBeenCalledWith('1')
      s.clearRecentItems()
      expect(managerInstance.clearRecentItems).toHaveBeenCalled()
    })

    it('addFavorite/removeFavorite/isFavorite delegate', () => {
      const s = useUserStore()
      const item = { id: 'fav', title: 'F', type: 'page', url: '/f' } as any
      s.addFavorite(item)
      expect(managerInstance.addFavorite).toHaveBeenCalledWith(item)
      expect(s.isFavorite('fav')).toBe(true)
      expect(s.isFavorite('nope')).toBe(false)
      s.removeFavorite('fav')
      expect(managerInstance.removeFavorite).toHaveBeenCalledWith('fav')
    })
  })

  describe('useUserStore — persistence actions', () => {
    it('loadPersistedData/persistData delegate', async () => {
      const s = useUserStore()
      await s.loadPersistedData()
      expect(managerInstance.loadPersistedData).toHaveBeenCalled()
      s.persistData()
      expect(managerInstance.persistData).toHaveBeenCalled()
    })
  })

  describe('useUser composable', () => {
    it('exposes computed refs and bound actions', async () => {
      managerInstance.isAuthenticated = true
      managerInstance.theme = 'dark'
      managerInstance.language = 'zh-CN'
      managerInstance.recentItemsCount = 1
      managerInstance.favoritesCount = 1
      const u = useUser()
      expect(u.currentUser.value).toBeNull()
      expect(u.isAuthenticated.value).toBe(true)
      expect(u.theme.value).toBe('dark')
      expect(u.language.value).toBe('zh-CN')
      expect(u.recentItemsCount.value).toBe(1)
      expect(u.favoritesCount.value).toBe(1)
      u.setTheme('light' as any)
      expect(managerInstance.setTheme).toHaveBeenCalledWith('light')
      u.addRecentItem({ id: '1', title: 'A', type: 'page', url: '/a' } as any)
      expect(managerInstance.addRecentItem).toHaveBeenCalled()
      u.addFavorite({ id: '2', title: 'B', type: 'page', url: '/b' } as any)
      expect(managerInstance.addFavorite).toHaveBeenCalled()
      expect(u.isFavorite('fav')).toBe(true)
      u.clearRecentItems()
      expect(managerInstance.clearRecentItems).toHaveBeenCalled()
    })

    it('composable fetchCurrentUser delegates to manager', async () => {
      const u = useUser()
      await u.fetchCurrentUser()
      expect(managerInstance.fetchCurrentUser).toHaveBeenCalled()
    })
  })

  describe('resetUserRuntime', () => {
    it('creates a fresh manager instance on next access', () => {
      const ctor = vi.mocked(MockedUserStateManager)
      useUserStore().fetchCurrentUser()
      const callsBefore = ctor.mock.calls.length
      resetUserRuntime()
      useUserStore().fetchCurrentUser()
      const callsAfter = ctor.mock.calls.length
      expect(callsAfter).toBeGreaterThan(callsBefore)
    })
  })
})
