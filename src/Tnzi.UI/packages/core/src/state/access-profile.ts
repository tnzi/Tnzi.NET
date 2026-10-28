/**
 * @tnzi/core/state/access-profile
 *
 * The default permission source of {@link AuthStateManager}.
 */

import type { HttpClient } from '../http/http';

/**
 * The framework's access profile for the CURRENT user. Any signed-in user may
 * read their own.
 *
 * Returns `[]` on any failure - a backend that predates the endpoint, a network
 * blip, an unexpected envelope. `AuthStateManager._fetchPermissions` keeps the
 * previous list when the source throws, so returning empty rather than throwing
 * is the honest answer for "the server says you hold nothing".
 *
 * NOTE for super admins: the backend returns the FULL code list for them rather
 * than a bypass flag, so `hasPermission()` is correct without any special-casing
 * on this side.
 */
export async function fetchAccessProfilePermissions(http: HttpClient): Promise<string[]> {
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
