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
  /**
   * Route path the optional `router` adapter is pushed to after logout /
   * session expiry. Defaults to `'/login'`. Set this when the app's login
   * route lives elsewhere (e.g. under a basePath prefix like
   * `'/admin/login'`). Admin apps built on `@tnzi/ui-admin` usually leave
   * `router` unset here: the framework's session-expired handler redirects
   * by route name instead, which is deployment-prefix agnostic.
   */
  loginPath?: string;
  /**
   * How the backend delivers the refresh token.
   *
   * - `'bearer'` (default): the refresh token comes back in the response body and
   *   is persisted through the `storage` adapter, alongside the access token.
   * - `'cookie'`: the backend sets an `HttpOnly` cookie
   *   (`Identity:TokenDelivery:Mode = Cookie`) and the body carries no refresh
   *   token. Nothing auth-related is written to `storage` at all - the access
   *   token lives in memory only and is re-obtained from the cookie on reload.
   *
   * ★ Why `'cookie'` is worth the extra moving part: a token in `localStorage`
   * is readable by any script on the page (one XSS and it is gone) and by any
   * process running as the user (this is exactly what infostealer malware
   * collects). An `HttpOnly` cookie is readable by neither. It also happens to be
   * the only shape that browser-level device binding (DBSC) can protect - that
   * mechanism binds *cookies* to a device key and does nothing for bearer tokens
   * in web storage.
   *
   * Must match the backend setting: `'cookie'` against a `Bearer` backend leaves
   * the client with no refresh token at all.
   */
  tokenDelivery?: 'bearer' | 'cookie';
}
