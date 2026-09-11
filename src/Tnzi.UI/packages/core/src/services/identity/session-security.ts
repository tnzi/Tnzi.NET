/**
 * Session-security signals: the two cases where the backend ends a session
 * *because something looked wrong*, rather than because it simply expired.
 *
 * Both arrive as a 401, which is why they need their own codes: "your session
 * expired, sign in again" and "we ended your session because your credentials
 * appear to be in someone else's hands" are the same HTTP status and completely
 * different things to tell a person. Silently rendering the first message for
 * the second case throws away the only moment the user could learn that their
 * account is being used from another machine.
 */

/**
 * A refresh token that had already been rotated away was presented again,
 * outside the concurrency grace window. The backend treats a replay as evidence
 * that the token was copied, and revokes the whole session.
 *
 * Whoever receives this may well be the legitimate user: whoever refreshes first
 * wins, and the loser gets this. That is precisely why it deserves a visible
 * message instead of a routine bounce to the login page.
 */
export const REFRESH_TOKEN_REUSED = 'IDENTITY_REFRESH_TOKEN_REUSED';

/**
 * The request's client fingerprint no longer matches the one captured when the
 * session was created - the token is being presented from a different browser
 * or device.
 */
export const SESSION_BINDING_MISMATCH = 'IDENTITY_SESSION_BINDING_MISMATCH';

/** Every code that means "ended for a security reason", not "expired". */
export const SESSION_SECURITY_CODES: readonly string[] = [
  REFRESH_TOKEN_REUSED,
  SESSION_BINDING_MISMATCH,
];

/**
 * Whether a failure means the session was ended for a security reason.
 *
 * Accepts both shapes a failure arrives in - the `{succeeded: false, errorCode}`
 * envelope `HttpClient` resolves with, and the thrown error the admin bridges
 * re-raise - for the same reason {@link isStepUpRequired} does: checking only one
 * makes the helper quietly useless at half the call sites.
 */
export function isSessionEndedForSecurity(value: unknown): boolean {
  const code = readErrorCode(value);
  return code != null && SESSION_SECURITY_CODES.includes(code);
}

function readErrorCode(value: unknown): string | undefined {
  if (!value || typeof value !== 'object') return undefined;
  const source = value as { errorCode?: unknown; cause?: { errorCode?: unknown } };
  const direct = typeof source.errorCode === 'string' ? source.errorCode : undefined;
  if (direct) return direct;
  const nested = source.cause?.errorCode;
  return typeof nested === 'string' ? nested : undefined;
}
