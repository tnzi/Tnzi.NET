/**
 * @tnzi/core/state/auth
 *
 * Authentication state manager - pure logic layer.
 * Uses Vue's reactivity for reactive state, UI packages can use directly or wrap as Pinia store.
 */

import { reactive } from 'vue';
import type { AuthState } from './types/auth';
import type { LoginDto, LoginResultDto, UserProfile, UserDto, UpdateProfileDto } from '../services/identity/types';
import { useAuthApi, useProfileApi } from '../services/identity/index';
import { isSessionEndedForSecurity } from '../services/identity/session-security';
import { HttpError, isHttpError } from '../errors/api-error';
import { useLogger } from '../adapters/logger';
import type { StateDeps } from './types/deps';

/**
 * Map UserDto to UserProfile.
 * UserDto doesn't have a permissions field, uses existing permissions or empty array.
 */
function toUserProfile(dto: UserDto, existingPermissions: string[] = []): UserProfile {
  return {
    id: dto.id,
    userName: dto.userName,
    email: dto.email,
    phoneNumber: dto.phoneNumber,
    nickname: dto.nickname,
    avatar: dto.avatar,
    roles: dto.roles,
    permissions: existingPermissions,
  };
}

// ============================================
// Refresh failure classification
// ============================================

/**
 * Statuses on which the server declined to *answer* rather than declining the
 * token: a client timeout (408, from HttpClient) and rate limiting (429).
 * Together with 5xx and transport errors (HttpClient reports those as 500)
 * they mean "could not ask", and a token nobody has rejected must not be
 * thrown away over them.
 */
const TRANSIENT_REFRESH_STATUSES: ReadonlySet<number> = new Set([408, 429]);

/**
 * Whether a refresh failure means the server looked at the refresh token and
 * rejected it (expired, revoked, replayed), as opposed to the request never
 * getting a verdict. Only a rejection justifies wiping the persisted tokens:
 * opening the app offline, or a refresh that hits the 30s timeout on a slow
 * link, used to erase the session for good although the server never said no.
 *
 * Unknown shapes (no status, a non-HttpError throw) count as "could not ask" -
 * keeping a dead token costs one more failed refresh on the next boot, while
 * wiping a live one costs the user their session.
 */
function isRefreshRejection(error: unknown): boolean {
  if (!isHttpError(error)) return false;
  const status = error.statusCode;
  return status >= 400 && status < 500 && !TRANSIENT_REFRESH_STATUSES.has(status);
}

/**
 * The message a session that ended for a security reason leaves for the login
 * page. Kept in one place because two paths must agree on it: the refresh that
 * detects the rejection, and the boot-restore cleanup that runs after it.
 *
 * Exported so a login page can render its own (translated) copy off
 * {@link sessionEndReasonOf} instead of matching this English string.
 */
export const SESSION_ENDED_FOR_SECURITY_MESSAGE =
  'Your session was ended for security reasons. Please sign in again.';
export const SESSION_EXPIRED_MESSAGE = 'Session expired, please login again';

/**
 * Why the previous session ended, as far as a login page needs to know.
 *
 * - `'security'`: the backend revoked the session because the credentials
 *   looked stolen (refresh-token replay, session binding mismatch). The one
 *   moment the legitimate user can learn their account is in use elsewhere.
 * - `'expired'`: the refresh token was rejected as expired / invalid. Routine.
 */
export type SessionEndReason = 'security' | 'expired';

/**
 * Classify the manager's `error` into a {@link SessionEndReason}.
 *
 * `error` is a single string slot shared with login failures ("Invalid user
 * name or password"), so a login page cannot show every value in it as a
 * session notice. Only the two messages `_doRefreshToken` /
 * `_clearAfterFailedRestore` write are session ends; anything else is null.
 */
export function sessionEndReasonOf(message: string | null | undefined): SessionEndReason | null {
  if (message === SESSION_ENDED_FOR_SECURITY_MESSAGE) return 'security';
  if (message === SESSION_EXPIRED_MESSAGE) return 'expired';
  return null;
}

// ============================================
// Initial state
// ============================================

export function createInitialAuthState(): AuthState {
  return {
    isAuthenticated: false,
    accessToken: null,
    refreshToken: null,
    tokenExpiry: null,
    user: null,
    permissions: [],
    roles: [],
    isRefreshing: false,
    error: null,
  };
}

// ============================================
// AuthStateManager
// ============================================

/**
 * Authentication state manager.
 *
 * Uses `reactive(this)` for Vue reactivity:
 * - All property reads are automatically tracked
 * - All property writes automatically trigger updates
 * - Class getters become computed properties
 *
 * Usage:
 * ```ts
 * const auth = new AuthStateManager(deps);
 * // Use directly in Vue templates
 * auth.isLoggedIn  // reactive
 * await auth.login(credentials);
 * ```
 */
export class AuthStateManager {
  // State
  isAuthenticated = false;
  accessToken: string | null = null;
  refreshToken: string | null = null;
  tokenExpiry: Date | null = null;
  user: UserProfile | null = null;
  permissions: string[] = [];
  roles: string[] = [];
  isRefreshing = false;
  error: string | null = null;

  /** Mutex: pending refresh promise for deduplication */
  private _refreshPromise: Promise<void> | null = null;
  /** Mutex: pending login promise for deduplication */
  private _loginPromise: Promise<LoginResultDto> | null = null;

  /**
   * The rejection that ended the current session, when a refresh was refused.
   *
   * ★ Recorded because the error that reaches a restore's `catch` is not always
   * the one that ended the session. In bearer mode `restoreAuth()` fetches the
   * profile first; the expired access token makes that a 401, the HttpClient
   * drives a refresh through `refreshTokenFn` (see `createTnziClient`), and when
   * the backend REJECTS it `_doRefreshToken` has already cleared the tokens by
   * the time the profile call returns. The retry `restoreAuth` then makes throws
   * the plain "No refresh token available" - and keying the security message off
   * that error erased it on the default boot path. Reset whenever a new session
   * is established or a new restore begins.
   */
  private _lastRefreshRejection: unknown = null;

  /** Persisted-storage keys derived from the configurable storage prefix. */
  private readonly _keys: { token: string; refresh: string; expiry: string };

  /** True when the backend delivers the refresh token as an HttpOnly cookie. */
  private readonly _cookieDelivery: boolean;

  constructor(private readonly deps: StateDeps) {
    const prefix = deps.storagePrefix ?? 'tnzi:auth';
    this._keys = {
      token: `${prefix}:token`,
      refresh: `${prefix}:refresh`,
      expiry: `${prefix}:expiry`,
    };
    this._cookieDelivery = deps.tokenDelivery === 'cookie';
    return reactive(this) as this;
  }

  /**
   * Auth API bound to this manager's delivery mode.
   *
   * In cookie mode the auth calls must carry credentials, otherwise a
   * cross-origin SPA neither stores the `Set-Cookie` from login nor sends it
   * back on refresh - and the symptom is "login works, reload logs me out".
   */
  private _authApi() {
    return useAuthApi(this.deps.httpClient, { withCredentials: this._cookieDelivery });
  }

  // ============================================
  // Getters (reactive computed properties)
  // ============================================

  get isLoggedIn(): boolean {
    return this.isAuthenticated && !!this.accessToken;
  }

  /**
   * Whether the backend delivers the refresh token as an HttpOnly cookie.
   *
   * Anything outside this manager that issues a session (invitation
   * acceptance builds its own `useInvitationApi`) has to pass
   * `withCredentials` on that call in cookie mode, or a cross-origin SPA
   * drops the `Set-Cookie` and the first refresh ends the session.
   */
  get cookieDelivery(): boolean {
    return this._cookieDelivery;
  }

  /**
   * Why the previous session ended, or null when nothing ended it (fresh boot,
   * explicit logout, or a login failure occupying `error` instead).
   *
   * The login page reads this on mount. It is derived from `error` rather than
   * stored separately so the existing invariants keep holding: `clearAuth()`
   * resets it, a new login attempt resets it (`_doLogin` nulls `error` first),
   * and the "keep the message after onUnauthorized" guard in `createTnziClient`
   * protects it for free.
   */
  get sessionEndReason(): SessionEndReason | null {
    return sessionEndReasonOf(this.error);
  }

  get userName(): string {
    return this.user?.userName ?? '';
  }

  get displayName(): string {
    return this.user?.nickname ?? this.user?.userName ?? 'Guest';
  }

  get avatar(): string | null {
    return this.user?.avatar ?? null;
  }

  get userRoles(): string[] {
    return this.roles.length > 0 ? this.roles : (this.user?.roles ?? []);
  }

  get userPermissions(): string[] {
    return this.permissions.length > 0 ? this.permissions : (this.user?.permissions ?? []);
  }

  get isTokenExpired(): boolean {
    if (!this.tokenExpiry) return true;
    return new Date(this.tokenExpiry) <= new Date();
  }

  get tokenExpiresIn(): number {
    if (!this.tokenExpiry) return 0;
    const remaining = new Date(this.tokenExpiry).getTime() - Date.now();
    return Math.max(0, Math.floor(remaining / 1000));
  }

  // ============================================
  // Permission checking
  // ============================================

  hasRole(role: string): boolean {
    return this.userRoles.includes(role);
  }

  hasPermission(permission: string): boolean {
    return this.userPermissions.includes(permission);
  }

  hasAnyRole(roles: string[]): boolean {
    return roles.some(role => this.userRoles.includes(role));
  }

  hasAnyPermission(permissions: string[]): boolean {
    return permissions.some(perm => this.userPermissions.includes(perm));
  }

  // ============================================
  // Actions (async business logic)
  // ============================================

  /**
   * Login with credentials.
   * Uses mutex to prevent concurrent login attempts.
   */
  async login(credentials: LoginDto): Promise<LoginResultDto> {
    // If a login is already in progress, wait for it
    if (this._loginPromise) {
      return this._loginPromise;
    }

    this._loginPromise = this._doLogin(credentials);
    try {
      return await this._loginPromise;
    } finally {
      this._loginPromise = null;
    }
  }

  private async _doLogin(credentials: LoginDto): Promise<LoginResultDto> {
    this.error = null;
    this.isRefreshing = true;

    try {
      const api = this._authApi();
      const result = await api.loginWithRefreshToken(credentials);
      if (!result.succeeded || !result.data) {
        // A failed envelope is not always a failure: `2FA_REQUIRED`,
        // `IDENTITY_PENDING_ACTIONS_REQUIRED` and `IDENTITY_CAPTCHA_REQUIRED`
        // are challenges whose `errorDetails` (temp token, methods, captcha)
        // the caller needs to continue. Throw the envelope as an HttpError so
        // `errorCode` / `details` survive - a bare Error(message) left the
        // accounts with the strongest settings unable to sign in through here.
        throw new HttpError({ ...result, message: result.message ?? 'Login failed' });
      }
      const tokenResult = result.data;
      // Set token first so profile fetch is authenticated
      this.accessToken = tokenResult.accessToken;
      this.refreshToken = tokenResult.refreshToken;
      this.tokenExpiry = new Date(Date.now() + tokenResult.expiresIn * 1000);
      this.isAuthenticated = true;
      this.deps.httpClient.setAccessToken(this.accessToken);

      // Fetch profile and permissions in parallel to reduce login latency
      await Promise.all([this.fetchUserProfile(), this._fetchPermissions()]);
      this.persistTokens();

      return {
        accessToken: tokenResult.accessToken,
        refreshToken: tokenResult.refreshToken,
        expiresIn: tokenResult.expiresIn,
        tokenType: 'Bearer',
        user: this.user!,
      };
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Login failed';
      throw error;
    } finally {
      this.isRefreshing = false;
    }
  }

  /**
   * Logout and clean up all auth state.
   * Invokes onLogout callback (e.g., to clear UserStateManager).
   */
  async logout(): Promise<void> {
    try {
      // Revoke on the server whenever we hold a session, profile or not. A
      // profile fetch can fail while the token pair is perfectly alive, and in
      // cookie mode the HttpOnly refresh cookie is a credential only the server
      // can kill - skipping the call because `user` is null leaves it valid.
      if (this.accessToken) {
        await this._revokeOnServer();
      }
    } finally {
      this.clearAuth();
      this.clearPersistedTokens();

      // Notify dependent state managers (e.g., UserStateManager)
      try {
        await this.deps.onLogout?.();
      } catch {
        // Ignore errors from logout callback
      }

      this.deps.router?.push(this.deps.loginPath ?? '/login');
    }
  }

  /**
   * Revoke the session server-side, obtaining a live access token first when
   * the one we hold has expired.
   *
   * `/auth/logout` is `[ApiAuthorize]` and the call is `skipAuthRefresh`, so
   * an expired bearer is rejected before the server ever reaches revocation -
   * and the refresh token (or the HttpOnly cookie) stays alive after the user
   * has watched "Sign out" succeed. On a shared machine in cookie mode the next
   * cold boot then resumes the previous person's session from that cookie.
   *
   * The refresh here is deliberately NOT `_doRefreshToken`: that path is the
   * session-expiry path and clears state, sets "Session expired" and navigates
   * on failure. Failing to refresh before a logout is just "nothing left to
   * revoke"; the local clear that follows is the same either way.
   */
  private async _revokeOnServer(): Promise<void> {
    const api = this._authApi();
    // Proactive: the client-side expiry says the token is dead.
    const attemptedProactively = this.isTokenExpired;
    if (attemptedProactively) await this._refreshForLogout(api);
    let result = await this._callLogout(api);
    // Reactive: the server disagreed with the client-side expiry (clock skew,
    // a shorter server-side lifetime). One more try with a fresh token - but
    // only when no refresh was ATTEMPTED yet. A proactive refresh that the
    // server refused already answers the question; presenting the same dead
    // token again is a wasted round trip, and the backend's replay detection
    // would read a plain sign-out as a copied token.
    if (result?.code === 401 && !attemptedProactively && (await this._refreshForLogout(api))) {
      result = await this._callLogout(api);
    }
    if (result && !result.succeeded) {
      useLogger().warn('Server-side logout did not succeed; the session may still be alive', {
        code: result.code,
        errorCode: result.errorCode,
      });
    }
  }

  /** One logout POST; a thrown error (network) counts as "no verdict". */
  private async _callLogout(api: ReturnType<typeof useAuthApi>) {
    try {
      return await api.logout();
    } catch {
      return null;
    }
  }

  /**
   * Obtain a fresh access token for the logout call. Returns whether it worked;
   * on failure the caller proceeds to the local clear as before.
   */
  private async _refreshForLogout(api: ReturnType<typeof useAuthApi>): Promise<boolean> {
    if (!this._cookieDelivery && !this.refreshToken) return false;
    try {
      const result = await api.refreshToken(
        this._cookieDelivery ? {} : { refreshToken: this._currentRefreshToken() });
      if (!result.succeeded || !result.data?.accessToken) return false;
      this.accessToken = result.data.accessToken;
      this.deps.httpClient.setAccessToken(this.accessToken);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Refresh access token.
   * Uses mutex to deduplicate concurrent refresh attempts:
   * only the first call executes the actual refresh, subsequent calls wait for the same result.
   */
  async refreshAccessToken(): Promise<void> {
    // Throw (instead of silent return) when no refresh token is available
    // so callers - particularly HttpClient.refreshTokenFn wrappers - can
    // distinguish "refreshed successfully" from "could not refresh". A
    // silent no-op here caused HttpClient to retry the original request
    // with the same stale access token, get another 401, and short-circuit
    // out without firing `onUnauthorized` - so the page would stay mounted
    // while every API call kept failing.
    // In cookie mode the refresh token is in an HttpOnly cookie the page cannot
    // read - "we don't hold one" is the normal state there, not a failure.
    if (!this._cookieDelivery && !this.refreshToken) {
      throw new Error('No refresh token available');
    }

    // If a refresh is already in progress, wait for it
    if (this._refreshPromise) {
      return this._refreshPromise;
    }

    this._refreshPromise = this._doRefreshToken();
    try {
      await this._refreshPromise;
    } finally {
      this._refreshPromise = null;
    }
  }

  private async _doRefreshToken(): Promise<void> {
    this.isRefreshing = true;
    this.error = null;
    this._lastRefreshRejection = null;

    try {
      const api = this._authApi();
      const result = await api.refreshToken(
        this._cookieDelivery ? {} : { refreshToken: this._currentRefreshToken() });
      if (!result.succeeded || !result.data) {
        // Throw the envelope itself (as an HttpError): the catch below needs
        // the status to tell "the server rejected the token" from "the server
        // could not be asked", and the error code to tell "your session
        // expired" from "we ended your session because the token appears to be
        // compromised".
        throw new HttpError({ ...result, message: result.message ?? 'Token refresh failed' });
      }
      this.accessToken = result.data.accessToken;
      // Cookie mode: the body's refreshToken is empty by design (the browser got
      // a new cookie instead). Assigning it would wipe the flag we use to know a
      // session exists at all.
      if (!this._cookieDelivery) {
        this.refreshToken = result.data.refreshToken;
      }
      this.tokenExpiry = new Date(Date.now() + result.data.expiresIn * 1000);
      this.persistTokens();

      // Sync token to HTTP client
      this.deps.httpClient.setAccessToken(this.accessToken);

      // Fetch updated permissions after token refresh
      await this._fetchPermissions();
    } catch (error) {
      // Clear mutex BEFORE cleanup so concurrent callers don't await a failed promise
      this._refreshPromise = null;
      // Only a REJECTION is the end of the session. A transport failure
      // (offline, timeout, 5xx, 429) means the server never looked at the
      // token: rethrow so this attempt fails - the HttpClient's onUnauthorized
      // will still sign this tab out - but leave the persisted tokens, the
      // message and the router alone so the next boot can try again. Wiping
      // here turned "opened the app on the train" into "signed out for good".
      if (!isRefreshRejection(error)) {
        throw error;
      }
      this._lastRefreshRejection = error;
      // Local sign-out only. The refresh token is already dead, so the backend
      // logout endpoint would just reject the stale access token; going through
      // logout() used to POST /auth/logout with that expired token, and the
      // resulting 401 stalled inside the HttpClient refresh cycle, delaying the
      // session-expired signal by a full request timeout.
      this.clearAuth();
      this.clearPersistedTokens();
      // Set AFTER clearAuth (which resets error) so the message survives for
      // the login page to display.
      // ★ The security case gets its own message on purpose: this may be the
      // only moment the legitimate user is told that their credentials are being
      // used from somewhere else. Rendering "session expired" for it discards
      // that signal entirely.
      this.error = isSessionEndedForSecurity(error)
        ? SESSION_ENDED_FOR_SECURITY_MESSAGE
        : SESSION_EXPIRED_MESSAGE;
      try {
        await this.deps.onLogout?.();
      } catch {
        // Ignore errors from logout callback
      }
      this.deps.router?.push(this.deps.loginPath ?? '/login');
      throw error;
    } finally {
      this.isRefreshing = false;
    }
  }

  async fetchUserProfile(): Promise<void> {
    if (!this.isAuthenticated) return;

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.get();
      if (result.succeeded && result.data) {
        this.user = toUserProfile(result.data, this.permissions);
      }
    } catch (error) {
      useLogger().error('Failed to fetch user profile:', error);
    }
  }

  async updateProfile(data: UpdateProfileDto): Promise<UserProfile> {
    if (!this.isAuthenticated) {
      throw new Error('Not authenticated');
    }

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.update(data);
      if (!result.succeeded || !result.data) {
        throw new HttpError({ ...result, message: result.message ?? 'Update failed' });
      }
      this.user = toUserProfile(result.data, this.permissions);
      return this.user;
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Update failed';
      throw error;
    }
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<void> {
    if (!this.isAuthenticated) {
      throw new Error('Not authenticated');
    }

    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.changePassword({ currentPassword, newPassword });
      if (!result.succeeded) {
        throw new HttpError({ ...result, message: result.message ?? 'Password change failed' });
      }
    } catch (error) {
      this.error = error instanceof Error ? error.message : 'Password change failed';
      throw error;
    }
  }

  // ============================================
  // State management
  // ============================================

  setAuth(result: LoginResultDto): void {
    this.isAuthenticated = true;
    this.accessToken = result.accessToken;
    this.refreshToken = result.refreshToken;
    this.tokenExpiry = new Date(Date.now() + result.expiresIn * 1000);
    this.user = result.user;
    this.roles = result.user.roles;
    this.permissions = result.user.permissions;
    this.error = null;
    this._lastRefreshRejection = null;

    // Sync token to HTTP client
    this.deps.httpClient.setAccessToken(this.accessToken);
  }

  /**
   * Establish an authenticated session from tokens obtained OUTSIDE the
   * password flow - code login, OAuth callback, magic link, etc. - where the
   * response carries the access/refresh tokens but no user object.
   *
   * Mirrors the tail of {@link login}: it sets + persists the tokens, syncs
   * the HTTP client, then fetches the user profile so getters / permissions
   * populate. Use this instead of {@link setAuth} when you only have tokens
   * (setAuth requires a full `LoginResultDto` including the user).
   */
  async applyTokenSession(tokens: {
    accessToken: string;
    refreshToken?: string | null;
    expiresIn?: number | null;
  }): Promise<void> {
    this.isAuthenticated = true;
    this.accessToken = tokens.accessToken;
    this.refreshToken = tokens.refreshToken ?? null;
    this.tokenExpiry = tokens.expiresIn ? new Date(Date.now() + tokens.expiresIn * 1000) : null;
    this.error = null;

    // Sync + persist so a hard refresh can restore the session. NOTE: restoreAuth
    // requires BOTH an access and a refresh token, so a token-only session (no
    // refresh token) lives only for the current tab and is NOT restored on hard
    // reload. setAuth deliberately persists nothing.
    this.deps.httpClient.setAccessToken(this.accessToken);
    this.persistTokens();

    // Mirror login()/restoreAuth(): fetch BOTH profile and permissions, otherwise
    // every hasPermission() / route guard / menu fails until a refresh or reload
    // happens to repopulate this.permissions.
    await Promise.all([this.fetchUserProfile(), this._fetchPermissions()]);
  }

  clearAuth(): void {
    Object.assign(this, createInitialAuthState());
    // Clear token from HTTP client
    this.deps.httpClient.setAccessToken(null);
  }

  setError(error: string | null): void {
    this.error = error;
  }

  // ============================================
  // Persistence (restore from / write to storage)
  // ============================================

  async restoreAuth(): Promise<void> {
    this._lastRefreshRejection = null;
    // Cookie mode keeps nothing in web storage on purpose - the whole point is
    // that no script (ours or an attacker's) can read the credential. So there is
    // nothing to "restore": ask the server instead, and let the HttpOnly cookie
    // answer whether a session still exists.
    if (this._cookieDelivery) {
      // A deployment that switched from bearer to cookie delivery leaves the old
      // token pair in web storage forever - nothing in cookie mode ever reads
      // or clears those keys. Scrub them: keeping credentials out of storage is
      // the whole point of this mode.
      this.clearPersistedTokens();
      await this._restoreFromCookie();
      return;
    }

    const token = this.deps.storage.get<string>(this._keys.token);
    const refresh = this.deps.storage.get<string>(this._keys.refresh);
    const expiry = this.deps.storage.get<string>(this._keys.expiry);

    if (!token || !refresh) return;

    this.accessToken = token;
    this.refreshToken = refresh;
    this.tokenExpiry = expiry ? new Date(expiry) : null;

    // Sync token to HTTP client
    this.deps.httpClient.setAccessToken(this.accessToken);

    // Try to fetch user profile
    try {
      const api = useProfileApi(this.deps.httpClient);
      const result = await api.get();
      if (!result.succeeded || !result.data) {
        throw new Error('Failed to fetch profile');
      }
      this.user = toUserProfile(result.data);
      this.roles = result.data.roles ?? [];

      // Fetch permissions before marking as authenticated,
      // so permission checks don't run against stale/empty permissions
      await this._fetchPermissions();
      this.isAuthenticated = true;
    } catch {
      // Token may be expired, try refresh
      try {
        await this.refreshAccessToken();
        // The refresh succeeded and persisted a fresh token pair, so the session
        // is live again - mark it authenticated BEFORE fetching the profile.
        // fetchUserProfile() short-circuits on `!isAuthenticated`, so skipping
        // this leaves `user` null and `isLoggedIn` false, and route guards bounce
        // the user back to the login page even though the refresh just worked.
        this.isAuthenticated = true;
        await this.fetchUserProfile();
        this.roles = this.user?.roles ?? [];
      } catch (error) {
        // Refresh also failed. Nothing is signed in either way, but what is
        // persisted depends on WHY: only a rejection by the server ends the
        // session for good; offline / timeout keep the tokens for the next boot.
        // ★ `error` may be the plain "No refresh token available": in bearer
        // mode the profile 401 above already drove the refresh through the
        // HttpClient, and a REJECTED one cleared the token before this retry.
        // The rejection the manager recorded is the verdict; this error is not.
        const cause = this._lastRefreshRejection ?? error;
        this._clearAfterFailedRestore(cause);
        if (isRefreshRejection(cause)) {
          this.clearPersistedTokens();
        }
      }
    }
  }

  /**
   * Clear the half-restored in-memory state after a failed boot, keeping the
   * one message worth showing. A boot that fails because the session was
   * ended for a security reason must keep that message, or the user is bounced
   * to the login page with nothing to tell them their credentials are in use
   * elsewhere. An ordinary expiry (or the plain "no cookie" boot) stays quiet,
   * as before - `clearAuth()` resets `error`.
   *
   * `cause` is the failure that actually ended the session - callers pass the
   * recorded refresh rejection when there is one (see `_lastRefreshRejection`).
   * The message is re-derived from it rather than read back from `this.error`,
   * so nothing that ran in between (a second `clearAuth()`, an `onUnauthorized`
   * listener) can have quietly blanked it first.
   */
  private _clearAfterFailedRestore(cause: unknown): void {
    const securityMessage = isSessionEndedForSecurity(cause) ? SESSION_ENDED_FOR_SECURITY_MESSAGE : null;
    this.clearAuth();
    this.error = securityMessage;
  }

  // ============================================
  // Internal methods
  // ============================================

  /**
   * The refresh token to present, re-read from shared storage first.
   *
   * ★ `this.refreshToken` is per-tab memory, seeded once at `restoreAuth()`. When a
   * second tab rotates the token, this tab's copy silently goes stale - and the
   * backend now treats a rotated-away token as a replay and kills the WHOLE
   * session, not just this tab. So the two-tab case would end with everything
   * signed out and a "your session was ended for security reasons" message, for
   * no reason at all.
   *
   * Reading storage at the moment of use collapses that window down to genuinely
   * concurrent refreshes, which the backend's rotation overlap window covers.
   * (Cookie mode has no such problem: the browser holds exactly one cookie.)
   */
  private _currentRefreshToken(): string {
    const persisted = this.deps.storage.get<string>(this._keys.refresh);
    if (persisted && persisted !== this.refreshToken) {
      this.refreshToken = persisted;
    }
    return this.refreshToken!;
  }

  /**
   * Cookie mode boot: exchange the HttpOnly refresh cookie for a fresh access
   * token, then load the profile.
   *
   * A failure here is the ordinary "not signed in" state (no cookie, or it
   * expired / was revoked), not an error worth surfacing - so it clears quietly
   * and lets the route guards do their job.
   */
  private async _restoreFromCookie(): Promise<void> {
    try {
      await this.refreshAccessToken();
      this.isAuthenticated = true;
      await this.fetchUserProfile();
      this.roles = this.user?.roles ?? [];
    } catch (error) {
      this._clearAfterFailedRestore(this._lastRefreshRejection ?? error);
    }
  }

  /**
   * Fetch permissions using the configured permissionsFetchFn.
   * Falls back to empty array if not configured.
   */
  private async _fetchPermissions(): Promise<void> {
    if (this.deps.permissionsFetchFn) {
      try {
        this.permissions = await this.deps.permissionsFetchFn();
      } catch {
        // Non-critical: keep existing permissions
        useLogger().warn('Failed to fetch permissions');
      }
    }
  }

  private persistTokens(): void {
    // Cookie mode: nothing auth-related goes to web storage. Writing the access
    // token "just for convenience" would hand back exactly the artefact this mode
    // exists to remove - a credential any script or local process can read.
    if (this._cookieDelivery) return;

    if (this.accessToken) {
      this.deps.storage.set(this._keys.token, this.accessToken);
    }
    if (this.refreshToken) {
      this.deps.storage.set(this._keys.refresh, this.refreshToken);
    }
    if (this.tokenExpiry) {
      this.deps.storage.set(this._keys.expiry, this.tokenExpiry.toISOString());
    }
  }

  private clearPersistedTokens(): void {
    this.deps.storage.remove(this._keys.token);
    this.deps.storage.remove(this._keys.refresh);
    this.deps.storage.remove(this._keys.expiry);
  }
}
