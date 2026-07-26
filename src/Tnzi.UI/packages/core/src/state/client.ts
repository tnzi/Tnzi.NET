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
  });
  authRef = auth;

  return { http, auth, authApi: useAuthApi(http) };
}
