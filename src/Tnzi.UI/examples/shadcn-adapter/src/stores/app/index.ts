/**
 * @tnzi/ui/stores/app
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
} from '@tnzi/core/state';
import type { Locale } from '@tnzi/core/adapters/i18n';
import type { ThemeMode } from '@tnzi/core/types/theme';
import { createStore } from '../factory';

/**
 * Internal type for actions that call other actions on the store.
 * The store proxy exposes all actions as direct methods.
 */
interface AppStoreThis {
  setTheme(theme: ThemeMode): void;
  applyTheme(theme: ThemeMode): void;
  applyLanguage(language: Locale): void;
  addNotification(notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string;
  removeNotification(id: string): void;
  closeModal(id: string): void;
}

// ============================================
// Helper Functions
// ============================================

const generateId = (): string => {
  return `${Date.now()}-${Math.random().toString(36).substring(2, 11)}`;
};

let _fullscreenListenerActive = false;
const _notificationTimers = new Map<string, ReturnType<typeof setTimeout>>();

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
    setTheme(this: { $state: AppState } & AppStoreThis, theme: ThemeMode): void {
      this.$state.theme = theme;
      this.applyTheme(theme);
    },

    toggleTheme(this: { $state: AppState } & AppStoreThis): void {
      const themes: ThemeMode[] = ['light', 'dark', 'system'];
      const currentIndex = themes.indexOf(this.$state.theme);
      const nextIndex = (currentIndex + 1) % themes.length;
      this.setTheme(themes[nextIndex] ?? 'light');
    },

    // Language actions
    setLanguage(this: { $state: AppState } & AppStoreThis, language: Locale): void {
      this.$state.language = language;
      this.applyLanguage(language);
    },

    // Sidebar actions
    toggleSidebar(this: { $state: AppState }): void {
      this.$state.ui.sidebarCollapsed = !this.$state.ui.sidebarCollapsed;
    },

    setSidebarCollapsed(this: { $state: AppState }, collapsed: boolean): void {
      this.$state.ui.sidebarCollapsed = collapsed;
    },

    setSidebarMode(this: { $state: AppState }, mode: AppState['ui']['sidebarMode']): void {
      this.$state.ui.sidebarMode = mode;
    },

    // Fullscreen actions
    toggleFullscreen(this: { $state: AppState }): void {
      if (typeof document === 'undefined') return;

      if (!this.$state.ui.fullscreen) {
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
    _initFullscreenListener(this: { $state: AppState }): void {
      if (_fullscreenListenerActive || typeof document === 'undefined') return;
      _fullscreenListenerActive = true;
      const self = this;
      document.addEventListener('fullscreenchange', () => {
        self.$state.ui.fullscreen = !!document.fullscreenElement;
      });
    },

    // Loading actions
    showLoading(this: { $state: AppState }, message?: string): void {
      this.$state.ui.loading = true;
      this.$state.ui.loadingMessage = message ?? null;
    },

    hideLoading(this: { $state: AppState }): void {
      this.$state.ui.loading = false;
      this.$state.ui.loadingMessage = null;
    },

    setLoading(this: { $state: AppState }, loading: boolean, message?: string | null): void {
      this.$state.ui.loading = loading;
      this.$state.ui.loadingMessage = message ?? null;
    },

    // Notification actions
    addNotification(this: { $state: AppState } & AppStoreThis, notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string {
      const id = generateId();
      const newNotification: AppNotification = {
        ...notification,
        id,
        timestamp: new Date(),
        read: false,
      };

      this.$state.ui.toasts = [newNotification, ...this.$state.ui.toasts];

      if (notification.duration > 0) {
        const timeoutId = setTimeout(() => {
          this.removeNotification(id);
        }, notification.duration);
        _notificationTimers.set(id, timeoutId);
      }

      return id;
    },

    showSuccess(this: { $state: AppState } & AppStoreThis, message: string, title?: string): string {
      return this.addNotification({
        type: 'success',
        severity: 'low',
        message,
        title,
        duration: 5000,
      });
    },

    showError(this: { $state: AppState } & AppStoreThis, message: string, title?: string): string {
      return this.addNotification({
        type: 'error',
        severity: 'high',
        message,
        title: title ?? 'Error',
        duration: 0,
      });
    },

    showWarning(this: { $state: AppState } & AppStoreThis, message: string, title?: string): string {
      return this.addNotification({
        type: 'warning',
        severity: 'medium',
        message,
        title,
        duration: 8000,
      });
    },

    showInfo(this: { $state: AppState } & AppStoreThis, message: string, title?: string): string {
      return this.addNotification({
        type: 'info',
        severity: 'low',
        message,
        title,
        duration: 5000,
      });
    },

    removeNotification(this: { $state: AppState }, id: string): void {
      const timer = _notificationTimers.get(id);
      if (timer) { clearTimeout(timer); _notificationTimers.delete(id); }

      const index = this.$state.ui.toasts.findIndex((n: AppNotification) => n.id === id);
      if (index !== -1) {
        const notification = this.$state.ui.toasts[index];
        if (notification) notification.onDismiss?.();
        this.$state.ui.toasts = this.$state.ui.toasts.filter((n: AppNotification) => n.id !== id);
      }
    },

    // HIGH-8: Immutable notification update
    markNotificationRead(this: { $state: AppState }, id: string): void {
      this.$state.ui.toasts = this.$state.ui.toasts.map((n: AppNotification) =>
        n.id === id ? { ...n, read: true } : n
      );
    },

    markAllNotificationsRead(this: { $state: AppState }): void {
      this.$state.ui.toasts = this.$state.ui.toasts.map((n: AppNotification) => ({ ...n, read: true }));
    },

    clearNotifications(this: { $state: AppState }): void {
      for (const timer of _notificationTimers.values()) {
        clearTimeout(timer);
      }
      _notificationTimers.clear();
      this.$state.ui.toasts = [];
    },

    // Modal actions (immutable patterns)
    openModal(this: { $state: AppState }, modal: Omit<AppModalState, 'id'>): string {
      const id = generateId();
      const newModal: AppModalState = { ...modal, id };
      this.$state.ui.modalStack = [...this.$state.ui.modalStack, newModal];
      return id;
    },

    closeModal(this: { $state: AppState }, id: string): void {
      this.$state.ui.modalStack = this.$state.ui.modalStack.filter((m: AppModalState) => m.id !== id);
    },

    closeTopModal(this: { $state: AppState } & AppStoreThis): void {
      if (this.$state.ui.modalStack.length > 0) {
        const topModal = this.$state.ui.modalStack[this.$state.ui.modalStack.length - 1];
        if (topModal) this.closeModal(topModal.id);
      }
    },

    closeAllModals(this: { $state: AppState }): void {
      this.$state.ui.modalStack = [];
    },

    // Online status actions
    setOnlineStatus(this: { $state: AppState }, status: OnlineStatus): void {
      this.$state.onlineStatus = status;
    },

    // Connection actions
    updateConnection(this: { $state: AppState }, connState: Partial<AppState['connection']>): void {
      this.$state.connection = { ...this.$state.connection, ...connState };
    },

    setConnected(this: { $state: AppState }, connected: boolean): void {
      this.$state.connection = {
        ...this.$state.connection,
        isConnected: connected,
        ...(connected ? { lastConnectedAt: new Date(), error: null } : {}),
      };
    },

    // Initialization actions
    setInitialized(this: { $state: AppState }): void {
      this.$state.initialized = true;
      this.$state.initError = null;
    },

    setInitError(this: { $state: AppState }, error: string | null): void {
      this.$state.initError = error;
      this.$state.initialized = !error;
    },

    // Persistence actions (handled by factory's persist plugin)
    async loadPersistedState(this: { $state: AppState } & AppStoreThis): Promise<void> {
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
  // Sync fullscreen state with browser (handles ESC key, etc.)
  (store as unknown as { _initFullscreenListener: () => void })._initFullscreenListener();

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
