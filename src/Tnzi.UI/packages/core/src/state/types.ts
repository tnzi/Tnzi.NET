/**
 * @tnzi/core/state - 状态管理类型
 *
 * 从 stores/ 迁移的类型定义 + 新增状态管理器依赖接口。
 */

import type { HttpClient } from '../http/http';
import type { StorageAdapter } from '../adapters/storage';
import type { ThemeAdapter } from '../adapters/theme/index';
import type { RouterAdapter } from '../adapters/router/index';

// 重新导出所有 store 类型（保持向后兼容）
export type {
  AuthState,
  AuthTokens,
  AuthPreferences,
  AuthStoreActions,
  AuthStoreGetters,
  AuthStore,
  AuthStoreOptions,
  AuthEvents,
  AuthEventHandler,
} from '../stores/auth/index';

export type {
  UserTheme,
  UserLanguage,
  UserNotificationPreferences,
  UserPreferences,
  RecentItemType,
  RecentItem,
  UserState,
  UserStoreActions,
  UserStoreGetters,
  UserStore,
  UserStoreOptions,
} from '../stores/user/index';

export { defaultUserPreferences } from '../stores/user/index';

export type {
  NotificationType,
  NotificationSeverity,
  AppNotification,
  OnlineStatus,
  ConnectionState,
  AppUiState,
  AppModalState,
  AppModalOptions,
  AppState,
  AppStoreActions,
  AppStoreGetters,
  AppStore,
  AppStoreOptions,
} from '../stores/app/index';

export { defaultAppState } from '../stores/app/index';

// ============================================
// 状态管理器依赖接口
// ============================================

/**
 * Shared dependencies for all state managers.
 * Each StateManager receives external services through this interface.
 */
export interface StateDeps {
  /** HTTP client */
  httpClient: HttpClient;
  /** Persistent storage adapter */
  storage: StorageAdapter;
  /** Theme adapter (optional) */
  theme?: ThemeAdapter;
  /** Router adapter (optional) */
  router?: RouterAdapter;
  /** Custom function to fetch user permissions (optional, used after token refresh/restore) */
  permissionsFetchFn?: () => Promise<string[]>;
  /** Callback invoked after logout completes (e.g., to clear UserStateManager) */
  onLogout?: () => void | Promise<void>;
}
