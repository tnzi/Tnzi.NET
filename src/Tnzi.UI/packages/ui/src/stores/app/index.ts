/**
 * @tnzi/ui/stores/app
 *
 * Application state store — thin Pinia wrapper delegating to core
 * AppStateManager. All UI state logic (theme, language, notifications, modals,
 * connection, persistence) lives in AppStateManager; this store only proxies
 * reactive state. The manager also wires the `fullscreenchange` listener in its
 * constructor, so no separate init step is required.
 */

import { computed } from 'vue';
import { defineStore } from 'pinia';
import { AppStateManager } from '@tnzi/core/state';
import type {
  StateDeps,
  AppState,
  AppNotification,
  AppModalState,
  OnlineStatus,
  ConnectionState,
} from '@tnzi/core/state';
import { createLocalStorageAdapter } from '@tnzi/core/adapters/storage';
import type { Locale } from '@tnzi/core/adapters/i18n';
import type { ThemeMode } from '@tnzi/core/types';
import { getStoreHttpClient, getStoreStorage } from '../factory';
import { createThemeAdapter } from '../../adapters/theme';

// ============================================
// AppStateManager Singleton
// ============================================

let _manager: AppStateManager | null = null;

function getManager(): AppStateManager {
  if (!_manager) {
    const deps: StateDeps = {
      httpClient: getStoreHttpClient(),
      storage: getStoreStorage() ?? createLocalStorageAdapter(),
      theme: createThemeAdapter(),
    };
    _manager = new AppStateManager(deps);
  }
  return _manager;
}

// ============================================
// App Store Definition
// ============================================

export const useAppStore = defineStore('app', () => {
  const m = computed(() => getManager());

  // --- Reactive state (proxied from manager) ---
  const ui = computed(() => m.value.ui);
  const theme = computed(() => m.value.theme);
  const language = computed(() => m.value.language);
  const onlineStatus = computed(() => m.value.onlineStatus);
  const connection = computed(() => m.value.connection);
  const initialized = computed(() => m.value.initialized);
  const initError = computed(() => m.value.initError);

  // --- Getters ---
  const currentTheme = computed(() => m.value.theme);
  const isDarkMode = computed(() => m.value.isDarkMode);
  const currentLanguage = computed(() => m.value.language);
  const sidebarCollapsed = computed(() => m.value.sidebarCollapsed);
  const sidebarMode = computed(() => m.value.sidebarMode);
  const isFullscreen = computed(() => m.value.isFullscreen);
  const isLoading = computed(() => m.value.isLoading);
  const loadingMessage = computed(() => m.value.loadingMessage);
  const unreadNotificationsCount = computed(() => m.value.unreadNotificationsCount);
  const notificationsCount = computed(() => m.value.notificationsCount);
  const hasOpenModal = computed(() => m.value.hasOpenModal);
  const topModalId = computed(() => m.value.topModalId);
  const modalCount = computed(() => m.value.modalCount);
  const currentOnlineStatus = computed(() => m.value.onlineStatus);
  const isConnected = computed(() => m.value.isConnected);
  const isInitialized = computed(() => m.value.initialized);
  const currentInitError = computed(() => m.value.initError);

  // --- Theme actions ---
  function setTheme(t: ThemeMode): void { getManager().setTheme(t); }
  function toggleTheme(): void { getManager().toggleTheme(); }

  // --- Language actions ---
  function setLanguage(lang: Locale): void { getManager().setLanguage(lang); }

  // --- Sidebar actions ---
  function toggleSidebar(): void { getManager().toggleSidebar(); }
  function setSidebarCollapsed(collapsed: boolean): void { getManager().setSidebarCollapsed(collapsed); }
  function setSidebarMode(mode: AppState['ui']['sidebarMode']): void { getManager().setSidebarMode(mode); }

  // --- Fullscreen actions ---
  function toggleFullscreen(): void { getManager().toggleFullscreen(); }

  // --- Loading actions ---
  function showLoading(message?: string): void { getManager().showLoading(message); }
  function hideLoading(): void { getManager().hideLoading(); }
  function setLoading(loading: boolean, message?: string | null): void { getManager().setLoading(loading, message); }

  // --- Notification actions ---
  function addNotification(notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string {
    return getManager().addNotification(notification);
  }
  function showSuccess(message: string, title?: string): string { return getManager().showSuccess(message, title); }
  function showError(message: string, title?: string): string { return getManager().showError(message, title); }
  function showWarning(message: string, title?: string): string { return getManager().showWarning(message, title); }
  function showInfo(message: string, title?: string): string { return getManager().showInfo(message, title); }
  function removeNotification(id: string): void { getManager().removeNotification(id); }
  function markNotificationRead(id: string): void { getManager().markNotificationRead(id); }
  function markAllNotificationsRead(): void { getManager().markAllNotificationsRead(); }
  function clearNotifications(): void { getManager().clearNotifications(); }

  // --- Modal actions ---
  function openModal(modal: Omit<AppModalState, 'id'>): string { return getManager().openModal(modal); }
  function closeModal(id: string): void { getManager().closeModal(id); }
  function closeTopModal(): void { getManager().closeTopModal(); }
  function closeAllModals(): void { getManager().closeAllModals(); }

  // --- Online status actions ---
  function setOnlineStatus(status: OnlineStatus): void { getManager().setOnlineStatus(status); }

  // --- Connection actions ---
  function updateConnection(state: Partial<ConnectionState>): void { getManager().updateConnection(state); }
  function setConnected(connected: boolean): void { getManager().setConnected(connected); }

  // --- Initialization actions ---
  function setInitialized(): void { getManager().setInitialized(); }
  function setInitError(error: string | null): void { getManager().setInitError(error); }

  // --- Persistence actions ---
  async function loadPersistedState(): Promise<void> { getManager().loadPersistedState(); }
  function persistState(): void { getManager().persistState(); }

  return {
    // State
    ui, theme, language, onlineStatus, connection, initialized, initError,
    // Getters
    currentTheme, isDarkMode, currentLanguage, sidebarCollapsed, sidebarMode,
    isFullscreen, isLoading, loadingMessage, unreadNotificationsCount,
    notificationsCount, hasOpenModal, topModalId, modalCount,
    currentOnlineStatus, isConnected, isInitialized, currentInitError,
    // Theme actions
    setTheme, toggleTheme,
    // Language actions
    setLanguage,
    // Sidebar actions
    toggleSidebar, setSidebarCollapsed, setSidebarMode,
    // Fullscreen actions
    toggleFullscreen,
    // Loading actions
    showLoading, hideLoading, setLoading,
    // Notification actions
    addNotification, showSuccess, showError, showWarning, showInfo,
    removeNotification, markNotificationRead, markAllNotificationsRead, clearNotifications,
    // Modal actions
    openModal, closeModal, closeTopModal, closeAllModals,
    // Online status actions
    setOnlineStatus,
    // Connection actions
    updateConnection, setConnected,
    // Initialization actions
    setInitialized, setInitError,
    // Persistence actions
    loadPersistedState, persistState,
  };
});

// ============================================
// Runtime Reset
// ============================================

/**
 * Reset app runtime to uninitialized state.
 * Disposes and nulls the internal AppStateManager singleton.
 * Useful for SSR isolation or test teardown.
 */
export function resetAppRuntime(): void {
  _manager?.dispose();
  _manager = null;
}

// ============================================
// Composable Wrapper
// ============================================

/**
 * Vue composable for using the app store.
 * Provides reactive computed refs and bound action methods.
 */
export function useApp() {
  const store = useAppStore();

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
