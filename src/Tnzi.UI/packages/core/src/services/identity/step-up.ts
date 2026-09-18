/**
 * Step-up (re-authentication for one action) helpers.
 *
 * A valid session is not always enough. A few actions - taking an original
 * attachment out of a controlled environment, a bulk export, releasing a
 * payment - need proof that the person is here right now, not that they logged
 * in this morning on a laptop that has been unattended since.
 *
 * The server answers those endpoints with `IDENTITY_STEP_UP_REQUIRED` and the
 * scope it wants. Verify, then retry the original call unchanged. That retry
 * loop is identical in every app, so it is collapsed into `withStepUp` here.
 */

import type { HttpClient } from '../../http/http';
import { STEP_UP_REQUIRED } from '../../http/auth-challenge';
import { useAuthApi } from './api';
import { isPasskeySupported, PasskeyUnsupportedError } from './passkey';
import type { StepUpGrantDto, TwoFactorType } from './types';

/**
 * The error code the backend uses to ask for re-authentication.
 *
 * Defined under `http/` because `HttpClient` has to recognise it too: a
 * step-up 401 that entered the ordinary refresh-and-retry path would rotate
 * the refresh token for nothing and then sign the user out.
 */
export { STEP_UP_REQUIRED };

/**
 * Whether something is a step-up challenge rather than a real failure.
 *
 * Accepts **both shapes a failure arrives in**: `HttpClient` resolves failures
 * as `{succeeded: false, errorCode}` envelopes, while the bridges in the admin
 * shell unwrap those and throw. Checking only one of the two would make this
 * helper silently useless for half of the call sites - and "silently useless"
 * here means the protected action just fails and never offers to re-verify.
 *
 * Matches on the error code, not the status: step-up comes back as 401 by
 * default, and so does an expired session. Treating the two the same sends the
 * user to the login page when all they needed was to touch their security key.
 */
export function isStepUpRequired(value: unknown): boolean {
  return readErrorCode(value) === STEP_UP_REQUIRED;
}

/**
 * The scope carried by a step-up challenge, if the server named one.
 *
 * On the wire the challenge's details are the envelope's `errorDetails`
 * (`ApiResult.Error(message, status, code, new { scope })` lands its last
 * argument there); on the thrown `HttpError` shape the same object is
 * `details`. There is no `errors` field anywhere in the contract - the first
 * version of this helper read one, and `withStepUp` never called `verify`.
 */
export function stepUpScopeOf(value: unknown): string | undefined {
  const source = value as
    | { errorDetails?: { scope?: unknown }; details?: { scope?: unknown }; scope?: unknown }
    | null;
  const scope = source?.errorDetails?.scope ?? source?.details?.scope ?? source?.scope;
  return typeof scope === 'string' && scope ? scope : undefined;
}

/**
 * Read the error code off either shape.
 *
 * A successful envelope has no `errorCode`, so this returns undefined for it
 * and every caller treats it as "not a challenge" - which is what we want.
 */
function readErrorCode(value: unknown): string | undefined {
  const source = value as { errorCode?: string; succeeded?: boolean } | null;
  if (source?.succeeded === true) {
    return undefined;
  }
  return source?.errorCode;
}

/**
 * Re-authenticate with a passkey for one scope.
 *
 * Reuses the ordinary assertion challenge - there is no separate step-up
 * challenge endpoint.
 *
 * @returns The grant, or `null` when the user dismissed the system dialog.
 *   Dismissal is a normal outcome, not an error.
 */
export async function stepUpWithPasskey(
  client: HttpClient,
  scope: string,
): Promise<StepUpGrantDto | null> {
  if (!isPasskeySupported()) {
    throw new PasskeyUnsupportedError();
  }

  const api = useAuthApi(client);
  const begun = ensureData(await api.beginPasskeyAssertion());

  const requestOptions = PublicKeyCredential.parseRequestOptionsFromJSON(
    JSON.parse(begun.optionsJson),
  );

  const credential = (await navigator.credentials.get({
    publicKey: requestOptions,
  })) as PublicKeyCredential | null;

  if (!credential) {
    return null;
  }

  return ensureData(
    await api.stepUpWithPasskey({
      stateId: begun.stateId,
      credentialJson: JSON.stringify(credential.toJSON()),
      scope,
    }),
  );
}

/**
 * Send a step-up verification code to the signed-in user, returning the masked
 * destination (e.g. `a***@example.com`) so the prompt can say where it went.
 *
 * Use this rather than any other send-code endpoint: a code issued for login /
 * recovery / contact-change will not complete a step-up.
 */
export async function sendStepUpCode(
  client: HttpClient,
  type: TwoFactorType,
): Promise<string | null> {
  const api = useAuthApi(client);
  return ensureData(await api.stepUpSendCode({ type }));
}

/** Re-authenticate with a two-factor code for one scope. */
export async function stepUpWithCode(
  client: HttpClient,
  scope: string,
  code: string,
  type: TwoFactorType,
): Promise<StepUpGrantDto> {
  const api = useAuthApi(client);
  return ensureData(await api.stepUpWithCode({ code, type, scope }));
}

/**
 * Run an action, and if the server asks for step-up, verify once and run it
 * again.
 *
 * ```ts
 * const file = await withStepUp(
 *   () => filesApi.downloadOriginal(id),
 *   (scope) => stepUpWithPasskey(client, scope),
 * );
 * ```
 *
 * Retries **once**. A second challenge for the same action means the grant did
 * not stick (wrong scope, single-use consumed by something else, clock skew) -
 * looping on that would just re-prompt the user forever without ever telling
 * them what is wrong.
 *
 * @param verify Called with the scope the server asked for. Return `null` to
 *   abort - `withStepUp` then replays the original challenge **in the shape it
 *   arrived in** (returned failures come back returned, thrown ones rethrown),
 *   so the caller sees "not done" rather than a silent no-op.
 */
export async function withStepUp<T>(
  action: () => Promise<T>,
  verify: (scope: string) => Promise<StepUpGrantDto | null>,
): Promise<T> {
  // ★ A failure reaches us two ways depending on the call site: `HttpClient`
  //   *resolves* `{succeeded: false, errorCode}`, the admin bridges *throw*.
  //   Handling only the throwing path would leave every raw-api caller with a
  //   silent no-op - the action just fails and the user is never offered the
  //   verification that would have let it through.
  const first = await runCatching(action);

  const challenge = isStepUpRequired(first.value) ? first.value : undefined;
  if (challenge === undefined) {
    return settle(first);
  }

  const scope = stepUpScopeOf(challenge);
  if (!scope) {
    return settle(first);
  }

  const grant = await verify(scope);
  if (!grant) {
    // Returning null means "not done". Surfacing the original challenge keeps
    // that visible to the caller instead of looking like a silent success.
    return settle(first);
  }

  // Retries once. A second challenge for the same action means the grant did
  // not stick - looping would re-prompt forever without ever saying why.
  return settle(await runCatching(action));
}

/** One attempt, with the thrown/returned distinction preserved. */
async function runCatching<T>(action: () => Promise<T>): Promise<Attempt<T>> {
  try {
    return { threw: false, value: await action() };
  } catch (error) {
    return { threw: true, value: error };
  }
}

/** Replay an attempt: rethrow what was thrown, return what was returned. */
function settle<T>(attempt: Attempt<T>): T {
  if (attempt.threw) {
    throw attempt.value;
  }
  return attempt.value as T;
}

type Attempt<T> = { threw: false; value: T } | { threw: true; value: unknown };

/**
 * Unwrap an envelope, throwing on failure.
 *
 * The ceremony is a chain: a failed `begin` must not fall through to
 * `navigator.credentials`, where it would surface as an opaque browser error
 * instead of the server's actual message.
 */
function ensureData<T>(result: { succeeded?: boolean; data?: T; message?: string }): T {
  if (result?.succeeded !== true || result.data == null) {
    throw new Error(result?.message || 'Step-up verification failed');
  }
  return result.data;
}
