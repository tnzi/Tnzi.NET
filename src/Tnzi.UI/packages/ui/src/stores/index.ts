/**
 * @tnzi/ui/stores
 *
 * Pinia store exports and initialization.
 */

// Store composables
export { useAuth, resetAuthRuntime } from './auth';
export { useUser } from './user';
export { useApp } from './app';

// Raw Pinia stores
export { useAuthStore } from './auth';
export { useUserStore } from './user';
export { useAppStore } from './app';

// Store initialization (module-level API)
export {
  initStoreRuntime,
  getStoreHttpClient,
  getStoreStorage,
  setStoreHttpClient,
  setStoreStorageAdapter,
  resetStoreRuntime,
} from './factory';

// Store DI (SSR-safe provide/inject)
export {
  STORE_HTTP_CLIENT,
  STORE_STORAGE,
  provideStoreRuntime,
  useStoreHttpClient,
  useStoreStorage,
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

export type { ThemeMode } from '@tnzi/core/types/theme';
