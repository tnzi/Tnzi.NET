/**
 * Auth challenges: 401s that are NOT session expiry.
 *
 * A 401 normally means "the access token is no good" and the client's answer
 * is refresh-and-retry, then `onUnauthorized` when that fails. A few backend
 * replies use the same status for something else entirely: the session is
 * fine, but this one action wants the person to prove they are present
 * (step-up). Feeding those into the refresh path burns a refresh-token
 * rotation for nothing, replays the request straight back into the same
 * challenge, and the second 401 then signs the user out - so the challenge can
 * never be answered.
 *
 * The distinction is made on the error code, never on the status: the backend
 * can be configured to answer step-up with 403 instead, and an expired session
 * is a 401 too. This module lives under `http/` so the client can consult it
 * without importing anything from `services/`; `services/identity/step-up`
 * re-exports the constant for callers that think in terms of step-up.
 */

/** The error code `[RequireStepUp]` answers with when re-authentication is needed. */
export const STEP_UP_REQUIRED = 'IDENTITY_STEP_UP_REQUIRED';

/**
 * Error codes the client treats as a challenge rather than an expired session
 * when they arrive with status 401. Overridable per client via
 * `HttpClientConfig.authChallengeCodes`.
 */
export const DEFAULT_AUTH_CHALLENGE_CODES: readonly string[] = [STEP_UP_REQUIRED];
