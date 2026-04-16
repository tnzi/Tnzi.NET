import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// --- Mocks: defaultUserPreferences + useProfileApi ---
vi.mock('@tnzi/core/state', () => ({
  defaultUserPreferences: { theme: 'light', language: 'en-us', density: 'default', fontSize: 14 },
}))

const profileApiMock = {
  get: vi.fn().mockResolvedValue({ succeeded: true, data: { id: 'u1', userName: 'alice', nickname: 'Alice', avatar: 'a.png', email: 'a@b.com', roles: ['admin'] } }),
  update: vi.fn(async (d: any) => ({ succeeded: true, data: { id: 'u1', userName: 'alice', nickname: d.nickname ?? 'Alice' } })),
}
vi.mock('@tnzi/core/services/identity', () => ({
  useProfileApi: vi.fn(() => profileApiMock),
}))

// In-memory storage mock exposed to tests via getStoreStorage
const storageBag: Record<string, unknown> = {}
const storageMock = {
  get: vi.fn((k: string) => storageBag[k] ?? null),
  set: vi.fn((k: string, v: unknown) => { storageBag[k] = v }),
  remove: vi.fn((k: string) => { delete storageBag[k] }),
}

vi.mock('../../src/stores/factory', () => ({
  getStoreHttpClient: () => ({ get: vi.fn(), post: vi.fn() }),
  getStoreStorage: () => storageMock,
}))

const applyThemeSpy = vi.fn()
const applyLanguageSpy = vi.fn()
vi.mock('../../src/utils/naive-helpers', () => ({
  applyThemeToDOM: (t: string) => applyThemeSpy(t),
  applyLanguageToDOM: (l: string) => applyLanguageSpy(l),
}))

import { useUserStore, useUser } from '../../src/stores/user'

describe('stores/user', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    Object.keys(storageBag).forEach((k) => delete storageBag[k])
    profileApiMock.get.mockClear()
    profileApiMock.update.mockClear()
    storageMock.get.mockClear()
    storageMock.set.mockClear()
    applyThemeSpy.mockClear()
    applyLanguageSpy.mockClear()
  })

  describe('initial state + getters', () => {
    it('starts empty with default preferences and guest display name', () => {
      const s = useUserStore()
      expect(s.currentUser).toBeNull()
      expect(s.isLoaded).toBe(false)
      expect(s.isAuthenticated).toBe(false)
      expect(s.displayName).toBe('Guest')
      expect(s.userName).toBe('')
      expect(s.avatar).toBeNull()
      expect(s.email).toBeNull()
      expect(s.roles).toEqual([])
      expect(s.theme).toBe('light')
      expect(s.language).toBe('en-us')
      expect(s.recentItemsCount).toBe(0)
      expect(s.favoritesCount).toBe(0)
    })

    it('derives display name from nickname, userName fallback, then Guest', () => {
      const s = useUserStore()
      s.currentUser = { id: 'u1', userName: 'alice', nickname: 'Alice' } as any
      expect(s.displayName).toBe('Alice')
      s.currentUser = { id: 'u1', userName: 'bob' } as any
      expect(s.displayName).toBe('bob')
      s.currentUser = { id: 'u1' } as any
      expect(s.displayName).toBe('Guest')
    })

    it('reflects roles/avatar/email from currentUser', () => {
      const s = useUserStore()
      s.currentUser = { id: 'u1', userName: 'a', avatar: 'x.png', email: 'a@b.com', roles: ['r1'] } as any
      expect(s.avatar).toBe('x.png')
      expect(s.email).toBe('a@b.com')
      expect(s.roles).toEqual(['r1'])
    })
  })

  describe('fetchCurrentUser', () => {
    it('populates currentUser on success and clears loading', async () => {
      const s = useUserStore()
      await s.fetchCurrentUser()
      expect(profileApiMock.get).toHaveBeenCalled()
      expect(s.currentUser?.userName).toBe('alice')
      expect(s.isLoading).toBe(false)
      expect(s.error).toBeNull()
    })

    it('skips assignment when result not succeeded', async () => {
      profileApiMock.get.mockResolvedValueOnce({ succeeded: false, data: null } as any)
      const s = useUserStore()
      await s.fetchCurrentUser()
      expect(s.currentUser).toBeNull()
    })

    it('sets error and rethrows on failure', async () => {
      profileApiMock.get.mockRejectedValueOnce(new Error('boom'))
      const s = useUserStore()
      await expect(s.fetchCurrentUser()).rejects.toThrow('boom')
      expect(s.error).toBe('boom')
      expect(s.isLoading).toBe(false)
    })

    it('non-Error rejection → generic message', async () => {
      profileApiMock.get.mockRejectedValueOnce('stringy')
      const s = useUserStore()
      await expect(s.fetchCurrentUser()).rejects.toBe('stringy')
      expect(s.error).toBe('Failed to fetch user')
    })
  })

  describe('updateProfile', () => {
    it('throws when not authenticated', async () => {
      const s = useUserStore()
      await expect(s.updateProfile({ nickname: 'x' } as any)).rejects.toThrow('Not authenticated')
    })

    it('updates currentUser and returns data on success', async () => {
      const s = useUserStore()
      s.currentUser = { id: 'u1', userName: 'alice' } as any
      const result = await s.updateProfile({ nickname: 'Ally' } as any)
      expect(profileApiMock.update).toHaveBeenCalledWith({ nickname: 'Ally' })
      expect(result.nickname).toBe('Ally')
      expect(s.currentUser?.nickname).toBe('Ally')
      expect(s.isLoading).toBe(false)
    })

    it('throws generic Update failed when result.succeeded is false', async () => {
      profileApiMock.update.mockResolvedValueOnce({ succeeded: false, data: null } as any)
      const s = useUserStore()
      s.currentUser = { id: 'u1', userName: 'alice' } as any
      await expect(s.updateProfile({} as any)).rejects.toThrow('Update failed')
      expect(s.error).toBe('Update failed')
    })

    it('sets error from thrown Error', async () => {
      profileApiMock.update.mockRejectedValueOnce(new Error('bad'))
      const s = useUserStore()
      s.currentUser = { id: 'u1', userName: 'alice' } as any
      await expect(s.updateProfile({} as any)).rejects.toThrow('bad')
      expect(s.error).toBe('bad')
    })
  })

  describe('preferences', () => {
    it('updatePreferences merges partial and persists', async () => {
      const s = useUserStore()
      await s.updatePreferences({ theme: 'dark' as any })
      expect(s.preferences.theme).toBe('dark')
      expect(applyThemeSpy).toHaveBeenCalledWith('dark')
      expect(storageMock.set).toHaveBeenCalled()
    })

    it('updatePreferences with language triggers applyLanguage', async () => {
      const s = useUserStore()
      await s.updatePreferences({ language: 'zh-cn' as any })
      expect(s.preferences.language).toBe('zh-cn')
      expect(applyLanguageSpy).toHaveBeenCalledWith('zh-cn')
    })

    it('updatePreferences without theme/language does not re-apply DOM', async () => {
      const s = useUserStore()
      await s.updatePreferences({ fontSize: 16 } as any)
      expect(applyThemeSpy).not.toHaveBeenCalled()
      expect(applyLanguageSpy).not.toHaveBeenCalled()
      expect(s.preferences.fontSize).toBe(16)
    })

    it('resetPreferences reverts to defaults and re-applies DOM', () => {
      const s = useUserStore()
      s.preferences = { ...s.preferences, theme: 'dark' as any, language: 'fr' as any }
      s.resetPreferences()
      expect(s.preferences.theme).toBe('light')
      expect(s.preferences.language).toBe('en-us')
      expect(applyThemeSpy).toHaveBeenCalledWith('light')
      expect(applyLanguageSpy).toHaveBeenCalledWith('en-us')
    })

    it('setTheme updates, applies, and persists', () => {
      const s = useUserStore()
      s.setTheme('dark' as any)
      expect(s.preferences.theme).toBe('dark')
      expect(applyThemeSpy).toHaveBeenCalledWith('dark')
      expect(storageMock.set).toHaveBeenCalled()
    })

    it('setLanguage updates, applies, and persists', () => {
      const s = useUserStore()
      s.setLanguage('zh-cn')
      expect(s.preferences.language).toBe('zh-cn')
      expect(applyLanguageSpy).toHaveBeenCalledWith('zh-cn')
    })
  })

  describe('recent items', () => {
    it('addRecentItem prepends and removes duplicate by id', () => {
      const s = useUserStore()
      s.addRecentItem({ id: '1', title: 'A', route: '/a' } as any)
      s.addRecentItem({ id: '2', title: 'B', route: '/b' } as any)
      s.addRecentItem({ id: '1', title: 'A2', route: '/a' } as any)
      expect(s.recentItems[0].id).toBe('1')
      expect(s.recentItems[0].title).toBe('A2')
      expect(s.recentItems.length).toBe(2)
    })

    it('addRecentItem trims past max (20)', () => {
      const s = useUserStore()
      for (let i = 0; i < 25; i++) {
        s.addRecentItem({ id: `${i}`, title: `T${i}`, route: `/${i}` } as any)
      }
      expect(s.recentItems.length).toBe(20)
      expect(s.recentItems[0].id).toBe('24')
    })

    it('removeRecentItem and clearRecentItems', () => {
      const s = useUserStore()
      s.addRecentItem({ id: '1', title: 'A', route: '/a' } as any)
      s.addRecentItem({ id: '2', title: 'B', route: '/b' } as any)
      s.removeRecentItem('1')
      expect(s.recentItems.find((i) => i.id === '1')).toBeUndefined()
      s.clearRecentItems()
      expect(s.recentItems).toEqual([])
    })
  })

  describe('favorites', () => {
    it('addFavorite skips duplicates and returns silently', () => {
      const s = useUserStore()
      s.addFavorite({ id: '1', title: 'A', route: '/a' } as any)
      s.addFavorite({ id: '1', title: 'A-dup', route: '/a' } as any)
      expect(s.favorites.length).toBe(1)
      expect(s.favorites[0].title).toBe('A')
    })

    it('addFavorite trims past max (10)', () => {
      const s = useUserStore()
      for (let i = 0; i < 15; i++) {
        s.addFavorite({ id: `${i}`, title: `T${i}`, route: `/${i}` } as any)
      }
      expect(s.favorites.length).toBe(10)
      expect(s.favorites[0].id).toBe('14')
    })

    it('removeFavorite and isFavorite', () => {
      const s = useUserStore()
      s.addFavorite({ id: '1', title: 'A', route: '/a' } as any)
      expect(s.isFavorite('1')).toBe(true)
      expect(s.isFavorite('2')).toBe(false)
      s.removeFavorite('1')
      expect(s.isFavorite('1')).toBe(false)
    })
  })

  describe('persistence', () => {
    it('persistData writes storage when storage exists', () => {
      const s = useUserStore()
      s.addRecentItem({ id: '1', title: 'A', route: '/a' } as any)
      expect(storageBag['user_data']).toBeDefined()
    })

    it('loadPersistedData restores preferences/recentItems/favorites and applies DOM', async () => {
      storageBag['user_data'] = {
        preferences: { theme: 'dark' as any, language: 'zh-cn' as any, density: 'default', fontSize: 14 },
        recentItems: [{ id: 'r1', title: 'R', route: '/r', accessedAt: new Date() }],
        favorites: [{ id: 'f1', title: 'F', route: '/f', accessedAt: new Date() }],
      }
      const s = useUserStore()
      await s.loadPersistedData()
      expect(s.preferences.theme).toBe('dark')
      expect(s.preferences.language).toBe('zh-cn')
      expect(s.recentItems).toHaveLength(1)
      expect(s.favorites).toHaveLength(1)
      expect(applyThemeSpy).toHaveBeenCalledWith('dark')
      expect(applyLanguageSpy).toHaveBeenCalledWith('zh-cn')
    })

    it('loadPersistedData is a noop when storage has no entry', async () => {
      const s = useUserStore()
      await s.loadPersistedData()
      expect(s.recentItems).toEqual([])
      expect(s.favorites).toEqual([])
      // applyTheme/applyLanguage still called with default
      expect(applyThemeSpy).toHaveBeenCalledWith('light')
    })

    it('loadPersistedData handles partial payload (only preferences)', async () => {
      storageBag['user_data'] = { preferences: { theme: 'dark' as any, language: 'en-us', density: 'default', fontSize: 14 } }
      const s = useUserStore()
      await s.loadPersistedData()
      expect(s.preferences.theme).toBe('dark')
      expect(s.recentItems).toEqual([])
    })
  })

  describe('useUser composable', () => {
    it('exposes computed refs and bound actions', async () => {
      const u = useUser()
      expect(u.currentUser.value).toBeNull()
      expect(u.isAuthenticated.value).toBe(false)
      expect(u.theme.value).toBe('light')
      expect(u.language.value).toBe('en-us')
      u.setTheme('dark' as any)
      expect(u.theme.value).toBe('dark')
      u.addRecentItem({ id: '1', title: 'A', route: '/a' } as any)
      expect(u.recentItemsCount.value).toBe(1)
      u.addFavorite({ id: '2', title: 'B', route: '/b' } as any)
      expect(u.favoritesCount.value).toBe(1)
      expect(u.isFavorite('2')).toBe(true)
      u.clearRecentItems()
      expect(u.recentItemsCount.value).toBe(0)
    })

    it('composable fetchCurrentUser delegates to store', async () => {
      const u = useUser()
      await u.fetchCurrentUser()
      expect(profileApiMock.get).toHaveBeenCalled()
      expect(u.currentUser.value?.userName).toBe('alice')
    })
  })
})
