/**
 * @tnzi/core/state
 *
 * 响应式状态管理逻辑层。
 * 基于 @vue/reactivity 实现，UI 包可直接使用或包装为 Pinia store。
 */

// 状态管理器
export { AuthStateManager, createInitialAuthState } from './auth';
export { UserStateManager, createInitialUserState } from './user';
export { AppStateManager, createInitialAppState } from './app';

// 依赖类型
export type { StateDeps } from './types';

// 重新导出所有状态类型（方便从 state 路径导入）
export type {
  // Auth
  AuthState,
  AuthTokens,
  AuthPreferences,
  AuthStoreActions,
  AuthStoreGetters,
  AuthEvents,
  // User
  UserTheme,
  UserLanguage,
  UserNotificationPreferences,
  UserPreferences,
  RecentItemType,
  RecentItem,
  UserState,
  // App
  NotificationType,
  NotificationSeverity,
  AppNotification,
  OnlineStatus,
  ConnectionState,
  AppUiState,
  AppModalState,
  AppModalOptions,
  AppState,
} from './types';

export { defaultUserPreferences, defaultAppState } from './types';
