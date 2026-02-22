/**
 * @tnzi/shadcn/stores
 *
 * Store exports and initialization.
 */

// Store composables
export { useAuth } from './auth';
export { useUser } from './user';
export { useApp } from './app';

// Store factories
export { useAuthStore } from './auth';
export { useUserStore } from './user';
export { useAppStore } from './app';

// Store initialization
export { initStoreRuntime, getStoreRuntime, getStoreHttpClient, getStoreStorage, createStore } from './factory';

// Re-export types from core
export type {
  AuthState,
  AuthStoreActions,
  AuthStoreGetters,
  AuthTokens,
} from '@tnzi/core/stores/auth';

export type {
  UserState,
  UserStoreActions,
  UserStoreGetters,
  UserPreferences,
  UserTheme,
  UserLanguage,
  RecentItem,
} from '@tnzi/core/stores/user';

export type {
  AppState,
  AppStoreActions,
  AppStoreGetters,
  AppNotification,
  NotificationType,
  NotificationSeverity,
  AppModalState,
  OnlineStatus,
} from '@tnzi/core/stores/app';

export type { ThemeMode } from '@tnzi/core/types/theme';
