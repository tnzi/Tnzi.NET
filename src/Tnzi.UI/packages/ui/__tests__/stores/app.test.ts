import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

const storageBag: Record<string, unknown> = {}
const storageMock = {
  get: vi.fn((k: string) => storageBag[k] ?? null),
  set: vi.fn((k: string, v: unknown) => { storageBag[k] = v }),
  remove: vi.fn((k: string) => { delete storageBag[k] }),
}

vi.mock('../../src/stores/factory', () => ({
  getStoreStorage: () => storageMock,
  getStoreHttpClient: () => ({ get: vi.fn(), post: vi.fn() }),
}))

const applyThemeSpy = vi.fn()
const applyLanguageSpy = vi.fn()
vi.mock('../../src/utils/naive-helpers', () => ({
  applyThemeToDOM: (t: string) => applyThemeSpy(t),
  applyLanguageToDOM: (l: string) => applyLanguageSpy(l),
}))

import { useAppStore, useApp } from '../../src/stores/app'

describe('stores/app', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    Object.keys(storageBag).forEach((k) => delete storageBag[k])
    storageMock.get.mockClear()
    storageMock.set.mockClear()
    applyThemeSpy.mockClear()
    applyLanguageSpy.mockClear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('initial state', () => {
    it('defaults are sane', () => {
      const s = useAppStore()
      expect(s.theme).toBe('light')
      expect(s.language).toBe('zh-CN')
      expect(s.ui.sidebarCollapsed).toBe(false)
      expect(s.ui.sidebarMode).toBe('responsive')
      expect(s.ui.loading).toBe(false)
      expect(s.ui.modalStack).toEqual([])
      expect(s.ui.toasts).toEqual([])
      expect(s.onlineStatus).toBe('online')
      expect(s.initialized).toBe(false)
    })
  })

  describe('getters', () => {
    it('isDarkMode is true when theme is dark', () => {
      const s = useAppStore()
      s.theme = 'dark'
      expect(s.isDarkMode).toBe(true)
    })

    it('isDarkMode uses matchMedia when theme is system (prefers dark)', () => {
      const originalMatchMedia = window.matchMedia
      window.matchMedia = vi.fn().mockReturnValue({ matches: true }) as any
      const s = useAppStore()
      s.theme = 'system'
      expect(s.isDarkMode).toBe(true)
      window.matchMedia = originalMatchMedia
    })

    it('isDarkMode is false when theme is system and prefers light', () => {
      const originalMatchMedia = window.matchMedia
      window.matchMedia = vi.fn().mockReturnValue({ matches: false }) as any
      setActivePinia(createPinia())
      const s = useAppStore()
      s.theme = 'system'
      expect(s.isDarkMode).toBe(false)
      window.matchMedia = originalMatchMedia
    })

    it('hasOpenModal / topModalId / modalCount reflect modal stack', () => {
      const s = useAppStore()
      expect(s.hasOpenModal).toBe(false)
      expect(s.topModalId).toBeNull()
      expect(s.modalCount).toBe(0)
      s.openModal({ type: 'confirm' } as any)
      s.openModal({ type: 'info' } as any)
      expect(s.hasOpenModal).toBe(true)
      expect(s.modalCount).toBe(2)
      expect(s.topModalId).toBeTruthy()
    })

    it('unread/notifications counts reflect toast list', () => {
      const s = useAppStore()
      s.addNotification({ type: 'info', severity: 'low', message: 'a', duration: 0 } as any)
      s.addNotification({ type: 'info', severity: 'low', message: 'b', duration: 0 } as any)
      expect(s.notificationsCount).toBe(2)
      expect(s.unreadNotificationsCount).toBe(2)
      const id = s.ui.toasts[0]!.id
      s.markNotificationRead(id)
      expect(s.unreadNotificationsCount).toBe(1)
    })
  })

  describe('theme actions', () => {
    it('setTheme updates state, calls apply, persists', () => {
      const s = useAppStore()
      s.setTheme('dark')
      expect(s.theme).toBe('dark')
      expect(applyThemeSpy).toHaveBeenCalledWith('dark')
      expect(storageMock.set).toHaveBeenCalled()
    })

    it('toggleTheme cycles through light → dark → system → light', () => {
      const s = useAppStore()
      s.theme = 'light'
      s.toggleTheme(); expect(s.theme).toBe('dark')
      s.toggleTheme(); expect(s.theme).toBe('system')
      s.toggleTheme(); expect(s.theme).toBe('light')
    })
  })

  describe('language actions', () => {
    it('setLanguage updates state and applies DOM', () => {
      const s = useAppStore()
      s.setLanguage('en-US' as any)
      expect(s.language).toBe('en-US')
      expect(applyLanguageSpy).toHaveBeenCalledWith('en-US')
    })
  })

  describe('sidebar actions', () => {
    it('toggleSidebar flips collapsed', () => {
      const s = useAppStore()
      s.toggleSidebar(); expect(s.ui.sidebarCollapsed).toBe(true)
      s.toggleSidebar(); expect(s.ui.sidebarCollapsed).toBe(false)
    })

    it('setSidebarCollapsed forces value', () => {
      const s = useAppStore()
      s.setSidebarCollapsed(true)
      expect(s.ui.sidebarCollapsed).toBe(true)
    })

    it('setSidebarMode updates', () => {
      const s = useAppStore()
      s.setSidebarMode('desktop' as any)
      expect(s.ui.sidebarMode).toBe('desktop')
    })
  })

  describe('fullscreen actions', () => {
    it('toggleFullscreen calls requestFullscreen when not fullscreen', () => {
      const requestSpy = vi.fn()
      const exitSpy = vi.fn()
      Object.defineProperty(document.documentElement, 'requestFullscreen', { value: requestSpy, configurable: true })
      Object.defineProperty(document, 'exitFullscreen', { value: exitSpy, configurable: true })
      const s = useAppStore()
      s.ui.fullscreen = false
      s.toggleFullscreen()
      expect(requestSpy).toHaveBeenCalled()
      s.ui.fullscreen = true
      s.toggleFullscreen()
      expect(exitSpy).toHaveBeenCalled()
    })

    it('_initFullscreenListener is idempotent and syncs state on fullscreenchange', () => {
      const s = useAppStore()
      s._initFullscreenListener()
      s._initFullscreenListener()  // idempotent
      Object.defineProperty(document, 'fullscreenElement', { value: document.documentElement, configurable: true })
      document.dispatchEvent(new Event('fullscreenchange'))
      expect(s.ui.fullscreen).toBe(true)
      Object.defineProperty(document, 'fullscreenElement', { value: null, configurable: true })
      document.dispatchEvent(new Event('fullscreenchange'))
      expect(s.ui.fullscreen).toBe(false)
    })
  })

  describe('loading actions', () => {
    it('showLoading / hideLoading', () => {
      const s = useAppStore()
      s.showLoading('loading...')
      expect(s.ui.loading).toBe(true)
      expect(s.ui.loadingMessage).toBe('loading...')
      s.hideLoading()
      expect(s.ui.loading).toBe(false)
      expect(s.ui.loadingMessage).toBeNull()
    })

    it('showLoading without message defaults to null', () => {
      const s = useAppStore()
      s.showLoading()
      expect(s.ui.loadingMessage).toBeNull()
    })

    it('setLoading with explicit args', () => {
      const s = useAppStore()
      s.setLoading(true, 'wait')
      expect(s.ui.loading).toBe(true)
      expect(s.ui.loadingMessage).toBe('wait')
      s.setLoading(false)
      expect(s.ui.loading).toBe(false)
      expect(s.ui.loadingMessage).toBeNull()
    })
  })

  describe('notification actions', () => {
    it('addNotification returns id and prepends to toasts', () => {
      const s = useAppStore()
      const id = s.addNotification({ type: 'info', severity: 'low', message: 'hello', duration: 0 } as any)
      expect(typeof id).toBe('string')
      expect(s.ui.toasts[0]!.id).toBe(id)
      expect(s.ui.toasts[0]!.read).toBe(false)
    })

    it('addNotification auto-dismisses after duration', () => {
      vi.useFakeTimers()
      const s = useAppStore()
      s.addNotification({ type: 'info', severity: 'low', message: 'tmp', duration: 100 } as any)
      expect(s.ui.toasts.length).toBe(1)
      vi.advanceTimersByTime(200)
      expect(s.ui.toasts.length).toBe(0)
    })

    it('showSuccess/showError/showWarning/showInfo set correct type+severity', () => {
      const s = useAppStore()
      s.showSuccess('ok')
      expect(s.ui.toasts[0]!.type).toBe('success')
      s.clearNotifications()
      s.showError('bad')
      expect(s.ui.toasts[0]!.type).toBe('error')
      expect(s.ui.toasts[0]!.title).toBe('Error')
      s.clearNotifications()
      s.showWarning('hm')
      expect(s.ui.toasts[0]!.type).toBe('warning')
      s.clearNotifications()
      s.showInfo('fyi')
      expect(s.ui.toasts[0]!.type).toBe('info')
    })

    it('removeNotification calls onDismiss and removes entry', () => {
      const s = useAppStore()
      const onDismiss = vi.fn()
      const id = s.addNotification({ type: 'info', severity: 'low', message: 'x', duration: 0, onDismiss } as any)
      s.removeNotification(id)
      expect(onDismiss).toHaveBeenCalled()
      expect(s.ui.toasts).toEqual([])
    })

    it('removeNotification on missing id is a noop', () => {
      const s = useAppStore()
      expect(() => s.removeNotification('nope')).not.toThrow()
    })

    it('markAllNotificationsRead flips every toast', () => {
      const s = useAppStore()
      s.addNotification({ type: 'info', severity: 'low', message: 'a', duration: 0 } as any)
      s.addNotification({ type: 'info', severity: 'low', message: 'b', duration: 0 } as any)
      s.markAllNotificationsRead()
      expect(s.ui.toasts.every((t) => t.read)).toBe(true)
    })

    it('clearNotifications empties the list', () => {
      const s = useAppStore()
      s.addNotification({ type: 'info', severity: 'low', message: 'a', duration: 0 } as any)
      s.clearNotifications()
      expect(s.ui.toasts).toEqual([])
    })
  })

  describe('modal actions', () => {
    it('openModal / closeModal by id', () => {
      const s = useAppStore()
      const id = s.openModal({ type: 'confirm', payload: {} } as any)
      expect(s.ui.modalStack.length).toBe(1)
      s.closeModal(id)
      expect(s.ui.modalStack.length).toBe(0)
    })

    it('closeModal on missing id is noop', () => {
      const s = useAppStore()
      expect(() => s.closeModal('nope')).not.toThrow()
    })

    it('closeTopModal removes the top modal', () => {
      const s = useAppStore()
      s.openModal({ type: 'a' } as any)
      s.openModal({ type: 'b' } as any)
      s.closeTopModal()
      expect(s.ui.modalStack.length).toBe(1)
      expect(s.ui.modalStack[0]!.type).toBe('a')
    })

    it('closeTopModal on empty stack is noop', () => {
      const s = useAppStore()
      expect(() => s.closeTopModal()).not.toThrow()
    })

    it('closeAllModals empties stack', () => {
      const s = useAppStore()
      s.openModal({ type: 'a' } as any)
      s.openModal({ type: 'b' } as any)
      s.closeAllModals()
      expect(s.ui.modalStack).toEqual([])
    })
  })

  describe('online/connection/initialization', () => {
    it('setOnlineStatus updates', () => {
      const s = useAppStore()
      s.setOnlineStatus('offline' as any)
      expect(s.onlineStatus).toBe('offline')
    })

    it('updateConnection merges partial state', () => {
      const s = useAppStore()
      s.updateConnection({ webSocketConnected: true })
      expect(s.connection.webSocketConnected).toBe(true)
      expect(s.connection.isConnected).toBe(true)
    })

    it('setConnected(true) sets lastConnectedAt and clears error', () => {
      const s = useAppStore()
      s.connection.error = 'old'
      s.setConnected(true)
      expect(s.connection.isConnected).toBe(true)
      expect(s.connection.lastConnectedAt).toBeInstanceOf(Date)
      expect(s.connection.error).toBeNull()
    })

    it('setConnected(false) does not touch lastConnectedAt', () => {
      const s = useAppStore()
      s.setConnected(true)
      const prev = s.connection.lastConnectedAt
      s.setConnected(false)
      expect(s.connection.isConnected).toBe(false)
      expect(s.connection.lastConnectedAt).toBe(prev)
    })

    it('setInitialized / setInitError', () => {
      const s = useAppStore()
      s.setInitError('bad')
      expect(s.initError).toBe('bad')
      expect(s.initialized).toBe(false)
      s.setInitialized()
      expect(s.initialized).toBe(true)
      expect(s.initError).toBeNull()
      s.setInitError(null)
      expect(s.initialized).toBe(true)
    })
  })

  describe('persistence', () => {
    it('persistState writes storage snapshot', () => {
      const s = useAppStore()
      s.theme = 'dark'
      s.persistState()
      expect(storageBag['app_state']).toEqual(expect.objectContaining({ theme: 'dark' }))
    })

    it('loadPersistedState restores fields and applies DOM', async () => {
      storageBag['app_state'] = { theme: 'dark', language: 'en-US', sidebarCollapsed: true, sidebarMode: 'desktop' }
      const s = useAppStore()
      await s.loadPersistedState()
      expect(s.theme).toBe('dark')
      expect(s.language).toBe('en-US')
      expect(s.ui.sidebarCollapsed).toBe(true)
      expect(s.ui.sidebarMode).toBe('desktop')
      expect(applyThemeSpy).toHaveBeenCalledWith('dark')
      expect(applyLanguageSpy).toHaveBeenCalledWith('en-US')
    })

    it('loadPersistedState with no data still applies current theme/language', async () => {
      const s = useAppStore()
      await s.loadPersistedState()
      expect(applyThemeSpy).toHaveBeenCalledWith('light')
      expect(applyLanguageSpy).toHaveBeenCalledWith('zh-CN')
    })

    it('loadPersistedState handles partial data with sidebarCollapsed=false', async () => {
      storageBag['app_state'] = { sidebarCollapsed: false }
      const s = useAppStore()
      s.ui.sidebarCollapsed = true
      await s.loadPersistedState()
      expect(s.ui.sidebarCollapsed).toBe(false)
    })
  })

  describe('useApp composable', () => {
    it('exposes computed state and bound actions', () => {
      const a = useApp()
      expect(a.theme.value).toBe('light')
      expect(a.language.value).toBe('zh-CN')
      expect(a.isLoading.value).toBe(false)
      expect(a.hasOpenModal.value).toBe(false)
      expect(a.notifications.value).toEqual([])
      a.showInfo('hi')
      expect(a.notificationsCount.value).toBe(1)
      a.setTheme('dark')
      expect(a.theme.value).toBe('dark')
      a.toggleTheme()
      expect(a.theme.value).toBe('system')
      a.openModal({ type: 't' } as any)
      expect(a.modalCount.value).toBe(1)
      a.closeAllModals()
      expect(a.hasOpenModal.value).toBe(false)
      a.setOnlineStatus('offline' as any)
      expect(a.onlineStatus.value).toBe('offline')
      a.setConnected(false)
      expect(a.isConnected.value).toBe(false)
      a.setInitialized()
      expect(a.initialized.value).toBe(true)
    })
  })
})
