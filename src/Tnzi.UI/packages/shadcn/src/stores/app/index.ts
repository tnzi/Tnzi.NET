/**
 * @tnzi/shadcn/stores/app
 *
 * Application state store implementation using store factory.
 * Manages UI state, theme, notifications, and modal state.
 */

import { computed } from 'vue';
import type {
  AppState,
  AppNotification,
  AppModalState,
  OnlineStatus,
} from '@tnzi/core/stores/app';
import type { Locale } from '@tnzi/core/adapters/i18n';
import type { ThemeMode } from '@tnzi/core/types/theme';
import { createStore } from '../factory';

// ============================================
// Helper Functions
// ============================================

const generateId = (): string => {
  return `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
};

// ============================================
// Default State
// ============================================

const defaultState = (): AppState => ({
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
});

// ============================================
// App Store Definition
// ============================================

export const useAppStore = createStore<AppState>({
  id: 'app',
  state: defaultState,

  getters: {
    currentTheme: (state: AppState): ThemeMode => state.theme,

    isDarkMode: (state: AppState): boolean => {
      return (
        state.theme === 'dark' ||
        (state.theme === 'system' &&
          typeof window !== 'undefined' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches)
      );
    },

    currentLanguage: (state: AppState): Locale => state.language,

    sidebarCollapsed: (state: AppState): boolean => state.ui.sidebarCollapsed,

    sidebarMode: (state: AppState): AppState['ui']['sidebarMode'] => state.ui.sidebarMode,

    isFullscreen: (state: AppState): boolean => state.ui.fullscreen,

    isLoading: (state: AppState): boolean => state.ui.loading,

    loadingMessage: (state: AppState): string | null => state.ui.loadingMessage,

    unreadNotificationsCount: (state: AppState): number =>
      state.ui.toasts.filter((n) => !n.read).length,

    notificationsCount: (state: AppState): number => state.ui.toasts.length,

    hasOpenModal: (state: AppState): boolean => state.ui.modalStack.length > 0,

    topModalId: (state: AppState): string | null =>
      state.ui.modalStack.length > 0 ? state.ui.modalStack[state.ui.modalStack.length - 1]?.id ?? null : null,

    modalCount: (state: AppState): number => state.ui.modalStack.length,

    currentOnlineStatus: (state: AppState): OnlineStatus => state.onlineStatus,

    isConnected: (state: AppState): boolean => state.connection.isConnected,

    isInitialized: (state: AppState): boolean => state.initialized,

    currentInitError: (state: AppState): string | null => state.initError,
  },

  actions: {
    // Theme actions
    setTheme(theme: ThemeMode): void {
      this.$state.theme = theme;
      this.applyTheme(theme);
    },

    toggleTheme(): void {
      const themes: ThemeMode[] = ['light', 'dark', 'system'];
      const currentIndex = themes.indexOf(this.$state.theme);
      const nextIndex = (currentIndex + 1) % themes.length;
      this.setTheme(themes[nextIndex] ?? 'light');
    },

    // Language actions
    setLanguage(language: Locale): void {
      this.$state.language = language;
      this.applyLanguage(language);
    },

    // Sidebar actions
    toggleSidebar(): void {
      this.$state.ui.sidebarCollapsed = !this.$state.ui.sidebarCollapsed;
    },

    setSidebarCollapsed(collapsed: boolean): void {
      this.$state.ui.sidebarCollapsed = collapsed;
    },

    setSidebarMode(mode: AppState['ui']['sidebarMode']): void {
      this.$state.ui.sidebarMode = mode;
    },

    // Fullscreen actions
    toggleFullscreen(): void {
      if (typeof document === 'undefined') return;

      if (!this.$state.ui.fullscreen) {
        document.documentElement.requestFullscreen?.();
      } else {
        document.exitFullscreen?.();
      }
    },

    // Loading actions
    showLoading(message?: string): void {
      this.$state.ui.loading = true;
      this.$state.ui.loadingMessage = message ?? null;
    },

    hideLoading(): void {
      this.$state.ui.loading = false;
      this.$state.ui.loadingMessage = null;
    },

    setLoading(loading: boolean, message?: string | null): void {
      this.$state.ui.loading = loading;
      this.$state.ui.loadingMessage = message ?? null;
    },

    // Notification actions
    addNotification(notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string {
      const id = generateId();
      const newNotification: AppNotification = {
        ...notification,
        id,
        timestamp: new Date(),
        read: false,
      };

      this.$state.ui.toasts = [newNotification, ...this.$state.ui.toasts];

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
      const index = this.$state.ui.toasts.findIndex((n: AppNotification) => n.id === id);
      if (index !== -1) {
        const notification = this.$state.ui.toasts[index];
        notification.onDismiss?.();
        this.$state.ui.toasts = this.$state.ui.toasts.filter((n: AppNotification) => n.id !== id);
      }
    },

    markNotificationRead(id: string): void {
      const notification = this.$state.ui.toasts.find((n: AppNotification) => n.id === id);
      if (notification) {
        notification.read = true;
      }
    },

    markAllNotificationsRead(): void {
      this.$state.ui.toasts.forEach((n: AppNotification) => {
        n.read = true;
      });
    },

    clearNotifications(): void {
      this.$state.ui.toasts = [];
    },

    // Modal actions
    openModal(modal: Omit<AppModalState, 'id'>): string {
      const id = generateId();
      const newModal: AppModalState = {
        ...modal,
        id,
      };

      this.$state.ui.modalStack.push(newModal);
      return id;
    },

    closeModal(id: string): void {
      const index = this.$state.ui.modalStack.findIndex((m: AppModalState) => m.id === id);
      if (index !== -1) {
        this.$state.ui.modalStack.splice(index, 1);
      }
    },

    closeTopModal(): void {
      if (this.$state.ui.modalStack.length > 0) {
        const topModal = this.$state.ui.modalStack[this.$state.ui.modalStack.length - 1];
        this.closeModal(topModal.id);
      }
    },

    closeAllModals(): void {
      this.$state.ui.modalStack = [];
    },

    // Online status actions
    setOnlineStatus(status: OnlineStatus): void {
      this.$state.onlineStatus = status;
    },

    // Connection actions
    updateConnection(state: Partial<AppState['connection']>): void {
      this.$state.connection = { ...this.$state.connection, ...state };
    },

    setConnected(connected: boolean): void {
      this.$state.connection.isConnected = connected;
      if (connected) {
        this.$state.connection.lastConnectedAt = new Date();
        this.$state.connection.error = null;
      }
    },

    // Initialization actions
    setInitialized(): void {
      this.$state.initialized = true;
      this.$state.initError = null;
    },

    setInitError(error: string | null): void {
      this.$state.initError = error;
      this.$state.initialized = !error;
    },

    // Persistence actions (handled by factory's persist plugin)
    async loadPersistedState(): Promise<void> {
      if (this.$state.theme) {
        this.applyTheme(this.$state.theme);
      }
      if (this.$state.language) {
        this.applyLanguage(this.$state.language);
      }
    },

    async persistState(): Promise<void> {
      // Handled automatically by the factory's persist plugin
    },

    // Apply theme to document
    applyTheme(theme: ThemeMode): void {
      if (typeof document === 'undefined') return;

      const root = document.documentElement;
      const isDark =
        theme === 'dark' ||
        (theme === 'system' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      root.classList.toggle('dark', isDark);
    },

    // Apply language to document
    applyLanguage(language: Locale): void {
      if (typeof document === 'undefined') return;
      document.documentElement.lang = language;
    },
  },

  persist: {
    key: 'app_state',
    paths: ['theme', 'language', 'ui.sidebarCollapsed', 'ui.sidebarMode'],
  },
});

// ============================================
// Export as Composable
// ============================================

/**
 * Vue composable for using the app store.
 * The underlying PiniaStoreWrapper Proxy exposes state/getters/actions directly.
 */
export function useApp() {
  const store = useAppStore();

  return {
    // State (typed via FlatStoreInstance<AppState>)
    theme: computed(() => store.currentTheme as ThemeMode),
    language: computed(() => store.currentLanguage as Locale),
    isDarkMode: computed(() => store.isDarkMode as boolean),
    onlineStatus: computed(() => store.currentOnlineStatus as OnlineStatus),
    isConnected: computed(() => store.isConnected as boolean),
    initialized: computed(() => store.isInitialized as boolean),
    initError: computed(() => store.currentInitError as string | null),

    // UI State (getters exposed via Proxy)
    sidebarCollapsed: computed(() => store.sidebarCollapsed as boolean),
    sidebarMode: computed(() => store.sidebarMode as AppState['ui']['sidebarMode']),
    isFullscreen: computed(() => store.isFullscreen as boolean),
    isLoading: computed(() => store.isLoading as boolean),
    loadingMessage: computed(() => store.loadingMessage as string | null),

    // Notifications
    notifications: computed(() => store.$state.ui.toasts),
    unreadCount: computed(() => store.unreadNotificationsCount as number),
    notificationsCount: computed(() => store.notificationsCount as number),

    // Modals
    hasOpenModal: computed(() => store.hasOpenModal as boolean),
    topModalId: computed(() => store.topModalId as string | null),
    modalCount: computed(() => store.modalCount as number),

    // Actions (bound to store for proper 'this' context)
    setTheme: (store.setTheme as (theme: ThemeMode) => void).bind(store),
    toggleTheme: (store.toggleTheme as () => void).bind(store),
    setLanguage: (store.setLanguage as (language: Locale) => void).bind(store),
    toggleSidebar: (store.toggleSidebar as () => void).bind(store),
    setSidebarCollapsed: (store.setSidebarCollapsed as (collapsed: boolean) => void).bind(store),
    setSidebarMode: (store.setSidebarMode as (mode: AppState['ui']['sidebarMode']) => void).bind(store),
    toggleFullscreen: (store.toggleFullscreen as () => void).bind(store),
    showLoading: (store.showLoading as (message?: string) => void).bind(store),
    hideLoading: (store.hideLoading as () => void).bind(store),
    setLoading: (store.setLoading as (loading: boolean, message?: string | null) => void).bind(store),
    addNotification: (store.addNotification as (notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>) => string).bind(store),
    showSuccess: (store.showSuccess as (message: string, title?: string) => string).bind(store),
    showError: (store.showError as (message: string, title?: string) => string).bind(store),
    showWarning: (store.showWarning as (message: string, title?: string) => string).bind(store),
    showInfo: (store.showInfo as (message: string, title?: string) => string).bind(store),
    removeNotification: (store.removeNotification as (id: string) => void).bind(store),
    markNotificationRead: (store.markNotificationRead as (id: string) => void).bind(store),
    markAllNotificationsRead: (store.markAllNotificationsRead as () => void).bind(store),
    clearNotifications: (store.clearNotifications as () => void).bind(store),
    openModal: (store.openModal as (modal: Omit<AppModalState, 'id'>) => string).bind(store),
    closeModal: (store.closeModal as (id: string) => void).bind(store),
    closeTopModal: (store.closeTopModal as () => void).bind(store),
    closeAllModals: (store.closeAllModals as () => void).bind(store),
    setOnlineStatus: (store.setOnlineStatus as (status: OnlineStatus) => void).bind(store),
    updateConnection: (store.updateConnection as (state: Partial<AppState['connection']>) => void).bind(store),
    setConnected: (store.setConnected as (connected: boolean) => void).bind(store),
    setInitialized: (store.setInitialized as () => void).bind(store),
    setInitError: (store.setInitError as (error: string | null) => void).bind(store),
    loadPersistedState: (store.loadPersistedState as () => Promise<void>).bind(store),
    persistState: (store.persistState as () => Promise<void>).bind(store),
  };
}
