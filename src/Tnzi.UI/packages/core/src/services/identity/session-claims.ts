/**
 * Reading the session claim off the access token.
 *
 * `GET /users/profile/sessions` lists every active session of the caller,
 * including the one making the request, and nothing in the DTO says which row
 * that is. The token the backend issued for this session does: it carries a
 * `session_id` claim, the same one the server checks on every request to
 * decide whether the session is still alive. Reading it back lets a settings
 * page label the caller's own row and keep "Revoke" from signing the user out
 * of the page they are standing on.
 *
 * This is presentation only. Nothing here verifies the signature, and no
 * decision that matters for security is made off it - the server enforces
 * revocation regardless of what the page thinks.
 */

/** The claim name `Tnzi.Identity` writes into every session-bound token. */
export const SESSION_ID_CLAIM = 'session_id';

/**
 * The session id the access token is bound to, or null.
 *
 * Null means "cannot tell": no token, an opaque (non-JWT) token, a token from
 * a deployment that predates session binding, or an unreadable payload. A
 * caller must treat null as unknown, never as "not the current session" -
 * hiding a safeguard on a guess is how a page ends up signing its user out.
 */
export function readSessionIdClaim(accessToken: string | null | undefined): string | null {
  if (!accessToken) return null;
  const parts = accessToken.split('.');
  if (parts.length !== 3) return null;

  const payload = decodePayload(parts[1]!);
  if (!payload) return null;

  const value = payload[SESSION_ID_CLAIM];
  return typeof value === 'string' && value ? value : null;
}

function decodePayload(segment: string): Record<string, unknown> | null {
  try {
    const base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    // atob yields a binary string; decode the bytes as UTF-8 so a payload that
    // carries a non-ASCII display name does not corrupt the whole parse.
    const bytes = Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
    const json = new TextDecoder().decode(bytes);
    const parsed: unknown = JSON.parse(json);
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}
