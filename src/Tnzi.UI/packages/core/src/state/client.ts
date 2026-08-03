/**
 * @tnzi/core/state/client
 *
 * `createTnziClient()` - one call that wires the standard Tnzi front-end
 * runtime: an {@link HttpClient} with the framework's canonical auto-refresh +
 * unauthorized behaviour, an {@link AuthStateManager} bound to it, and the auth
 * API factory. Every consumer app used to hand-roll this identical singleton
 * (the `refreshTokenFn` forward-ref dance, the `onUnauthorized → clearAuth`
 * wiring, the `AuthStateManager` construction) in its own `app.ts`; this
 * collapses it to:
 *
 * ```ts
 * const { http, auth, authApi } = createTnziClient({ baseUrl: '/api' })
 * ```
 *
 * The returned pieces are plain framework objects - attach any additional
 * per-service factories the app needs (`useChatApi(http)`, …) alongside them.
 */

import { HttpClient } from '../http/http';
import { createLocalStorageAdapter } from '../adapters/storage';
import type { StorageAdapter } from '../adapters/storage';
import { useAuthApi } from '../services/identity/index';
import { AuthStateManager } from './auth';

export interface CreateTnziClientOptions {
  /**
   * REST base URL for every request the client makes. Defaults to `'/api'`,
   * matching the Vite dev proxy / production reverse-proxy convention. Also
   * the base the SignalR hub URLs derive from on the admin side
   * (`defineAdminApp({ apiBase })`) - keep the two in sync.
   */
  baseUrl?: string;
  /**
   * Persistent storage for the auth tokens. Defaults to a localStorage
   * adapter (per-origin). Pass a custom adapter to isolate storage or run in a
   * non-browser environment.
   */
  storage?: StorageAdapter;
  /**
   * Prefix for the persisted token keys (`{prefix}:token` etc.). Defaults to
   * `'tnzi:auth'`. Set a distinct value to isolate multiple apps that share
   * the same storage origin.
   */
  storagePrefix?: string;
  /**
   * Request timeout in ms forwarded to the {@link HttpClient} (default 30000;
   * `0` disables). Rarely needed - the framework default is sensible.
   */
  timeout?: number;
  /**
   * How the signed-in user's permission codes are loaded, feeding
   * `auth.hasPermission()` / `hasAnyPermission()`.
   *
   * Defaults to the framework's own access-profile endpoint. Pass `null` to
   * skip the call entirely in an app that never checks a permission - but be
   * aware of what skipping means: `hasPermission()` then answers `false` to
   * everything, so a privileged surface gated on it simply never appears, with
   * nothing logged. That silent-denial failure mode is why loading is the
   * default rather than opt-in.
   *
   * NOTE for super admins: the backend returns the FULL code list for them
   * rather than a bypass flag, so `hasPermission()` is correct without any
   * special-casing on this side.
   */
  permissionsFetchFn?: (() => Promise<string[]>) | null;
}

/**
 * The wired runtime returned by {@link createTnziClient}: the shared HttpClient,
 * the reactive auth state manager bound to it, and the auth API factory that
 * backs the login callbacks. `@tnzi/ui-admin`'s `defineAdminApp({ runtime })`
 * accepts this object directly to auto-generate the default auth orchestration.
 */
export interface TnziClient {
  http: HttpClient;
  auth: AuthStateManager;
  authApi: ReturnType<typeof useAuthApi>;
}

/**
 * Create the standard Tnzi front-end runtime (HttpClient + AuthStateManager +
 * auth API), fully wired with the framework's canonical token-refresh and
 * session-expiry behaviour.
 *
 * The `refreshTokenFn` asks the auth manager for a fresh access token and
 * treats "same token back" / "no token" as a refresh failure (so the
 * HttpClient falls through to `onUnauthorized`); `onUnauthorized` clears the
 * auth state. This is the belt-and-braces wiring every app copied by hand.
 */
export function createTnziClient(options: CreateTnziClientOptions = {}): TnziClient {
  const storage = options.storage ?? createLocalStorageAdapter();

  // Forward declaration so the HttpClient's refreshTokenFn can reach the auth
  // manager that is constructed just below it.
  let authRef: AuthStateManager | null = null;

  const http = new HttpClient({
    baseUrl: options.baseUrl ?? '/api',
    timeout: options.timeout,
    refreshTokenFn: async () => {
      if (!authRef) throw new Error('Auth not initialised');
      const previous = authRef.accessToken;
      await authRef.refreshAccessToken();
      const next = authRef.accessToken;
      // AuthStateManager.refreshAccessToken now throws when no refresh token is
      // available, but if a backend returns the same access token (or empties
      // it) we still surface that as a refresh failure so HttpClient triggers
      // onUnauthorized instead of retrying with a stale token.
      if (!next || next === previous) {
        throw new Error('Refresh did not produce a new token');
      }
      return next;
    },
    onUnauthorized: () => {
      // Refresh path failed (or no refresh token) - clear core auth state.
      // Navigation is app-level: `@tnzi/ui-admin`'s built-in session-expired
      // listener (registered via defineAdminApp install) redirects the admin
      // app to the login route; other surfaces rely on their route guards.
      authRef?.clearAuth();
    },
  });

  const auth = new AuthStateManager({
    httpClient: http,
    storage,
    storagePrefix: options.storagePrefix,
    permissionsFetchFn:
      options.permissionsFetchFn === null
        ? undefined
        : (options.permissionsFetchFn ?? (() => fetchAccessProfilePermissions(http))),
  });
  authRef = auth;

  return { http, auth, authApi: useAuthApi(http) };
}

/**
 * Default permission source: the framework's access profile for the CURRENT
 * user. Any signed-in user may read their own.
 *
 * Returns `[]` on any failure - a backend that predates the endpoint, a network
 * blip, an unexpected envelope. `AuthStateManager._fetchPermissions` keeps the
 * previous list when this throws, so returning empty rather than throwing is
 * the honest answer for "the server says you hold nothing".
 */
async function fetchAccessProfilePermissions(http: HttpClient): Promise<string[]> {
  const res = await http.get<{ permissions?: string[] | null }>(
    '/admin/function-authorization/access-profile',
    // ★ Part of the auth cycle, so it must NOT re-enter the refresh path.
    // `_fetchPermissions` runs right after a token is established - including
    // from inside `refreshAccessToken` itself. A 401 here without this flag
    // would ask the HttpClient to refresh while a refresh is already in flight,
    // and that wait can only be broken by the 30s mutex timeout. A 401 on this
    // endpoint means the session is bad; retrying it cannot help.
    { skipAuthRefresh: true },
  );
  const permissions = res?.data?.permissions;
  return Array.isArray(permissions) ? permissions : [];
}
