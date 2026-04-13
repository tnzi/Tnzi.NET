/**
 * @tnzi/core/state/types/app
 *
 * Application state type definitions.
 */

import type { ThemeMode } from '../../types/theme';
import type { Locale } from '../../adapters/i18n/types';

// ============================================
// Notification Types
// ============================================

/**
 * Notification type enum.
 */
export type NotificationType = 'success' | 'error' | 'warning' | 'info';

/**
 * Notification severity level.
 */
export type NotificationSeverity = 'low' | 'medium' | 'high' | 'critical';

/**
 * Application notification.
 */
export interface AppNotification {
  /** Unique notification ID */
  id: string;
  /** Notification type */
  type: NotificationType;
  /** Notification severity */
  severity: NotificationSeverity;
  /** Notification title */
  title?: string;
  /** Notification message */
  message: string;
  /** Duration in milliseconds (0 = persistent) */
  duration: number;
  /** Action button label */
  actionLabel?: string;
  /** Action callback */
  action?: () => void;
  /** Dismiss callback */
  onDismiss?: () => void;
  /** Timestamp */
  timestamp: Date;
  /** Whether notification has been read */
  read: boolean;
  /** Metadata */
  metadata?: Record<string, unknown>;
}

// ============================================
// Online Status Types
// ============================================

/**
 * User online status.
 */
export type OnlineStatus = 'online' | 'away' | 'busy' | 'offline';

/**
 * Connection state.
 */
export interface ConnectionState {
  /** Whether connected to server */
  isConnected: boolean;
  /** WebSocket connection status */
  webSocketConnected: boolean;
  /** Last connection timestamp */
  lastConnectedAt: Date | null;
  /** Connection error message */
  error: string | null;
}

// ============================================
// UI State Types
// ============================================

/**
 * UI layout state.
 */
export interface AppUiState {
  /** Sidebar collapsed state */
  sidebarCollapsed: boolean;
  /** Sidebar mode (responsive, pinned, mini) */
  sidebarMode: 'responsive' | 'pinned' | 'mini';
  /** Header visible state */
  headerVisible: boolean;
  /** Footer visible state */
  footerVisible: boolean;
  /** Fullscreen mode */
  fullscreen: boolean;
  /** Loading overlay visible */
  loading: boolean;
  /** Loading message */
  loadingMessage: string | null;
  /** Modal stack */
  modalStack: AppModalState[];
  /** Toast queue */
  toasts: AppNotification[];
}

/**
 * Modal state for dialog management.
 * Renamed from ModalState to avoid conflict with stores.ts:ModalState
 */
export interface AppModalState {
  /** Modal identifier */
  id: string;
  /** Modal component name */
  component: string;
  /** Modal props */
  props?: Record<string, unknown>;
  /** Modal options */
  options?: AppModalOptions;
}

/**
 * Modal options.
 */
export interface AppModalOptions {
  /** Modal title */
  title?: string;
  /** Modal width */
  width?: string | number;
  /** Modal height */
  height?: string | number;
  /** Whether modal can be closed by backdrop click */
  closable?: boolean;
  /** Whether to show close button */
  showClose?: boolean;
  /** Z-index level */
  zIndex?: number;
  /** Custom class name */
  className?: string;
}

// ============================================
// App State Types
// ============================================

/**
 * Application state interface.
 */
export interface AppState {
  /** UI state */
  ui: AppUiState;
  /** Theme mode */
  theme: ThemeMode;
  /** Interface language */
  language: Locale;
  /** User online status */
  onlineStatus: OnlineStatus;
  /** Connection state */
  connection: ConnectionState;
  /** Application version */
  version: string;
  /** Environment (development, staging, production) */
  environment: string;
  /** Whether app is initialized */
  initialized: boolean;
  /** Initialization error */
  initError: string | null;
}

// ============================================
// App Store Actions Interface
// ============================================

/**
 * App store actions interface.
 */
export interface AppStoreActions {
  // Theme
  /**
   * Set application theme.
   * @param theme - Theme mode
   */
  setTheme(theme: ThemeMode): void;

  /**
   * Toggle between light and dark mode.
   */
  toggleTheme(): void;

  // Language
  /**
   * Set interface language.
   * @param language - Language locale
   */
  setLanguage(language: Locale): void;

  // UI State
  /**
   * Toggle sidebar collapsed state.
   */
  toggleSidebar(): void;

  /**
   * Set sidebar collapsed state.
   * @param collapsed - Whether sidebar is collapsed
   */
  setSidebarCollapsed(collapsed: boolean): void;

  /**
   * Set sidebar mode.
   * @param mode - Sidebar mode
   */
  setSidebarMode(mode: AppUiState['sidebarMode']): void;

  /**
   * Toggle fullscreen mode.
   */
  toggleFullscreen(): void;

  /**
   * Show loading overlay.
   * @param message - Loading message
   */
  showLoading(message?: string): void;

  /**
   * Hide loading overlay.
   */
  hideLoading(): void;

  /**
   * Set loading state.
   * @param loading - Loading state
   * @param message - Loading message
   */
  setLoading(loading: boolean, message?: string | null): void;

  // Notifications
  /**
   * Add a notification.
   * @param notification - Notification to add
   */
  addNotification(notification: Omit<AppNotification, 'id' | 'timestamp' | 'read'>): string;

  /**
   * Show success notification.
   * @param message - Success message
   * @param title - Optional title
   */
  showSuccess(message: string, title?: string): string;

  /**
   * Show error notification.
   * @param message - Error message
   * @param title - Optional title
   */
  showError(message: string, title?: string): string;

  /**
   * Show warning notification.
   * @param message - Warning message
   * @param title - Optional title
   */
  showWarning(message: string, title?: string): string;

  /**
   * Show info notification.
   * @param message - Info message
   * @param title - Optional title
   */
  showInfo(message: string, title?: string): string;

  /**
   * Remove a notification.
   * @param id - Notification ID
   */
  removeNotification(id: string): void;

  /**
   * Mark notification as read.
   * @param id - Notification ID
   */
  markNotificationRead(id: string): void;

  /**
   * Mark all notifications as read.
   */
  markAllNotificationsRead(): void;

  /**
   * Clear all notifications.
   */
  clearNotifications(): void;

  // Modals
  /**
   * Open a modal.
   * @param modal - Modal state
   */
  openModal(modal: Omit<AppModalState, 'id'>): string;

  /**
   * Close a modal.
   * @param id - Modal ID
   */
  closeModal(id: string): void;

  /**
   * Close the topmost modal.
   */
  closeTopModal(): void;

  /**
   * Close all modals.
   */
  closeAllModals(): void;

  // Online Status
  /**
   * Set online status.
   * @param status - Online status
   */
  setOnlineStatus(status: OnlineStatus): void;

  // Connection
  /**
   * Update connection state.
   * @param state - Partial connection state
   */
  updateConnection(state: Partial<ConnectionState>): void;

  /**
   * Set connected state.
   * @param connected - Whether connected
   */
  setConnected(connected: boolean): void;

  // Initialization
  /**
   * Mark app as initialized.
   */
  setInitialized(): void;

  /**
   * Set initialization error.
   * @param error - Error message
   */
  setInitError(error: string | null): void;

  // Persistence
  /**
   * Load persisted app state.
   */
  loadPersistedState(): Promise<void>;

  /**
   * Persist app state.
   */
  persistState(): Promise<void>;
}

// ============================================
// App Store Getters Interface
// ============================================

/**
 * App store getters interface.
 */
export interface AppStoreGetters {
  /** Current theme mode */
  theme: ThemeMode;
  /** Whether dark mode is active */
  isDarkMode: boolean;
  /** Current language */
  language: Locale;
  /** Whether sidebar is collapsed */
  sidebarCollapsed: boolean;
  /** Current sidebar mode */
  sidebarMode: AppUiState['sidebarMode'];
  /** Whether fullscreen mode is active */
  isFullscreen: boolean;
  /** Whether loading overlay is visible */
  isLoading: boolean;
  /** Loading message */
  loadingMessage: string | null;
  /** Unread notifications count */
  unreadNotificationsCount: number;
  /** Total notifications count */
  notificationsCount: number;
  /** Whether any modal is open */
  hasOpenModal: boolean;
  /** Topmost modal ID */
  topModalId: string | null;
  /** Modal count */
  modalCount: number;
  /** Online status */
  onlineStatus: OnlineStatus;
  /** Whether connected */
  isConnected: boolean;
  /** Whether app is initialized */
  initialized: boolean;
  /** Initialization error */
  initError: string | null;
}

// ============================================
// Combined App Store Interface
// ============================================

/**
 * Complete app store interface.
 */
export interface AppStore extends AppState, AppStoreGetters, AppStoreActions {
  /** Store identifier */
  readonly $id: 'app';
}

// ============================================
// App Store Options
// ============================================

/**
 * Default app state values.
 */
export const defaultAppState: Partial<AppState> = {
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

/**
 * App store factory options.
 */
export interface AppStoreOptions {
  /** App state factory */
  state: () => AppState;
  /** App getters */
  getters: AppStoreGetters;
  /** App actions */
  actions: AppStoreActions;
}

// ============================================
// Legacy State Types (from types/stores.ts)
// ============================================

/**
 * Notification/Toast state
 */
export interface NotificationState {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  title?: string;
  message: string;
  duration?: number;
  timestamp: Date;
}

/**
 * Modal state
 */
export interface ModalState<T = unknown> {
  isOpen: boolean;
  data: T | null;
}
