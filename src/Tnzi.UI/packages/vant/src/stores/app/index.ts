/**
 * @tnzi/vant/stores/app
 *
 * Application state store implementation using Pinia defineStore.
 * Manages UI state, theme, notifications, modals, and connection status.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
import type {
  AppState,
  AppNotification,
  AppModalState,
  OnlineStatus,
} from '@tnzi/core/stores/app';
import type { Locale } from '@tnzi/core/adapters/i18n';
import type { ThemeMode } from '@tnzi/core/types/theme';
import { getStoreStorage } from '../factory';

// ============================================
// Storage Keys
// ============================================

const STORAGE_KEY = 'app_state';

// ============================================
// Helper Functions
// ============================================

/** Generate a unique ID for notifications and modals. */
const generateId = (): string => {
  return `${Date.now()}-${Math.random().toString(36).substring(2, 11)}`;
};

// TODO: add cleanup support for tests — removeEventListener on store disposal
let _fullscreenListenerActive = false;

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

export const useAppStore = defineStore('app', {
  state: defaultState,

  getters: {
    /** Current theme mode */
    currentTheme: (state): ThemeMode => state.theme,

    /** Whether dark mode is active (considering system preference) */
    isDarkMode: (state): boolean => {
      return (
        state.theme === 'dark' ||
        (state.theme === 'system' &&
          typeof window !== 'undefined' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches)
      );
    },

    /** Current language */
    currentLanguage: (state): Locale => state.language,

    /** Whether sidebar is collapsed */
    sidebarCollapsed: (state): boolean => state.ui.sidebarCollapsed,

    /** Current sidebar mode */
    sidebarMode: (state): AppState['ui']['sidebarMode'] => state.ui.sidebarMode,

    /** Whether fullscreen mode is active */
    isFullscreen: (state): boolean => state.ui.fullscreen,

    /** Whether loading overlay is visible */
    isLoading: (state): boolean => state.ui.loading,

    /** Loading overlay message */
    loadingMessage: (state): string | null => state.ui.loadingMessage,

    /** Unread notifications count */
    unreadNotificationsCount: (state): number =>
      state.ui.toasts.filter((n: AppNotification) => !n.read).length,

    /** Total notifications count */
    notificationsCount: (state): number => state.ui.toasts.length,

    /** Whether any modal is open */
    hasOpenModal: (state): boolean => state.ui.modalStack.length > 0,

    /** Topmost modal ID */
    topModalId: (state): string | null =>
      state.ui.modalStack.length > 0
        ? state.ui.modalStack[state.ui.modalStack.length - 1]?.id ?? null
        : null,

    /** Number of open modals */
    modalCount: (state): number => state.ui.modalStack.length,

    /** Current online status */
    currentOnlineStatus: (state): OnlineStatus => state.onlineStatus,

    /** Whether connected to server */
    isConnected: (state): boolean => state.connection.isConnected,

    /** Whether app is initialized */
    isInitialized: (state): boolean => state.initialized,

    /** Initialization error */
    initializationError: (state): string | null => state.initError,
  },

  actions: {
    // ============================================
    // Theme Actions
    // ============================================

    /**
     * Set application theme.
     * @param theme - Theme mode (light, dark, system)
     */
    setTheme(theme: ThemeMode): void {
      this.theme = theme;
      this.applyTheme(theme);
      this.persistState();
    },

    /**
     * Toggle between light, dark, and system themes.
     */
    toggleTheme(): void {
      const themes: ThemeMode[] = ['light', 'dark', 'system'];
      const currentIndex = themes.indexOf(this.theme);
      const nextIndex = (currentIndex + 1) % themes.length;
      this.setTheme(themes[nextIndex] ?? 'light');
    },

    // ============================================
    // Language Actions
    // ============================================

    /**
     * Set interface language.
     * @param language - Language locale
     */
    setLanguage(language: Locale): void {
      this.language = language;
      this.applyLanguage(language);
      this.persistState();
    },

    // ============================================
    // Sidebar Actions
    // ============================================

    /**
     * Toggle sidebar collapsed state.
     */
    toggleSidebar(): void {
      this.ui.sidebarCollapsed = !this.ui.sidebarCollapsed;
      this.persistState();
    },

    /**
     * Set sidebar collapsed state.
     * @param collapsed - Whether sidebar should be collapsed
     */
    setSidebarCollapsed(collapsed: boolean): void {
      this.ui.sidebarCollapsed = collapsed;
      this.persistState();
    },

    /**
     * Set sidebar mode.
     * @param mode - Sidebar mode (responsive, pinned, mini)
     */
    setSidebarMode(mode: AppState['ui']['sidebarMode']): void {
      this.ui.sidebarMode = mode;
      this.persistState();
    },

    // ============================================
    // Fullscreen Actions
    // ============================================

    /**
     * Toggle fullscreen mode.
     */
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

    // ============================================
    // Loading Actions
    // ============================================

    /**
     * Show loading overlay with optional message.
     * @param message - Loading message
     */
    showLoading(message?: string): void {
      this.ui.loading = true;
      this.ui.loadingMessage = message ?? null;
    },

    /**
     * Hide loading overlay.
     */
    hideLoading(): void {
      this.ui.loading = false;
      this.ui.loadingMessage = null;
    },

    /**
     * Set loading state.
     * @param loading - Whether loading
     * @param message - Loading message
     */
    setLoading(loading: boolean, message?: string | null): void {
      this.ui.loading = loading;
      this.ui.loadingMessage = message ?? null;
    },

    // ============================================
    // Notification Actions
    // ============================================

    /**
     * Add a notification to the toast queue.
     * @param notification - Notification data (id, timestamp, read auto-assigned)
     * @returns Notification ID
     */
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

    /**
     * Show success notification (5s auto-dismiss).
     * @param message - Success message
     * @param title - Optional title
     * @returns Notification ID
     */
    showSuccess(message: string, title?: string): string {
      return this.addNotification({
        type: 'success',
        severity: 'low',
        message,
        title,
        duration: 5000,
      });
    },

    /**
     * Show error notification (persistent until dismissed).
     * @param message - Error message
     * @param title - Optional title
     * @returns Notification ID
     */
    showError(message: string, title?: string): string {
      return this.addNotification({
        type: 'error',
        severity: 'high',
        message,
        title: title ?? 'Error',
        duration: 0,
      });
    },

    /**
     * Show warning notification (8s auto-dismiss).
     * @param message - Warning message
     * @param title - Optional title
     * @returns Notification ID
     */
    showWarning(message: string, title?: string): string {
      return this.addNotification({
        type: 'warning',
        severity: 'medium',
        message,
        title,
        duration: 8000,
      });
    },

    /**
     * Show info notification (5s auto-dismiss).
     * @param message - Info message
     * @param title - Optional title
     * @returns Notification ID
     */
    showInfo(message: string, title?: string): string {
      return this.addNotification({
        type: 'info',
        severity: 'low',
        message,
        title,
        duration: 5000,
      });
    },

    /**
     * Remove a notification by ID.
     * @param id - Notification ID
     */
    removeNotification(id: string): void {
      const index = this.ui.toasts.findIndex((n: AppNotification) => n.id === id);
      if (index !== -1) {
        const notification = this.ui.toasts[index]!;
        notification.onDismiss?.();
        this.ui.toasts = this.ui.toasts.filter((n: AppNotification) => n.id !== id);
      }
    },

    /**
     * Mark a notification as read.
     * @param id - Notification ID
     */
    markNotificationRead(id: string): void {
      this.ui.toasts = this.ui.toasts.map((n: AppNotification) =>
        n.id === id ? { ...n, read: true } : n,
      );
    },

    /**
     * Mark all notifications as read.
     */
    markAllNotificationsRead(): void {
      this.ui.toasts = this.ui.toasts.map((n: AppNotification) => ({ ...n, read: true }));
    },

    /**
     * Clear all notifications.
     */
    clearNotifications(): void {
      this.ui.toasts = [];
    },

    // ============================================
    // Modal Actions
    // ============================================

    /**
     * Open a modal (pushed to modal stack).
     * @param modal - Modal data (id auto-assigned)
     * @returns Modal ID
     */
    openModal(modal: Omit<AppModalState, 'id'>): string {
      const id = generateId();
      const newModal: AppModalState = {
        ...modal,
        id,
      };

      this.ui.modalStack.push(newModal);
      return id;
    },

    /**
     * Close a specific modal by ID.
     * @param id - Modal ID
     */
    closeModal(id: string): void {
      const index = this.ui.modalStack.findIndex((m: AppModalState) => m.id === id);
      if (index !== -1) {
        this.ui.modalStack.splice(index, 1);
      }
    },

    /**
     * Close the topmost modal.
     */
    closeTopModal(): void {
      if (this.ui.modalStack.length > 0) {
        const topModal = this.ui.modalStack[this.ui.modalStack.length - 1]!;
        this.closeModal(topModal.id);
      }
    },

    /**
     * Close all modals.
     */
    closeAllModals(): void {
      this.ui.modalStack = [];
    },

    // ============================================
    // Online Status Actions
    // ============================================

    /**
     * Set user online status.
     * @param status - Online status
     */
    setOnlineStatus(status: OnlineStatus): void {
      this.onlineStatus = status;
    },

    // ============================================
    // Connection Actions
    // ============================================

    /**
     * Update connection state (partial merge).
     * @param state - Partial connection state
     */
    updateConnection(state: Partial<AppState['connection']>): void {
      this.connection = { ...this.connection, ...state };
    },

    /**
     * Set connected state.
     * @param connected - Whether connected
     */
    setConnected(connected: boolean): void {
      this.connection.isConnected = connected;
      if (connected) {
        this.connection.lastConnectedAt = new Date();
        this.connection.error = null;
      }
    },

    // ============================================
    // Initialization Actions
    // ============================================

    /**
     * Mark app as initialized successfully.
     */
    setInitialized(): void {
      this.initialized = true;
      this.initError = null;
    },

    /**
     * Set initialization error.
     * @param error - Error message or null to clear
     */
    setInitError(error: string | null): void {
      this.initError = error;
      this.initialized = !error;
    },

    // ============================================
    // Persistence Actions
    // ============================================

    /**
     * Load persisted app state from storage.
     */
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

      // Apply persisted theme and language
      this.applyTheme(this.theme);
      this.applyLanguage(this.language);
    },

    /**
     * Persist app state to storage.
     */
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

    // ============================================
    // Theme & Language Application
    // ============================================

    /**
     * Apply theme to document root element.
     * @param theme - Theme mode to apply
     */
    applyTheme(theme: ThemeMode): void {
      if (typeof document === 'undefined') return;

      const root = document.documentElement;
      const isDark =
        theme === 'dark' ||
        (theme === 'system' &&
          window.matchMedia('(prefers-color-scheme: dark)').matches);

      root.classList.toggle('dark', isDark);
    },

    /**
     * Apply language to document lang attribute.
     * @param language - Language locale
     */
    applyLanguage(language: Locale): void {
      if (typeof document === 'undefined') return;
      document.documentElement.lang = language;
    },
  },
});

// ============================================
// Composable Wrapper
// ============================================

/**
 * Vue composable for using app store.
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
    initError: computed(() => store.initializationError),

    // UI State
    sidebarCollapsed: computed(() => store.sidebarCollapsed),
    sidebarMode: computed(() => store.sidebarMode),
    isFullscreen: computed(() => store.isFullscreen),
    isLoading: computed(() => store.isLoading),
    loadingMessage: computed(() => store.loadingMessage),

    // Notifications
    notifications: computed(() => store.$state.ui.toasts),
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
