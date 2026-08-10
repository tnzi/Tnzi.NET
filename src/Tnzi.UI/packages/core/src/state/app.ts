/**
 * @tnzi/core/state/app
 *
 * Application state manager - pure logic layer.
 */

import { reactive } from 'vue';
import type {
  AppState,
  AppNotification,
  AppModalState,
  OnlineStatus,
  ConnectionState,
} from './types/app';
import type { ThemeMode } from '../types/theme';
import { THEME_MODES, normalizeThemeMode } from '../types/theme';
import type { Locale } from '../adapters/i18n/types';
import type { StateDeps } from './types/deps';
import { generateId } from '../utils/id';

// ============================================
// Initial state
// ============================================

export function createInitialAppState(): AppState {
  return {
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
  };
}

// ============================================
// Storage key constants
// ============================================

const STORAGE_KEY_THEME = 'tnzi:app:theme';
const STORAGE_KEY_LANGUAGE = 'tnzi:app:language';
const STORAGE_KEY_SIDEBAR = 'tnzi:app:sidebar';

// ============================================
// AppStateManager
// ============================================

export class AppStateManager {
  // State
  ui = createInitialAppState().ui;
  theme: ThemeMode = 'light';
  language: Locale = 'zh-CN';
  onlineStatus: OnlineStatus = 'online';
  connection: ConnectionState = {
    isConnected: true,
    webSocketConnected: false,
    lastConnectedAt: null,
    error: null,
  };
  version = '';
  environment = 'development';
  initialized = false;
  initError: string | null = null;

  /** Track notification auto-dismiss timers for proper cleanup */
  private _notificationTimers = new Map<string, ReturnType<typeof setTimeout>>();
  /** Track fullscreenchange listener for cleanup */
  private _fullscreenCleanup: (() => void) | null = null;

  constructor(private readonly deps: StateDeps) {
    const instance = reactive(this) as this;

    // Listen for fullscreen changes (e.g., user presses ESC)
    if (typeof document !== 'undefined') {
      const handler = () => {
        instance.ui.fullscreen = !!document.fullscreenElement;
      };
      document.addEventListener('fullscreenchange', handler);
      instance._fullscreenCleanup = () => document.removeEventListener('fullscreenchange', handler);
    }

    return instance;
  }

  // ============================================
  // Getters
  // ============================================

  get isDarkMode(): boolean {
    return (
      this.theme === 'dark' ||
      (this.theme === 'auto' && (this.deps.theme?.getResolvedTheme() === 'dark'))
    );
  }

  get sidebarCollapsed(): boolean {
    return this.ui.sidebarCollapsed;
  }

  get sidebarMode(): AppState['ui']['sidebarMode'] {
    return this.ui.sidebarMode;
  }

  get isFullscreen(): boolean {
    return this.ui.fullscreen;
  }

  get isLoading(): boolean {
    return this.ui.loading;
  }

  get loadingMessage(): string | null {
    return this.ui.loadingMessage;
  }

  get unreadNotificationsCount(): number {
    return this.ui.toasts.filter(n => !n.read).length;
  }

  get notificationsCount(): number {
    return this.ui.toasts.length;
  }

  get hasOpenModal(): boolean {
    return this.ui.modalStack.length > 0;
  }

  get topModalId(): string | null {
    const stack = this.ui.modalStack;
    return stack.length > 0 ? stack[stack.length - 1]!.id : null;
  }

  get modalCount(): number {
    return this.ui.modalStack.length;
  }

  get isConnected(): boolean {
    return this.connection.isConnected;
  }

  // ============================================
  // Theme & Language
  // ============================================

  setTheme(theme: ThemeMode): void {
    this.theme = theme;
    this.deps.theme?.applyTheme(theme);
    this.deps.storage.set(STORAGE_KEY_THEME, theme);
  }

  toggleTheme(): void {
    const currentIndex = THEME_MODES.indexOf(this.theme);
    const nextIndex = (currentIndex + 1) % THEME_MODES.length;
    this.setTheme(THEME_MODES[nextIndex]!);
  }

  setLanguage(language: Locale): void {
    this.language = language;
    if (typeof document !== 'undefined') {
      document.documentElement.lang = language;
    }
    this.deps.storage.set(STORAGE_KEY_LANGUAGE, language);
  }

  // ============================================
  // UI state
  // ============================================

  toggleSidebar(): void {
    this.ui.sidebarCollapsed = !this.ui.sidebarCollapsed;
    this.deps.storage.set(STORAGE_KEY_SIDEBAR, this.ui.sidebarCollapsed);
  }

  setSidebarCollapsed(collapsed: boolean): void {
    this.ui.sidebarCollapsed = collapsed;
    this.deps.storage.set(STORAGE_KEY_SIDEBAR, collapsed);
  }

  setSidebarMode(mode: AppState['ui']['sidebarMode']): void {
    this.ui.sidebarMode = mode;
  }

  toggleFullscreen(): void {
    if (typeof document === 'undefined') return;

    if (!this.ui.fullscreen) {
      document.documentElement.requestFullscreen?.();
    } else {
      document.exitFullscreen?.();
    }
    // Note: ui.fullscreen is updated by the fullscreenchange event listener
  }

  showLoading(message?: string): void {
    this.ui.loading = true;
    this.ui.loadingMessage = message ?? null;
  }

  hideLoading(): void {
    this.ui.loading = false;
    this.ui.loadingMessage = null;
  }

  setLoading(loading: boolean, message?: string | null): void {
    this.ui.loading = loading;
    this.ui.loadingMessage = message ?? null;
  }

  // ============================================
  // Notifications
  // ============================================

  addNotification(notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string {
    const id = generateId();
    const newNotification: AppNotification = {
      ...notification,
      id,
      timestamp: new Date(),
      read: false,
    };

    this.ui.toasts = [newNotification, ...this.ui.toasts];

    if (notification.duration > 0) {
      const timerId = setTimeout(() => {
        this._notificationTimers.delete(id);
        this.removeNotification(id);
      }, notification.duration);
      this._notificationTimers.set(id, timerId);
    }

    return id;
  }

  showSuccess(message: string, title?: string): string {
    return this.addNotification({ type: 'success', severity: 'low', message, title, duration: 5000 });
  }

  showError(message: string, title?: string): string {
    return this.addNotification({ type: 'error', severity: 'high', message, title: title ?? 'Error', duration: 0 });
  }

  showWarning(message: string, title?: string): string {
    return this.addNotification({ type: 'warning', severity: 'medium', message, title, duration: 8000 });
  }

  showInfo(message: string, title?: string): string {
    return this.addNotification({ type: 'info', severity: 'low', message, title, duration: 5000 });
  }

  removeNotification(id: string): void {
    // Clear auto-dismiss timer if exists
    const timerId = this._notificationTimers.get(id);
    if (timerId) {
      clearTimeout(timerId);
      this._notificationTimers.delete(id);
    }

    const notification = this.ui.toasts.find(n => n.id === id);
    notification?.onDismiss?.();
    this.ui.toasts = this.ui.toasts.filter(n => n.id !== id);
  }

  markNotificationRead(id: string): void {
    this.ui.toasts = this.ui.toasts.map(n =>
      n.id === id ? { ...n, read: true } : n
    );
  }

  markAllNotificationsRead(): void {
    this.ui.toasts = this.ui.toasts.map(n => ({ ...n, read: true }));
  }

  clearNotifications(): void {
    // Clear all auto-dismiss timers
    for (const timerId of this._notificationTimers.values()) {
      clearTimeout(timerId);
    }
    this._notificationTimers.clear();
    this.ui.toasts = [];
  }

  // ============================================
  // Modals
  // ============================================

  openModal(modal: Omit<AppModalState, 'id'>): string {
    const id = generateId();
    this.ui.modalStack.push({ ...modal, id });
    return id;
  }

  closeModal(id: string): void {
    const index = this.ui.modalStack.findIndex(m => m.id === id);
    if (index !== -1) this.ui.modalStack.splice(index, 1);
  }

  closeTopModal(): void {
    if (this.ui.modalStack.length > 0) {
      this.ui.modalStack.pop();
    }
  }

  closeAllModals(): void {
    this.ui.modalStack = [];
  }

  // ============================================
  // Connection state
  // ============================================

  setOnlineStatus(status: OnlineStatus): void {
    this.onlineStatus = status;
  }

  updateConnection(state: Partial<ConnectionState>): void {
    Object.assign(this.connection, state);
  }

  setConnected(connected: boolean): void {
    this.connection.isConnected = connected;
    if (connected) {
      this.connection.lastConnectedAt = new Date();
      this.connection.error = null;
    }
  }

  // ============================================
  // Initialization
  // ============================================

  setInitialized(): void {
    this.initialized = true;
    this.initError = null;
  }

  setInitError(error: string | null): void {
    this.initError = error;
    this.initialized = !error;
  }

  // ============================================
  // Persistence
  // ============================================

  loadPersistedState(): void {
    const storedTheme = this.deps.storage.get<unknown>(STORAGE_KEY_THEME);
    const language = this.deps.storage.get<Locale>(STORAGE_KEY_LANGUAGE);
    const sidebarCollapsed = this.deps.storage.get<boolean>(STORAGE_KEY_SIDEBAR);

    // Storage is untrusted: it may hold the legacy 'system' spelling written by
    // an older build, or junk from a hand-edited devtools session. Coercing here
    // (rather than casting) keeps `theme` inside its declared union - an
    // out-of-union value would make `isDarkMode` silently answer "light" forever.
    if (storedTheme != null) {
      const theme = normalizeThemeMode(storedTheme, this.theme);
      this.theme = theme;
      this.deps.theme?.applyTheme(theme);
    }
    if (language) {
      this.language = language;
    }
    if (sidebarCollapsed !== undefined && sidebarCollapsed !== null) {
      this.ui.sidebarCollapsed = sidebarCollapsed;
    }
  }

  persistState(): void {
    this.deps.storage.set(STORAGE_KEY_THEME, this.theme);
    this.deps.storage.set(STORAGE_KEY_LANGUAGE, this.language);
    this.deps.storage.set(STORAGE_KEY_SIDEBAR, this.ui.sidebarCollapsed);
  }

  // ============================================
  // Cleanup
  // ============================================

  /**
   * Dispose resources. Call when the app is being unmounted.
   */
  dispose(): void {
    this.clearNotifications();
    this.closeAllModals();
    this._fullscreenCleanup?.();
    this._fullscreenCleanup = null;
  }
}
