import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'

// Mock AppStateManager so tests exercise store delegation without touching real DOM/storage.
// The store is a thin proxy: state is read through computed(() => manager.xxx),
// actions forward to manager methods.
const managerInstance = {
  // --- State (mutable per test) ---
  ui: {
    sidebarCollapsed: false,
    sidebarMode: 'responsive',
    headerVisible: true,
    footerVisible: true,
    fullscreen: false,
    loading: false,
    loadingMessage: null as string | null,
    modalStack: [] as any[],
    toasts: [] as any[],
  },
  theme: 'light',
  language: 'zh-CN',
  onlineStatus: 'online',
  connection: { isConnected: true, webSocketConnected: false, lastConnectedAt: null, error: null },
  initialized: false,
  initError: null as string | null,
  // --- Getters ---
  isDarkMode: false,
  sidebarCollapsed: false,
  sidebarMode: 'responsive',
  isFullscreen: false,
  isLoading: false,
  loadingMessage: null as string | null,
  unreadNotificationsCount: 0,
  notificationsCount: 0,
  hasOpenModal: false,
  topModalId: null as string | null,
  modalCount: 0,
  isConnected: true,
  // --- Methods ---
  setTheme: vi.fn(),
  toggleTheme: vi.fn(),
  setLanguage: vi.fn(),
  toggleSidebar: vi.fn(),
  setSidebarCollapsed: vi.fn(),
  setSidebarMode: vi.fn(),
  toggleFullscreen: vi.fn(),
  showLoading: vi.fn(),
  hideLoading: vi.fn(),
  setLoading: vi.fn(),
  addNotification: vi.fn(() => 'n1'),
  showSuccess: vi.fn(() => 's1'),
  showError: vi.fn(() => 'e1'),
  showWarning: vi.fn(() => 'w1'),
  showInfo: vi.fn(() => 'i1'),
  removeNotification: vi.fn(),
  markNotificationRead: vi.fn(),
  markAllNotificationsRead: vi.fn(),
  clearNotifications: vi.fn(),
  openModal: vi.fn(() => 'm1'),
  closeModal: vi.fn(),
  closeTopModal: vi.fn(),
  closeAllModals: vi.fn(),
  setOnlineStatus: vi.fn(),
  updateConnection: vi.fn(),
  setConnected: vi.fn(),
  setInitialized: vi.fn(),
  setInitError: vi.fn(),
  loadPersistedState: vi.fn(),
  persistState: vi.fn(),
  dispose: vi.fn(),
}

vi.mock('@tnzi/core/state', () => ({
  AppStateManager: vi.fn(() => managerInstance),
}))

vi.mock('@tnzi/core/adapters/storage', () => ({
  createLocalStorageAdapter: () => ({ getItem: vi.fn(), setItem: vi.fn(), removeItem: vi.fn() }),
}))

vi.mock('../../src/adapters/theme', () => ({
  createThemeAdapter: () => ({ applyTheme: vi.fn(), getResolvedTheme: () => 'light' }),
}))

vi.mock('../../src/stores/factory', () => ({
  getStoreHttpClient: () => ({ get: vi.fn(), post: vi.fn() }),
  getStoreStorage: () => null,
}))

import { useAppStore, useApp, resetAppRuntime } from '../../src/stores/app'
import { AppStateManager as MockedAppStateManager } from '@tnzi/core/state'

describe('stores/app', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    resetAppRuntime()
    Object.values(managerInstance).forEach((v) => {
      if (typeof v === 'function' && 'mockClear' in v) (v as ReturnType<typeof vi.fn>).mockClear()
    })
    // Reset mutable manager state
    managerInstance.theme = 'light'
    managerInstance.language = 'zh-CN'
    managerInstance.onlineStatus = 'online'
    managerInstance.initialized = false
    managerInstance.initError = null
    managerInstance.isDarkMode = false
    managerInstance.sidebarCollapsed = false
    managerInstance.sidebarMode = 'responsive'
    managerInstance.isFullscreen = false
    managerInstance.isLoading = false
    managerInstance.loadingMessage = null
    managerInstance.unreadNotificationsCount = 0
    managerInstance.notificationsCount = 0
    managerInstance.hasOpenModal = false
    managerInstance.topModalId = null
    managerInstance.modalCount = 0
    managerInstance.isConnected = true
    managerInstance.ui.toasts = []
    managerInstance.ui.modalStack = []
    managerInstance.ui.sidebarCollapsed = false
  })

  afterEach(() => {
    resetAppRuntime()
  })

  describe('useAppStore — reactive state proxies', () => {
    it('exposes theme/language/onlineStatus/initialized/initError', () => {
      managerInstance.theme = 'dark'
      managerInstance.language = 'en-US'
      managerInstance.onlineStatus = 'offline'
      managerInstance.initialized = true
      managerInstance.initError = 'err'
      const s = useAppStore()
      expect(s.theme).toBe('dark')
      expect(s.language).toBe('en-US')
      expect(s.onlineStatus).toBe('offline')
      expect(s.initialized).toBe(true)
      expect(s.initError).toBe('err')
    })

    it('exposes ui + connection objects', () => {
      managerInstance.ui.toasts = [{ id: 't1' }] as any
      const s = useAppStore()
      expect(s.ui.toasts).toEqual([{ id: 't1' }])
      expect(s.connection.isConnected).toBe(true)
    })
  })

  describe('useAppStore — getters', () => {
    it('proxies isDarkMode/sidebar/fullscreen/loading/notification/modal/connection getters', () => {
      managerInstance.isDarkMode = true
      managerInstance.sidebarCollapsed = true
      managerInstance.sidebarMode = 'mini'
      managerInstance.isFullscreen = true
      managerInstance.isLoading = true
      managerInstance.loadingMessage = 'wait'
      managerInstance.unreadNotificationsCount = 2
      managerInstance.notificationsCount = 3
      managerInstance.hasOpenModal = true
      managerInstance.topModalId = 'm9'
      managerInstance.modalCount = 1
      managerInstance.isConnected = false
      const s = useAppStore()
      expect(s.isDarkMode).toBe(true)
      expect(s.currentTheme).toBe('light')
      expect(s.currentLanguage).toBe('zh-CN')
      expect(s.sidebarCollapsed).toBe(true)
      expect(s.sidebarMode).toBe('mini')
      expect(s.isFullscreen).toBe(true)
      expect(s.isLoading).toBe(true)
      expect(s.loadingMessage).toBe('wait')
      expect(s.unreadNotificationsCount).toBe(2)
      expect(s.notificationsCount).toBe(3)
      expect(s.hasOpenModal).toBe(true)
      expect(s.topModalId).toBe('m9')
      expect(s.modalCount).toBe(1)
      expect(s.isConnected).toBe(false)
      expect(s.currentOnlineStatus).toBe('online')
      expect(s.isInitialized).toBe(false)
      expect(s.currentInitError).toBeNull()
    })
  })

  describe('useAppStore — theme/language actions', () => {
    it('setTheme/toggleTheme/setLanguage delegate', () => {
      const s = useAppStore()
      s.setTheme('dark')
      expect(managerInstance.setTheme).toHaveBeenCalledWith('dark')
      s.toggleTheme()
      expect(managerInstance.toggleTheme).toHaveBeenCalled()
      s.setLanguage('en-US' as any)
      expect(managerInstance.setLanguage).toHaveBeenCalledWith('en-US')
    })
  })

  describe('useAppStore — sidebar/fullscreen/loading actions', () => {
    it('sidebar actions delegate', () => {
      const s = useAppStore()
      s.toggleSidebar()
      expect(managerInstance.toggleSidebar).toHaveBeenCalled()
      s.setSidebarCollapsed(true)
      expect(managerInstance.setSidebarCollapsed).toHaveBeenCalledWith(true)
      s.setSidebarMode('mini' as any)
      expect(managerInstance.setSidebarMode).toHaveBeenCalledWith('mini')
    })

    it('toggleFullscreen delegates', () => {
      const s = useAppStore()
      s.toggleFullscreen()
      expect(managerInstance.toggleFullscreen).toHaveBeenCalled()
    })

    it('loading actions delegate', () => {
      const s = useAppStore()
      s.showLoading('x')
      expect(managerInstance.showLoading).toHaveBeenCalledWith('x')
      s.hideLoading()
      expect(managerInstance.hideLoading).toHaveBeenCalled()
      s.setLoading(true, 'y')
      expect(managerInstance.setLoading).toHaveBeenCalledWith(true, 'y')
    })
  })

  describe('useAppStore — notification actions', () => {
    it('delegate and return ids from manager', () => {
      const s = useAppStore()
      expect(s.addNotification({ type: 'info', severity: 'low', message: 'a', duration: 0 } as any)).toBe('n1')
      expect(s.showSuccess('ok')).toBe('s1')
      expect(s.showError('bad')).toBe('e1')
      expect(s.showWarning('hm')).toBe('w1')
      expect(s.showInfo('fyi')).toBe('i1')
      s.removeNotification('n1')
      expect(managerInstance.removeNotification).toHaveBeenCalledWith('n1')
      s.markNotificationRead('n1')
      expect(managerInstance.markNotificationRead).toHaveBeenCalledWith('n1')
      s.markAllNotificationsRead()
      expect(managerInstance.markAllNotificationsRead).toHaveBeenCalled()
      s.clearNotifications()
      expect(managerInstance.clearNotifications).toHaveBeenCalled()
    })
  })

  describe('useAppStore — modal actions', () => {
    it('delegate and return id', () => {
      const s = useAppStore()
      expect(s.openModal({ component: 'C' } as any)).toBe('m1')
      s.closeModal('m1')
      expect(managerInstance.closeModal).toHaveBeenCalledWith('m1')
      s.closeTopModal()
      expect(managerInstance.closeTopModal).toHaveBeenCalled()
      s.closeAllModals()
      expect(managerInstance.closeAllModals).toHaveBeenCalled()
    })
  })

  describe('useAppStore — online/connection/init actions', () => {
    it('delegate', () => {
      const s = useAppStore()
      s.setOnlineStatus('offline' as any)
      expect(managerInstance.setOnlineStatus).toHaveBeenCalledWith('offline')
      s.updateConnection({ webSocketConnected: true })
      expect(managerInstance.updateConnection).toHaveBeenCalledWith({ webSocketConnected: true })
      s.setConnected(false)
      expect(managerInstance.setConnected).toHaveBeenCalledWith(false)
      s.setInitialized()
      expect(managerInstance.setInitialized).toHaveBeenCalled()
      s.setInitError('bad')
      expect(managerInstance.setInitError).toHaveBeenCalledWith('bad')
    })
  })

  describe('useAppStore — persistence actions', () => {
    it('delegate', async () => {
      const s = useAppStore()
      await s.loadPersistedState()
      expect(managerInstance.loadPersistedState).toHaveBeenCalled()
      s.persistState()
      expect(managerInstance.persistState).toHaveBeenCalled()
    })
  })

  describe('useApp composable', () => {
    it('exposes computed state and bound actions', () => {
      managerInstance.theme = 'dark'
      managerInstance.notificationsCount = 1
      managerInstance.ui.toasts = [{ id: 't1' }] as any
      const a = useApp()
      expect(a.theme.value).toBe('dark')
      expect(a.language.value).toBe('zh-CN')
      expect(a.isLoading.value).toBe(false)
      expect(a.hasOpenModal.value).toBe(false)
      expect(a.notifications.value).toEqual([{ id: 't1' }])
      expect(a.notificationsCount.value).toBe(1)
      a.showInfo('hi')
      expect(managerInstance.showInfo).toHaveBeenCalledWith('hi', undefined)
      a.setTheme('light')
      expect(managerInstance.setTheme).toHaveBeenCalledWith('light')
      a.toggleTheme()
      expect(managerInstance.toggleTheme).toHaveBeenCalled()
      a.openModal({ component: 't' } as any)
      expect(managerInstance.openModal).toHaveBeenCalled()
      a.closeAllModals()
      expect(managerInstance.closeAllModals).toHaveBeenCalled()
      a.setOnlineStatus('offline' as any)
      expect(managerInstance.setOnlineStatus).toHaveBeenCalledWith('offline')
      a.setConnected(false)
      expect(managerInstance.setConnected).toHaveBeenCalledWith(false)
      a.setInitialized()
      expect(managerInstance.setInitialized).toHaveBeenCalled()
    })
  })

  describe('resetAppRuntime', () => {
    it('disposes the manager and creates a fresh instance on next access', () => {
      const ctor = vi.mocked(MockedAppStateManager)
      useAppStore().toggleTheme()
      const callsBefore = ctor.mock.calls.length
      resetAppRuntime()
      expect(managerInstance.dispose).toHaveBeenCalled()
      useAppStore().toggleTheme()
      const callsAfter = ctor.mock.calls.length
      expect(callsAfter).toBeGreaterThan(callsBefore)
    })
  })
})
