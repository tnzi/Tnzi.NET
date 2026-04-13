/**
 * @tnzi/mobile/stores/app
 *
 * Application state store — thin Pinia wrapper delegating to core AppStateManager.
 * All business logic lives in AppStateManager; this store only proxies reactive state.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
import { AppStateManager } from '@tnzi/core/state';
import type { StateDeps } from '@tnzi/core/state';
import { getStoreStorage, getStoreHttpClient } from '../factory';

// ============================================
// AppStateManager Singleton
// ============================================

let _manager: AppStateManager | null = null;

function getManager(): AppStateManager {
  if (!_manager) {
    // AppStateManager requires StateDeps shape but does not use httpClient internally.
    // httpClient is provided here to satisfy the type contract.
    const deps = {
      httpClient: getStoreHttpClient(),
      storage: getStoreStorage(),
    } as unknown as StateDeps;
    _manager = new AppStateManager(deps);
  }
  return _manager;
}

// ============================================
// App Store Definition
// ============================================

export const useAppStore = defineStore('app', () => {
  // --- State (proxied from manager) ---
  const theme = computed(() => getManager().theme);
  const language = computed(() => getManager().language);
  const onlineStatus = computed(() => getManager().onlineStatus);
  const connection = computed(() => getManager().connection);
  const initialized = computed(() => getManager().initialized);
  const initError = computed(() => getManager().initError);
  const ui = computed(() => getManager().ui);

  // --- Computed getters ---
  const isDarkMode = computed(() => getManager().isDarkMode);
  const sidebarCollapsed = computed(() => getManager().sidebarCollapsed);
  const sidebarMode = computed(() => getManager().sidebarMode);
  const isFullscreen = computed(() => getManager().isFullscreen);
  const isLoading = computed(() => getManager().isLoading);
  const loadingMessage = computed(() => getManager().loadingMessage);
  const unreadNotificationsCount = computed(() => getManager().unreadNotificationsCount);
  const notificationsCount = computed(() => getManager().notificationsCount);
  const hasOpenModal = computed(() => getManager().hasOpenModal);
  const topModalId = computed(() => getManager().topModalId);
  const modalCount = computed(() => getManager().modalCount);
  const isConnected = computed(() => getManager().isConnected);

  return {
    // State
    theme, language, onlineStatus, connection, initialized, initError, ui,
    // Getters
    isDarkMode, sidebarCollapsed, sidebarMode, isFullscreen,
    isLoading, loadingMessage,
    unreadNotificationsCount, notificationsCount,
    hasOpenModal, topModalId, modalCount,
    isConnected,
    // Theme
    setTheme: (t: Parameters<AppStateManager['setTheme']>[0]) => getManager().setTheme(t),
    toggleTheme: () => getManager().toggleTheme(),
    // Language
    setLanguage: (l: Parameters<AppStateManager['setLanguage']>[0]) => getManager().setLanguage(l),
    // Sidebar
    toggleSidebar: () => getManager().toggleSidebar(),
    setSidebarCollapsed: (v: boolean) => getManager().setSidebarCollapsed(v),
    setSidebarMode: (mode: Parameters<AppStateManager['setSidebarMode']>[0]) => getManager().setSidebarMode(mode),
    // Fullscreen
    toggleFullscreen: () => getManager().toggleFullscreen(),
    // Loading
    showLoading: (msg?: string) => getManager().showLoading(msg),
    hideLoading: () => getManager().hideLoading(),
    setLoading: (loading: boolean, msg?: string | null) => getManager().setLoading(loading, msg),
    // Notifications
    addNotification: (n: Parameters<AppStateManager['addNotification']>[0]) => getManager().addNotification(n),
    showSuccess: (msg: string, title?: string) => getManager().showSuccess(msg, title),
    showError: (msg: string, title?: string) => getManager().showError(msg, title),
    showWarning: (msg: string, title?: string) => getManager().showWarning(msg, title),
    showInfo: (msg: string, title?: string) => getManager().showInfo(msg, title),
    removeNotification: (id: string) => getManager().removeNotification(id),
    markNotificationRead: (id: string) => getManager().markNotificationRead(id),
    markAllNotificationsRead: () => getManager().markAllNotificationsRead(),
    clearNotifications: () => getManager().clearNotifications(),
    // Modals
    openModal: (modal: Parameters<AppStateManager['openModal']>[0]) => getManager().openModal(modal),
    closeModal: (id: string) => getManager().closeModal(id),
    closeTopModal: () => getManager().closeTopModal(),
    closeAllModals: () => getManager().closeAllModals(),
    // Online / Connection
    setOnlineStatus: (status: Parameters<AppStateManager['setOnlineStatus']>[0]) => getManager().setOnlineStatus(status),
    updateConnection: (state: Parameters<AppStateManager['updateConnection']>[0]) => getManager().updateConnection(state),
    setConnected: (connected: boolean) => getManager().setConnected(connected),
    // Initialization
    setInitialized: () => getManager().setInitialized(),
    setInitError: (err: string | null) => getManager().setInitError(err),
    // Persistence
    loadPersistedState: () => getManager().loadPersistedState(),
    persistState: () => getManager().persistState(),
  };
});

// ============================================
// Composable Wrapper
// ============================================

export function useApp() {
  const store = useAppStore();

  return {
    // State
    theme: computed(() => store.theme),
    language: computed(() => store.language),
    isDarkMode: computed(() => store.isDarkMode),
    onlineStatus: computed(() => store.onlineStatus),
    isConnected: computed(() => store.isConnected),
    initialized: computed(() => store.initialized),
    initError: computed(() => store.initError),
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
    // Actions
    setTheme: store.setTheme,
    toggleTheme: store.toggleTheme,
    setLanguage: store.setLanguage,
    toggleSidebar: store.toggleSidebar,
    setSidebarCollapsed: store.setSidebarCollapsed,
    setSidebarMode: store.setSidebarMode,
    toggleFullscreen: store.toggleFullscreen,
    showLoading: store.showLoading,
    hideLoading: store.hideLoading,
    setLoading: store.setLoading,
    addNotification: store.addNotification,
    showSuccess: store.showSuccess,
    showError: store.showError,
    showWarning: store.showWarning,
    showInfo: store.showInfo,
    removeNotification: store.removeNotification,
    markNotificationRead: store.markNotificationRead,
    markAllNotificationsRead: store.markAllNotificationsRead,
    clearNotifications: store.clearNotifications,
    openModal: store.openModal,
    closeModal: store.closeModal,
    closeTopModal: store.closeTopModal,
    closeAllModals: store.closeAllModals,
    setOnlineStatus: store.setOnlineStatus,
    updateConnection: store.updateConnection,
    setConnected: store.setConnected,
    setInitialized: store.setInitialized,
    setInitError: store.setInitError,
    loadPersistedState: store.loadPersistedState,
    persistState: store.persistState,
  };
}
