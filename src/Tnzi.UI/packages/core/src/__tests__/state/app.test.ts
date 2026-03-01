import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { AppStateManager, createInitialAppState } from '../../state/app';
import type { StateDeps } from '../../state/types';
import type { HttpClient } from '../../http/http';
import type { StorageAdapter } from '../../adapters/storage';

function createMockStorage(): StorageAdapter {
  const store = new Map<string, unknown>();
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
  };
}

function createMockHttpClient(): HttpClient {
  return {
    setAccessToken: vi.fn(),
    getAccessToken: vi.fn().mockReturnValue(null),
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
    upload: vi.fn(),
    uploadFormData: vi.fn(),
    download: vi.fn(),
    resolveUrl: vi.fn(),
  } as unknown as HttpClient;
}

function createDeps(overrides?: Partial<StateDeps>): StateDeps {
  return {
    httpClient: createMockHttpClient(),
    storage: createMockStorage(),
    ...overrides,
  };
}

describe('AppStateManager', () => {
  let app: AppStateManager;
  let deps: StateDeps;

  beforeEach(() => {
    deps = createDeps();
    app = new AppStateManager(deps);
  });

  afterEach(() => {
    app.dispose();
  });

  // ------------------------------------------
  // Initial state
  // ------------------------------------------

  describe('initial state', () => {
    it('should start with light theme', () => {
      expect(app.theme).toBe('light');
    });

    it('should start with zh-CN language', () => {
      expect(app.language).toBe('zh-CN');
    });

    it('should start as online', () => {
      expect(app.onlineStatus).toBe('online');
    });

    it('should start as connected', () => {
      expect(app.isConnected).toBe(true);
    });

    it('should not be initialized', () => {
      expect(app.initialized).toBe(false);
    });

    it('should have no init error', () => {
      expect(app.initError).toBeNull();
    });

    it('should not be loading', () => {
      expect(app.isLoading).toBe(false);
    });

    it('should not be fullscreen', () => {
      expect(app.isFullscreen).toBe(false);
    });

    it('should have sidebar expanded', () => {
      expect(app.sidebarCollapsed).toBe(false);
    });

    it('should have no notifications', () => {
      expect(app.notificationsCount).toBe(0);
      expect(app.unreadNotificationsCount).toBe(0);
    });

    it('should have no modals', () => {
      expect(app.hasOpenModal).toBe(false);
      expect(app.topModalId).toBeNull();
      expect(app.modalCount).toBe(0);
    });
  });

  // ------------------------------------------
  // Theme
  // ------------------------------------------

  describe('theme', () => {
    it('setTheme should update theme', () => {
      app.setTheme('dark');
      expect(app.theme).toBe('dark');
    });

    it('setTheme should persist to storage', () => {
      app.setTheme('dark');
      expect(deps.storage.get('tnzi:app:theme')).toBe('dark');
    });

    it('setTheme should call theme adapter', () => {
      const mockTheme = {
        applyTheme: vi.fn(),
        getResolvedTheme: vi.fn().mockReturnValue('light'),
        onSystemThemeChange: vi.fn(),
      };
      const depsWithTheme = createDeps({ theme: mockTheme });
      const appWithTheme = new AppStateManager(depsWithTheme);

      appWithTheme.setTheme('dark');
      expect(mockTheme.applyTheme).toHaveBeenCalledWith('dark');
      appWithTheme.dispose();
    });

    it('toggleTheme should cycle through light, dark, system', () => {
      expect(app.theme).toBe('light');
      app.toggleTheme();
      expect(app.theme).toBe('dark');
      app.toggleTheme();
      expect(app.theme).toBe('system');
      app.toggleTheme();
      expect(app.theme).toBe('light');
    });

    it('isDarkMode should be false for light theme', () => {
      app.setTheme('light');
      expect(app.isDarkMode).toBe(false);
    });

    it('isDarkMode should be true for dark theme', () => {
      app.setTheme('dark');
      expect(app.isDarkMode).toBe(true);
    });
  });

  // ------------------------------------------
  // Language
  // ------------------------------------------

  describe('language', () => {
    it('setLanguage should update language', () => {
      app.setLanguage('en-US');
      expect(app.language).toBe('en-US');
    });

    it('setLanguage should persist to storage', () => {
      app.setLanguage('en-US');
      expect(deps.storage.get('tnzi:app:language')).toBe('en-US');
    });
  });

  // ------------------------------------------
  // Sidebar
  // ------------------------------------------

  describe('sidebar', () => {
    it('toggleSidebar should toggle collapsed state', () => {
      expect(app.sidebarCollapsed).toBe(false);
      app.toggleSidebar();
      expect(app.sidebarCollapsed).toBe(true);
      app.toggleSidebar();
      expect(app.sidebarCollapsed).toBe(false);
    });

    it('setSidebarCollapsed should set state', () => {
      app.setSidebarCollapsed(true);
      expect(app.sidebarCollapsed).toBe(true);
    });

    it('toggleSidebar should persist', () => {
      app.toggleSidebar();
      expect(deps.storage.get('tnzi:app:sidebar')).toBe(true);
    });

    it('setSidebarMode should update mode', () => {
      app.setSidebarMode('mini');
      expect(app.sidebarMode).toBe('mini');
    });
  });

  // ------------------------------------------
  // Loading
  // ------------------------------------------

  describe('loading', () => {
    it('showLoading should set loading state', () => {
      app.showLoading('Processing...');
      expect(app.isLoading).toBe(true);
      expect(app.loadingMessage).toBe('Processing...');
    });

    it('showLoading without message should set null message', () => {
      app.showLoading();
      expect(app.isLoading).toBe(true);
      expect(app.loadingMessage).toBeNull();
    });

    it('hideLoading should clear loading state', () => {
      app.showLoading('Loading');
      app.hideLoading();
      expect(app.isLoading).toBe(false);
      expect(app.loadingMessage).toBeNull();
    });

    it('setLoading should set both loading and message', () => {
      app.setLoading(true, 'Saving...');
      expect(app.isLoading).toBe(true);
      expect(app.loadingMessage).toBe('Saving...');
    });
  });

  // ------------------------------------------
  // Notifications
  // ------------------------------------------

  describe('notifications', () => {
    it('addNotification should create notification with auto-generated fields', () => {
      const id = app.addNotification({
        type: 'info',
        severity: 'low',
        message: 'Hello',
        duration: 0,
      });
      expect(id).toBeDefined();
      expect(app.notificationsCount).toBe(1);
      expect(app.unreadNotificationsCount).toBe(1);
    });

    it('showSuccess should add success notification', () => {
      const id = app.showSuccess('Done!');
      expect(id).toBeDefined();
      expect(app.notificationsCount).toBe(1);
    });

    it('showError should add error notification', () => {
      const id = app.showError('Failed!', 'Error Title');
      expect(id).toBeDefined();
      expect(app.notificationsCount).toBe(1);
    });

    it('showWarning should add warning notification', () => {
      const id = app.showWarning('Watch out!');
      expect(id).toBeDefined();
    });

    it('showInfo should add info notification', () => {
      const id = app.showInfo('FYI');
      expect(id).toBeDefined();
    });

    it('removeNotification should remove by id', () => {
      const id = app.showSuccess('Done!');
      app.removeNotification(id);
      expect(app.notificationsCount).toBe(0);
    });

    it('removeNotification should call onDismiss callback', () => {
      const onDismiss = vi.fn();
      const id = app.addNotification({
        type: 'info',
        severity: 'low',
        message: 'Test',
        duration: 0,
        onDismiss,
      });
      app.removeNotification(id);
      expect(onDismiss).toHaveBeenCalled();
    });

    it('markNotificationRead should mark as read', () => {
      const id = app.showInfo('Test');
      expect(app.unreadNotificationsCount).toBe(1);
      app.markNotificationRead(id);
      expect(app.unreadNotificationsCount).toBe(0);
    });

    it('markAllNotificationsRead should mark all as read', () => {
      app.showInfo('Test 1');
      app.showInfo('Test 2');
      expect(app.unreadNotificationsCount).toBe(2);
      app.markAllNotificationsRead();
      expect(app.unreadNotificationsCount).toBe(0);
    });

    it('clearNotifications should remove all', () => {
      app.showInfo('Test 1');
      app.showInfo('Test 2');
      app.clearNotifications();
      expect(app.notificationsCount).toBe(0);
    });

    it('auto-dismiss should remove notification after duration', async () => {
      vi.useFakeTimers();
      app.addNotification({
        type: 'success',
        severity: 'low',
        message: 'Auto dismiss',
        duration: 3000,
      });
      expect(app.notificationsCount).toBe(1);
      vi.advanceTimersByTime(3100);
      expect(app.notificationsCount).toBe(0);
      vi.useRealTimers();
    });

    it('notifications should appear newest first', () => {
      app.showInfo('First');
      app.showInfo('Second');
      expect(app.ui.toasts[0].message).toBe('Second');
      expect(app.ui.toasts[1].message).toBe('First');
    });
  });

  // ------------------------------------------
  // Modals
  // ------------------------------------------

  describe('modals', () => {
    it('openModal should add to stack', () => {
      const id = app.openModal({ title: 'Test Modal', component: 'TestComponent' });
      expect(id).toBeDefined();
      expect(app.hasOpenModal).toBe(true);
      expect(app.modalCount).toBe(1);
      expect(app.topModalId).toBe(id);
    });

    it('closeModal should remove from stack', () => {
      const id = app.openModal({ title: 'Test', component: 'C' });
      app.closeModal(id);
      expect(app.hasOpenModal).toBe(false);
      expect(app.modalCount).toBe(0);
    });

    it('closeTopModal should remove the topmost modal', () => {
      app.openModal({ title: 'Modal 1', component: 'C1' });
      const id2 = app.openModal({ title: 'Modal 2', component: 'C2' });
      expect(app.topModalId).toBe(id2);
      app.closeTopModal();
      expect(app.modalCount).toBe(1);
    });

    it('closeAllModals should clear the stack', () => {
      app.openModal({ title: 'Modal 1', component: 'C1' });
      app.openModal({ title: 'Modal 2', component: 'C2' });
      app.closeAllModals();
      expect(app.hasOpenModal).toBe(false);
    });

    it('closeTopModal on empty stack should be safe', () => {
      app.closeTopModal();
      expect(app.modalCount).toBe(0);
    });
  });

  // ------------------------------------------
  // Connection
  // ------------------------------------------

  describe('connection', () => {
    it('setOnlineStatus should update status', () => {
      app.setOnlineStatus('away');
      expect(app.onlineStatus).toBe('away');
    });

    it('updateConnection should merge state', () => {
      app.updateConnection({ webSocketConnected: true });
      expect(app.connection.webSocketConnected).toBe(true);
      expect(app.connection.isConnected).toBe(true); // Unchanged
    });

    it('setConnected(true) should set connected and timestamp', () => {
      app.setConnected(false);
      expect(app.isConnected).toBe(false);
      app.setConnected(true);
      expect(app.isConnected).toBe(true);
      expect(app.connection.lastConnectedAt).toBeDefined();
      expect(app.connection.error).toBeNull();
    });
  });

  // ------------------------------------------
  // Initialization
  // ------------------------------------------

  describe('initialization', () => {
    it('setInitialized should mark as initialized', () => {
      app.setInitialized();
      expect(app.initialized).toBe(true);
      expect(app.initError).toBeNull();
    });

    it('setInitError should set error and mark not initialized', () => {
      app.setInitError('Config error');
      expect(app.initError).toBe('Config error');
      expect(app.initialized).toBe(false);
    });

    it('setInitError(null) should clear error and mark initialized', () => {
      app.setInitError('Some error');
      app.setInitError(null);
      expect(app.initError).toBeNull();
      expect(app.initialized).toBe(true);
    });
  });

  // ------------------------------------------
  // Persistence
  // ------------------------------------------

  describe('persistence', () => {
    it('loadPersistedState should restore theme from storage', () => {
      deps.storage.set('tnzi:app:theme', 'dark');
      app.loadPersistedState();
      expect(app.theme).toBe('dark');
    });

    it('loadPersistedState should restore language from storage', () => {
      deps.storage.set('tnzi:app:language', 'en-US');
      app.loadPersistedState();
      expect(app.language).toBe('en-US');
    });

    it('loadPersistedState should restore sidebar from storage', () => {
      deps.storage.set('tnzi:app:sidebar', true);
      app.loadPersistedState();
      expect(app.sidebarCollapsed).toBe(true);
    });

    it('persistState should write all persisted fields', () => {
      app.setTheme('dark');
      app.setLanguage('en-US');
      app.setSidebarCollapsed(true);
      app.persistState();

      expect(deps.storage.get('tnzi:app:theme')).toBe('dark');
      expect(deps.storage.get('tnzi:app:language')).toBe('en-US');
      expect(deps.storage.get('tnzi:app:sidebar')).toBe(true);
    });
  });

  // ------------------------------------------
  // Dispose
  // ------------------------------------------

  describe('dispose', () => {
    it('should clear all notifications', () => {
      app.showInfo('Test');
      app.dispose();
      expect(app.notificationsCount).toBe(0);
    });

    it('should close all modals', () => {
      app.openModal({ title: 'Test', component: 'C' });
      app.dispose();
      expect(app.hasOpenModal).toBe(false);
    });
  });

  // ------------------------------------------
  // createInitialAppState
  // ------------------------------------------

  describe('createInitialAppState', () => {
    it('should return clean initial state', () => {
      const state = createInitialAppState();
      expect(state.theme).toBe('light');
      expect(state.language).toBe('zh-CN');
      expect(state.onlineStatus).toBe('online');
      expect(state.connection.isConnected).toBe(true);
      expect(state.initialized).toBe(false);
      expect(state.ui.sidebarCollapsed).toBe(false);
      expect(state.ui.toasts).toEqual([]);
      expect(state.ui.modalStack).toEqual([]);
    });
  });
});
