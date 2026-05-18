/**
 * @tnzi/ui/stores/app
 *
 * Application state store using Pinia defineStore directly.
 * Manages UI state, theme, notifications, modals, and connection status.
 */

import { defineStore } from 'pinia';
import { computed } from 'vue';
import type {
  AppState,
  AppNotification,
  AppModalState,
  OnlineStatus,
} from '@tnzi/core/state';
import type { Locale } from '@tnzi/core/adapters/i18n';
import type { ThemeMode } from '@tnzi/core/types';
import { getStoreStorage } from '../factory';
import { applyThemeToDOM, applyLanguageToDOM } from '../../utils/naive-helpers';

// ============================================
// Storage Keys
// ============================================

const STORAGE_KEY = 'app_state';

// ============================================
// Helper Functions
// ============================================

const generateId = (): string => {
  return `${Date.now()}-${Math.random().toString(36).substring(2, 11)}`;
};

let _fullscreenListenerActive = false;

// ============================================
// App Store Definition
// ============================================

export const useAppStore = defineStore('app', {
  state: (): AppState => ({
    ui: {
      sidebarCollapsed: false,
      sidebarMode: 'responsive',
      headerVisible: true,
      footerVisible: true,
      fullscreen: false,
      loading: false,
      loadingMessage: null,
      modalStack: [],
      toasts: [],
    },
    theme: 'light',
    language: 'zh-CN',
    onlineStatus: 'online',
    connection: {
      isConnected: true,
      webSocketConnected: false,
      lastConnectedAt: null,
      error: null,
    },
    version: '',
    environment: 'development',
    initialized: false,
    initError: null,
  }),

  getters: {
    currentTheme: (state): ThemeMode => state.theme,

    isDarkMode: (state): boolean => {
      return (
        state.theme === 'dark' ||
        (state.theme === 'system' &&
          typeof window !== 'undefined' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches)
      );
    },

    currentLanguage: (state): Locale => state.language,

    sidebarCollapsed: (state): boolean => state.ui.sidebarCollapsed,

    sidebarMode: (state): AppState['ui']['sidebarMode'] => state.ui.sidebarMode,

    isFullscreen: (state): boolean => state.ui.fullscreen,

    isLoading: (state): boolean => state.ui.loading,

    loadingMessage: (state): string | null => state.ui.loadingMessage,

    unreadNotificationsCount: (state): number =>
      state.ui.toasts.filter((n) => !n.read).length,

    notificationsCount: (state): number => state.ui.toasts.length,

    hasOpenModal: (state): boolean => state.ui.modalStack.length > 0,

    topModalId: (state): string | null =>
      state.ui.modalStack.length > 0
        ? state.ui.modalStack[state.ui.modalStack.length - 1]?.id ?? null
        : null,

    modalCount: (state): number => state.ui.modalStack.length,

    currentOnlineStatus: (state): OnlineStatus => state.onlineStatus,

    isConnected: (state): boolean => state.connection.isConnected,

    isInitialized: (state): boolean => state.initialized,

    currentInitError: (state): string | null => state.initError,
  },

  actions: {
    // ---- Theme actions ----

    setTheme(theme: ThemeMode): void {
      this.theme = theme;
      this.applyTheme(theme);
      this.persistState();
    },

    toggleTheme(): void {
      const themes: ThemeMode[] = ['light', 'dark', 'system'];
      const currentIndex = themes.indexOf(this.theme);
      const nextIndex = (currentIndex + 1) % themes.length;
      this.setTheme(themes[nextIndex] ?? 'light');
    },

    // ---- Language actions ----

    setLanguage(language: Locale): void {
      this.language = language;
      this.applyLanguage(language);
      this.persistState();
    },

    // ---- Sidebar actions ----

    toggleSidebar(): void {
      this.ui.sidebarCollapsed = !this.ui.sidebarCollapsed;
      this.persistState();
    },

    setSidebarCollapsed(collapsed: boolean): void {
      this.ui.sidebarCollapsed = collapsed;
      this.persistState();
    },

    setSidebarMode(mode: AppState['ui']['sidebarMode']): void {
      this.ui.sidebarMode = mode;
      this.persistState();
    },

    // ---- Fullscreen actions ----

    toggleFullscreen(): void {
      if (typeof document === 'undefined') return;

      if (!this.ui.fullscreen) {
        document.documentElement.requestFullscreen?.();
      } else {
        document.exitFullscreen?.();
      }
      // Note: ui.fullscreen is updated by the fullscreenchange event listener (see _initFullscreenListener)
    },

    /**
     * Initialize fullscreenchange event listener to sync state with browser.
     * Called automatically from useApp(). Idempotent — safe to call multiple times.
     */
    _initFullscreenListener(): void {
      if (_fullscreenListenerActive || typeof document === 'undefined') return;
      _fullscreenListenerActive = true;
      // eslint-disable-next-line @typescript-eslint/no-this-alias
      const store = this;
      document.addEventListener('fullscreenchange', () => {
        store.ui.fullscreen = !!document.fullscreenElement;
      });
    },

    // ---- Loading actions ----

    showLoading(message?: string): void {
      this.ui.loading = true;
      this.ui.loadingMessage = message ?? null;
    },

    hideLoading(): void {
      this.ui.loading = false;
      this.ui.loadingMessage = null;
    },

    setLoading(loading: boolean, message?: string | null): void {
      this.ui.loading = loading;
      this.ui.loadingMessage = message ?? null;
    },

    // ---- Notification actions ----

    addNotification(notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string {
      const id = generateId();
      const newNotification: AppNotification = {
        ...notification,
        id,
        timestamp: new Date(),
        read: false,
      };

      this.ui.toasts = [newNotification, ...this.ui.toasts];

      // Auto-dismiss if duration is set
      if (notification.duration > 0) {
        setTimeout(() => {
          this.removeNotification(id);
        }, notification.duration);
      }

      return id;
    },

    showSuccess(message: string, title?: string): string {
      return this.addNotification({
        type: 'success',
        severity: 'low',
        message,
        title,
        duration: 5000,
      });
    },

    showError(message: string, title?: string): string {
      return this.addNotification({
        type: 'error',
        severity: 'high',
        message,
        title: title ?? 'Error',
        duration: 0,
      });
    },

    showWarning(message: string, title?: string): string {
      return this.addNotification({
        type: 'warning',
        severity: 'medium',
        message,
        title,
        duration: 8000,
      });
    },

    showInfo(message: string, title?: string): string {
      return this.addNotification({
        type: 'info',
        severity: 'low',
        message,
        title,
        duration: 5000,
      });
    },

    removeNotification(id: string): void {
      const index = this.ui.toasts.findIndex((n: AppNotification) => n.id === id);
      if (index !== -1) {
        const notification = this.ui.toasts[index] as AppNotification | undefined;
        notification?.onDismiss?.();
        this.ui.toasts = this.ui.toasts.filter((n: AppNotification) => n.id !== id);
      }
    },

    markNotificationRead(id: string): void {
      this.ui.toasts = this.ui.toasts.map((n: AppNotification) =>
        n.id === id ? { ...n, read: true } : n
      );
    },

    markAllNotificationsRead(): void {
      this.ui.toasts = this.ui.toasts.map((n: AppNotification) => ({ ...n, read: true }));
    },

    clearNotifications(): void {
      this.ui.toasts = [];
    },

    // ---- Modal actions ----

    openModal(modal: Omit<AppModalState, 'id'>): string {
      const id = generateId();
      const newModal: AppModalState = {
        ...modal,
        id,
      };

      this.ui.modalStack.push(newModal);
      return id;
    },

    closeModal(id: string): void {
      const index = this.ui.modalStack.findIndex((m: AppModalState) => m.id === id);
      if (index !== -1) {
        this.ui.modalStack.splice(index, 1);
      }
    },

    closeTopModal(): void {
      if (this.ui.modalStack.length > 0) {
        const topModal = this.ui.modalStack[this.ui.modalStack.length - 1] as AppModalState | undefined;
        if (topModal) this.closeModal(topModal.id);
      }
    },

    closeAllModals(): void {
      this.ui.modalStack = [];
    },

    // ---- Online status actions ----

    setOnlineStatus(status: OnlineStatus): void {
      this.onlineStatus = status;
    },

    // ---- Connection actions ----

    updateConnection(state: Partial<AppState['connection']>): void {
      this.connection = { ...this.connection, ...state };
    },

    setConnected(connected: boolean): void {
      this.connection.isConnected = connected;
      if (connected) {
        this.connection.lastConnectedAt = new Date();
        this.connection.error = null;
      }
    },

    // ---- Initialization actions ----

    setInitialized(): void {
      this.initialized = true;
      this.initError = null;
    },

    setInitError(error: string | null): void {
      this.initError = error;
      this.initialized = !error;
    },

    // ---- Persistence actions ----

    async loadPersistedState(): Promise<void> {
      const storage = getStoreStorage();
      if (!storage) return;

      const data = storage.get<{
        theme?: ThemeMode;
        language?: Locale;
        sidebarCollapsed?: boolean;
        sidebarMode?: AppState['ui']['sidebarMode'];
      }>(STORAGE_KEY);

      if (data) {
        if (data.theme) this.theme = data.theme;
        if (data.language) this.language = data.language;
        if (data.sidebarCollapsed !== undefined) this.ui.sidebarCollapsed = data.sidebarCollapsed;
        if (data.sidebarMode) this.ui.sidebarMode = data.sidebarMode;
      }

      this.applyTheme(this.theme);
      this.applyLanguage(this.language);
    },

    persistState(): void {
      const storage = getStoreStorage();
      if (!storage) return;

      storage.set(STORAGE_KEY, {
        theme: this.theme,
        language: this.language,
        sidebarCollapsed: this.ui.sidebarCollapsed,
        sidebarMode: this.ui.sidebarMode,
      });
    },

    // ---- Internal helpers ----

    applyTheme(theme: ThemeMode): void {
      applyThemeToDOM(theme);
    },

    applyLanguage(language: Locale): void {
      applyLanguageToDOM(language);
    },
  },
});

// ============================================
// Composable Wrapper
// ============================================

/**
 * Vue composable for using the app store.
 * Provides reactive computed refs and bound action methods.
 */
export function useApp() {
  const store = useAppStore();
  // Sync fullscreen state with browser (handles ESC key, etc.)
  store._initFullscreenListener();

  return {
    // State (reactive)
    theme: computed(() => store.currentTheme),
    language: computed(() => store.currentLanguage),
    isDarkMode: computed(() => store.isDarkMode),
    onlineStatus: computed(() => store.currentOnlineStatus),
    isConnected: computed(() => store.isConnected),
    initialized: computed(() => store.isInitialized),
    initError: computed(() => store.currentInitError),

    // UI State
    sidebarCollapsed: computed(() => store.sidebarCollapsed),
    sidebarMode: computed(() => store.sidebarMode),
    isFullscreen: computed(() => store.isFullscreen),
    isLoading: computed(() => store.isLoading),
    loadingMessage: computed(() => store.loadingMessage),

    // Notifications
    notifications: computed(() => store.ui.toasts),
    unreadCount: computed(() => store.unreadNotificationsCount),
    notificationsCount: computed(() => store.notificationsCount),

    // Modals
    hasOpenModal: computed(() => store.hasOpenModal),
    topModalId: computed(() => store.topModalId),
    modalCount: computed(() => store.modalCount),

    // Actions - Theme
    setTheme: store.setTheme.bind(store),
    toggleTheme: store.toggleTheme.bind(store),

    // Actions - Language
    setLanguage: store.setLanguage.bind(store),

    // Actions - Sidebar
    toggleSidebar: store.toggleSidebar.bind(store),
    setSidebarCollapsed: store.setSidebarCollapsed.bind(store),
    setSidebarMode: store.setSidebarMode.bind(store),

    // Actions - Fullscreen
    toggleFullscreen: store.toggleFullscreen.bind(store),

    // Actions - Loading
    showLoading: store.showLoading.bind(store),
    hideLoading: store.hideLoading.bind(store),
    setLoading: store.setLoading.bind(store),

    // Actions - Notifications
    addNotification: store.addNotification.bind(store),
    showSuccess: store.showSuccess.bind(store),
    showError: store.showError.bind(store),
    showWarning: store.showWarning.bind(store),
    showInfo: store.showInfo.bind(store),
    removeNotification: store.removeNotification.bind(store),
    markNotificationRead: store.markNotificationRead.bind(store),
    markAllNotificationsRead: store.markAllNotificationsRead.bind(store),
    clearNotifications: store.clearNotifications.bind(store),

    // Actions - Modals
    openModal: store.openModal.bind(store),
    closeModal: store.closeModal.bind(store),
    closeTopModal: store.closeTopModal.bind(store),
    closeAllModals: store.closeAllModals.bind(store),

    // Actions - Online Status
    setOnlineStatus: store.setOnlineStatus.bind(store),

    // Actions - Connection
    updateConnection: store.updateConnection.bind(store),
    setConnected: store.setConnected.bind(store),

    // Actions - Initialization
    setInitialized: store.setInitialized.bind(store),
    setInitError: store.setInitError.bind(store),

    // Actions - Persistence
    loadPersistedState: store.loadPersistedState.bind(store),
    persistState: store.persistState.bind(store),
  };
}
