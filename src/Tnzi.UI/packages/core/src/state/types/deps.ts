/**
 * @tnzi/core/state/types/deps
 *
 * Shared dependencies for state managers.
 */

import type { HttpClient } from '../../http/http';
import type { StorageAdapter } from '../../adapters/storage';
import type { ThemeAdapter } from '../../adapters/theme/index';
import type { RouterAdapter } from '../../adapters/router/index';

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
  /**
   * Prefix for persisted auth storage keys (token/refresh/expiry).
   * Defaults to `'tnzi:auth'` → `tnzi:auth:token` etc. Set a distinct value
   * to isolate multiple apps that share the same storage origin.
   */
  storagePrefix?: string;
}
