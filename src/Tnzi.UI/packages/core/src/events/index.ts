/**
 * @tnzi/core/events
 *
 * Common event definitions for cross-module communication.
 * Applications should use these event names for consistency.
 */

// ============================================
// Authentication Events
// ============================================

/**
 * Authentication-related event names.
 */
export const AUTH_EVENTS = {
  /** User successfully logged in */
  LOGIN: 'auth:login',

  /** User logged out */
  LOGOUT: 'auth:logout',

  /** Access token refreshed */
  TOKEN_REFRESHED: 'auth:tokenRefreshed',

  /** Token refresh failed */
  TOKEN_REFRESH_FAILED: 'auth:tokenRefreshFailed',

  /** Session expired */
  SESSION_EXPIRED: 'auth:sessionExpired',

  /** User profile updated */
  PROFILE_UPDATED: 'auth:profileUpdated',

  /** User password changed */
  PASSWORD_CHANGED: 'auth:passwordChanged',
} as const;

/** Authentication event payload types */
export interface AuthLoginPayload {
  userId: string;
  userName: string;
  timestamp: Date;
}

export interface AuthLogoutPayload {
  userId: string;
  timestamp: Date;
}

export interface AuthTokenRefreshedPayload {
  accessToken: string;
  expiresAt: Date;
}

export interface AuthTokenRefreshFailedPayload {
  error: string;
  timestamp: Date;
}

export interface AuthProfileUpdatedPayload {
  userId: string;
  changes: Record<string, unknown>;
}

// ============================================
// User Events
// ============================================

/**
 * User preference and profile event names.
 */
export const USER_EVENTS = {
  /** User preferences changed */
  PREFERENCES_CHANGED: 'user:preferencesChanged',

  /** Theme changed */
  THEME_CHANGED: 'user:themeChanged',

  /** Language changed */
  LANGUAGE_CHANGED: 'user:languageChanged',

  /** User online status changed */
  ONLINE_STATUS_CHANGED: 'user:onlineStatusChanged',
} as const;

/** User event payload types */
export interface UserPreferencesChangedPayload {
  changes: Record<string, unknown>;
}

export interface UserThemeChangedPayload {
  theme: 'light' | 'dark' | 'system';
}

export interface UserLanguageChangedPayload {
  language: string;
}

export interface UserOnlineStatusChangedPayload {
  status: 'online' | 'away' | 'busy' | 'offline';
}

// ============================================
// Application Events
// ============================================

/**
 * Application lifecycle event names.
 */
export const APP_EVENTS = {
  /** Application initialized */
  INITIALIZED: 'app:initialized',

  /** Application ready */
  READY: 'app:ready',

  /** Application error occurred */
  ERROR: 'app:error',

  /** Application route changed */
  ROUTE_CHANGED: 'app:routeChanged',

  /** Application visibility changed */
  VISIBILITY_CHANGED: 'app:visibilityChanged',
} as const;

/** Application event payload types */
export interface AppErrorPayload {
  error: Error;
  context?: string;
  fatal: boolean;
}

export interface AppRouteChangedPayload {
  from: string;
  to: string;
}

export interface AppVisibilityChangedPayload {
  visible: boolean;
}

// ============================================
// Notification Events
// ============================================

/**
 * Notification event names.
 */
export const NOTIFICATION_EVENTS = {
  /** Notification shown */
  SHOWN: 'notification:shown',

  /** Notification dismissed */
  DISMISSED: 'notification:dismissed',

  /** Notification clicked */
  CLICKED: 'notification:clicked',

  /** All notifications cleared */
  CLEARED: 'notification:cleared',
} as const;

/** Notification event payload types */
export interface NotificationPayload {
  id: string;
  type: 'success' | 'error' | 'warning' | 'info';
  message: string;
  title?: string;
}

// ============================================
// Modal Events
// ============================================

/**
 * Modal event names.
 */
export const MODAL_EVENTS = {
  /** Modal opened */
  OPENED: 'modal:opened',

  /** Modal closed */
  CLOSED: 'modal:closed',

  /** Modal confirmed */
  CONFIRMED: 'modal:confirmed',

  /** Modal cancelled */
  CANCELLED: 'modal:cancelled',

  /** All modals closed */
  ALL_CLOSED: 'modal:allClosed',
} as const;

/** Modal event payload types */
export interface ModalPayload {
  id: string;
  component?: string;
}

// ============================================
// Network Events
// ============================================

/**
 * Network event names.
 */
export const NETWORK_EVENTS = {
  /** Connection status changed */
  STATUS_CHANGED: 'network:statusChanged',

  /** Request started */
  REQUEST_START: 'network:requestStart',

  /** Request completed */
  REQUEST_COMPLETE: 'network:requestComplete',

  /** Request failed */
  REQUEST_ERROR: 'network:requestError',
} as const;

/** Network event payload types */
export interface NetworkStatusChangedPayload {
  online: boolean;
  connectionType?: string;
}

export interface NetworkRequestPayload {
  url: string;
  method: string;
  requestId: string;
}

export interface NetworkRequestErrorPayload extends NetworkRequestPayload {
  error: Error;
  status?: number;
}

// ============================================
// Data Events
// ============================================

/**
 * Data synchronization event names.
 */
export const DATA_EVENTS = {
  /** Data loaded */
  LOADED: 'data:loaded',

  /** Data updated */
  UPDATED: 'data:updated',

  /** Data deleted */
  DELETED: 'data:deleted',

  /** Data sync started */
  SYNC_START: 'data:syncStart',

  /** Data sync completed */
  SYNC_COMPLETE: 'data:syncComplete',

  /** Data sync failed */
  SYNC_ERROR: 'data:syncError',
} as const;

/** Data event payload types */
export interface DataPayload {
  type: string;
  id: string;
  data?: unknown;
}

export interface DataSyncPayload {
  type: string;
  items: number;
}

// ============================================
// Event Type Helper
// ============================================

/**
 * All event names as a union type.
 */
export type EventName =
  | (typeof AUTH_EVENTS)[keyof typeof AUTH_EVENTS]
  | (typeof USER_EVENTS)[keyof typeof USER_EVENTS]
  | (typeof APP_EVENTS)[keyof typeof APP_EVENTS]
  | (typeof NOTIFICATION_EVENTS)[keyof typeof NOTIFICATION_EVENTS]
  | (typeof MODAL_EVENTS)[keyof typeof MODAL_EVENTS]
  | (typeof NETWORK_EVENTS)[keyof typeof NETWORK_EVENTS]
  | (typeof DATA_EVENTS)[keyof typeof DATA_EVENTS];

/**
 * Event payload type mapping.
 */
export type EventPayloadMap = {
  [AUTH_EVENTS.LOGIN]: AuthLoginPayload;
  [AUTH_EVENTS.LOGOUT]: AuthLogoutPayload;
  [AUTH_EVENTS.TOKEN_REFRESHED]: AuthTokenRefreshedPayload;
  [AUTH_EVENTS.TOKEN_REFRESH_FAILED]: AuthTokenRefreshFailedPayload;
  [AUTH_EVENTS.PROFILE_UPDATED]: AuthProfileUpdatedPayload;
  [AUTH_EVENTS.PASSWORD_CHANGED]: Record<string, never>;

  [USER_EVENTS.PREFERENCES_CHANGED]: UserPreferencesChangedPayload;
  [USER_EVENTS.THEME_CHANGED]: UserThemeChangedPayload;
  [USER_EVENTS.LANGUAGE_CHANGED]: UserLanguageChangedPayload;
  [USER_EVENTS.ONLINE_STATUS_CHANGED]: UserOnlineStatusChangedPayload;

  [APP_EVENTS.INITIALIZED]: Record<string, never>;
  [APP_EVENTS.READY]: Record<string, never>;
  [APP_EVENTS.ERROR]: AppErrorPayload;
  [APP_EVENTS.ROUTE_CHANGED]: AppRouteChangedPayload;
  [APP_EVENTS.VISIBILITY_CHANGED]: AppVisibilityChangedPayload;

  [NOTIFICATION_EVENTS.SHOWN]: NotificationPayload;
  [NOTIFICATION_EVENTS.DISMISSED]: NotificationPayload;
  [NOTIFICATION_EVENTS.CLICKED]: NotificationPayload;
  [NOTIFICATION_EVENTS.CLEARED]: Record<string, never>;

  [MODAL_EVENTS.OPENED]: ModalPayload;
  [MODAL_EVENTS.CLOSED]: ModalPayload;
  [MODAL_EVENTS.CONFIRMED]: ModalPayload;
  [MODAL_EVENTS.CANCELLED]: ModalPayload;
  [MODAL_EVENTS.ALL_CLOSED]: Record<string, never>;

  [NETWORK_EVENTS.STATUS_CHANGED]: NetworkStatusChangedPayload;
  [NETWORK_EVENTS.REQUEST_START]: NetworkRequestPayload;
  [NETWORK_EVENTS.REQUEST_COMPLETE]: NetworkRequestPayload;
  [NETWORK_EVENTS.REQUEST_ERROR]: NetworkRequestErrorPayload;

  [DATA_EVENTS.LOADED]: DataPayload;
  [DATA_EVENTS.UPDATED]: DataPayload;
  [DATA_EVENTS.DELETED]: DataPayload;
  [DATA_EVENTS.SYNC_START]: DataSyncPayload;
  [DATA_EVENTS.SYNC_COMPLETE]: DataSyncPayload;
  [DATA_EVENTS.SYNC_ERROR]: DataSyncPayload;
};
