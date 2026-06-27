/**
 * @tnzi/mobile/stores
 *
 * Store exports and initialization.
 */

// Store composables
export { useAuth } from './auth';
export { useUser } from './user';
export { useApp } from './app';

// Store definitions (Pinia stores)
export { useAuthStore } from './auth';
export { useUserStore } from './user';
export { useAppStore } from './app';

// Store initialization
export {
  initStoreRuntime,
  getStoreHttpClient,
  getStoreStorage,
  setStoreHttpClient,
  setStoreStorageAdapter,
} from './factory';

// Re-export types from core
export type {
  AuthState,
  AuthStoreActions,
  AuthStoreGetters,
  AuthTokens,
} from '@tnzi/core/state';

export type {
  UserState,
  UserStoreActions,
  UserStoreGetters,
  UserPreferences,
  UserTheme,
  UserLanguage,
  RecentItem,
} from '@tnzi/core/state';

export type {
  AppState,
  AppStoreActions,
  AppStoreGetters,
  AppNotification,
  NotificationType,
  NotificationSeverity,
  AppModalState,
  OnlineStatus,
} from '@tnzi/core/state';

export type { ThemeMode } from '@tnzi/core/types';
